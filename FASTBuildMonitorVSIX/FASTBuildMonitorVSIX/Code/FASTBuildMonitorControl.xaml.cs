//------------------------------------------------------------------------------
// Copyright 2016 Yassine Riahi and Liam Flookes. 
// Provided under a MIT License, see license file on github.
//------------------------------------------------------------------------------

//#define ENABLE_RENDERING_STATS

using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.IO;
using System.Diagnostics;
using System.Collections;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using EnvDTE;
using EnvDTE80;

namespace FASTBuildMonitorVSIX
{
    public partial class FASTBuildMonitorControl : UserControl
    {
        public const int LOG_VERSION = 1;

        private DispatcherTimer _timer;

        private static List<Rectangle> _bars = new List<Rectangle>();

        public static FASTBuildMonitorControl _StaticWindow = null;

        public FASTBuildMonitorControl()
        {
            _StaticWindow = this;

            // WPF init flow
            InitializeComponent();

            // Our internal init
            InitializeInternalState();
        }

        private void InitializeInternalState()
        {
            // Initialize text rendering
            TextUtils.StaticInitialize();

            // Time bar display
            _timeBar = new TimeBar(TimeBarCanvas);

            // System Graphs display
            _systemPerformanceGraphs = new SystemPerformanceGraphsCanvas(SystemGraphsCanvas);

            //events
            this.Loaded += FASTBuildMonitorControl_Loaded;

            EventsScrollViewer.PreviewMouseWheel += MainWindow_MouseWheel;
            EventsScrollViewer.MouseWheel += MainWindow_MouseWheel;
            MouseWheel += MainWindow_MouseWheel;
            EventsCanvas.MouseWheel += MainWindow_MouseWheel;

            EventsScrollViewer.PreviewMouseLeftButtonDown += EventsScrollViewer_MouseDown;
            EventsScrollViewer.MouseDown += EventsScrollViewer_MouseDown;
            MouseDown += EventsScrollViewer_MouseDown;
            EventsCanvas.MouseDown += EventsScrollViewer_MouseDown;

            EventsScrollViewer.PreviewMouseLeftButtonUp += EventsScrollViewer_MouseUp;
            EventsScrollViewer.MouseUp += EventsScrollViewer_MouseUp;
            MouseUp += EventsScrollViewer_MouseUp;
            EventsCanvas.MouseUp += EventsScrollViewer_MouseUp;

            EventsScrollViewer.PreviewMouseDoubleClick += EventsScrollViewer_MouseDoubleClick;
            EventsScrollViewer.MouseDoubleClick += EventsScrollViewer_MouseDoubleClick;

            OutputTextBox.PreviewMouseDoubleClick += OutputTextBox_PreviewMouseDoubleClick;
            OutputTextBox.MouseDoubleClick += OutputTextBox_PreviewMouseDoubleClick;
            OutputTextBox.PreviewKeyDown += OutputTextBox_KeyDown;
            OutputTextBox.KeyDown += OutputTextBox_KeyDown;
            OutputTextBox.LayoutUpdated += OutputTextBox_LayoutUpdated;

            OutputWindowComboBox.SelectionChanged += OutputWindowComboBox_SelectionChanged;

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                //update timer
                _timer = new DispatcherTimer();
                _timer.Tick += HandleTick;
                _timer.Interval = new TimeSpan(TimeSpan.TicksPerMillisecond * 16);
                _timer.Start();
            }));
        }

        /* Settings Tab Check boxes */
        private void checkBox_Checked(object sender, RoutedEventArgs e)
        {
            _systemPerformanceGraphs.SetVisibility((bool)(sender as CheckBox).IsChecked);
        }

        private void checkBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _systemPerformanceGraphs.SetVisibility((bool)(sender as CheckBox).IsChecked);
        }


        /* Output Window ESC */
        private void OutputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            if (e.Key == Key.Space)
            {
                if (_StaticWindow.OutputWindowComboBox.SelectedIndex != 0)
                {
                    _StaticWindow.OutputWindowComboBox.SelectedIndex = 0;
                }
            }
            else if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.C)
            {
                Clipboard.SetText(_StaticWindow.OutputTextBox.SelectedText);
            }                 
        }


        /* Output Window double click */

        private void OutputTextBox_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                TextBox tb = sender as TextBox;
                String doubleClickedWord = tb.SelectedText;

                if (tb.SelectionStart >= 0 && tb.SelectionLength > 0)
                {
                    try
                    {
                        string text = tb.Text;
                        int startLineIndex = text.LastIndexOf(Environment.NewLine, tb.SelectionStart) + Environment.NewLine.Length;
                        int endLineIndex = tb.Text.IndexOf(Environment.NewLine, tb.SelectionStart);

                        string selectedLineText = tb.Text.Substring(startLineIndex, endLineIndex - startLineIndex);
                        //Console.WriteLine("SelectedLine {0}", selectedLineText);

                        int startParenthesisIndex = selectedLineText.IndexOf('(');
                        int endParenthesisIndex = selectedLineText.IndexOf(')');

                        if (startParenthesisIndex > 0 && endParenthesisIndex > 0)
                        {
                            string filePath = selectedLineText.Substring(0, startParenthesisIndex);
                            string lineString = selectedLineText.Substring(startParenthesisIndex + 1, endParenthesisIndex - startParenthesisIndex - 1);

                            Int32 lineNumber = Int32.Parse(lineString);

                            //Console.WriteLine("File({0}) Line({1})", filePath, lineNumber);

                            Microsoft.VisualStudio.Shell.VsShellUtilities.OpenDocument(FASTBuildMonitorVSIX.FASTBuildMonitorPackage._instance, filePath);

                            DTE2 _dte = (DTE2)FASTBuildMonitorPackage._instance._dte;

                            //Console.WriteLine("Window: {0}", _dte.ActiveWindow.Caption);

                            EnvDTE.TextSelection sel = _dte.ActiveDocument.Selection as EnvDTE.TextSelection;

                            sel.StartOfDocument(false);
                            sel.EndOfDocument(true);
                            sel.GotoLine(lineNumber);

                            try
                            {
                                sel.ActivePoint.TryToShow(vsPaneShowHow.vsPaneShowCentered, null);
                            }
                            catch (System.Exception ex)
                            {
                                Console.WriteLine("Exception! " + ex.ToString());
                            }

                        }
                    }
                    catch (System.Exception ex)
                    {
                        Console.WriteLine("Exception! " + ex.ToString());
                    }
                }
            }
        }

        /* Output Window Filtering & Combo box management */

        public class OutputFilterItem
        {
            public OutputFilterItem(string name)
            {
                _name = name;
            }

            public OutputFilterItem(BuildEvent buildEvent)
            {
                _buildEvent = buildEvent;
            }

            private BuildEvent _internalBuildEvent = null;

            public BuildEvent _buildEvent
            {
                get { return _internalBuildEvent; }

                private set { _internalBuildEvent = value; }
            }

            public string _internalName = "";
            public string _name
            {
                get
                {
                    string result;

                    if (_buildEvent != null)
                    {
                        result = _buildEvent._name.Substring(1, _buildEvent._name.Length - 2);
                    }
                    else
                    {
                        // fallback
                        result = _internalName;
                    }

                    const int charactersToDisplay = 50;

                    if (result.Length > charactersToDisplay)
                    {
                        result = result.Substring(result.IndexOf('\\', result.Length - charactersToDisplay));
                    }

                    return result;
                }

                set
                {
                    _internalName = value;
                }
            }
        }

        static public ObservableCollection<OutputFilterItem> _outputComboBoxFilters;

        void ResetOutputWindowCombox()
        {
            if (_outputComboBoxFilters != null)
            {
                _outputComboBoxFilters.Clear();
            }
            else
            {
                _outputComboBoxFilters = new ObservableCollection<OutputFilterItem>();
            }

            _outputComboBoxFilters.Add(new OutputFilterItem("ALL"));

            OutputWindowComboBox.ItemsSource = _outputComboBoxFilters;

            OutputWindowComboBox.SelectedIndex = 0;
        }


        void AddOutputWindowFilterItem(BuildEvent buildEvent)
        {
            _outputComboBoxFilters.Add(new OutputFilterItem(buildEvent));

            RefreshOutputTextBox();
        }

        private void OutputWindowComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshOutputTextBox();
        }

        void RefreshOutputTextBox()
        {
            OutputTextBox.Clear();


            if (OutputWindowComboBox.SelectedIndex >= 0)
            {
                OutputFilterItem selectedFilter = _outputComboBoxFilters[OutputWindowComboBox.SelectedIndex];

                foreach (OutputFilterItem filter in _outputComboBoxFilters)
                {
                    if (filter._buildEvent != null && (selectedFilter._buildEvent == null || filter._buildEvent == selectedFilter._buildEvent))
                    {
                        OutputTextBox.AppendText(filter._buildEvent._outputMessages + "\n");
                    }
                }
            }

            // Since we changed the text inside the text box we now require a layout update to refresh
            // the internal state of the UIControl
            _outputTextBoxPendingLayoutUpdate = true;

            _StaticWindow.OutputTextBox.UpdateLayout();
        }

        void ChangeOutputWindowComboBoxSelection(BuildEvent buildEvent)
        {
            int index = 0;

            foreach (OutputFilterItem filter in _outputComboBoxFilters)
            {
                if (filter._buildEvent == buildEvent)
                {
                    OutputWindowComboBox.SelectedIndex = index;
                    break;
                }
                index++;
            }
        }


        /* Tab Control management */

        public enum eTABs
        {
            TAB_TimeLine = 0,
            TAB_OUTPUT
        }

        private void MyTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

            if (e.Source is TabControl)
            {
                TabControl tabControl = e.Source as TabControl;

                if (tabControl.SelectedIndex == (int)eTABs.TAB_OUTPUT)
                {
                    _StaticWindow.OutputTextBox.UpdateLayout();

                    _outputTextBoxPendingLayoutUpdate = true;
                }
            }
        }

        private void FASTBuildMonitorControl_Loaded(object sender, RoutedEventArgs e)
        {
            Image image = new Image();
            image.Source = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.TimeLineTabIcon);
            image.Margin = new Thickness(5, 5, 5, 5);
            image.Width = 20.0f;
            image.Height = 20.0f;
            image.ToolTip = new ToolTip();
            ((ToolTip)image.ToolTip).Content = "Events TimeLine";
            TabItemTimeBar.Header = image;

            image = new Image();
            image.Source = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.TextOutputTabIcon);
            image.Margin = new Thickness(5, 5, 5, 5);
            image.Width = 20.0f;
            image.Height = 20.0f;
            image.ToolTip = new ToolTip();
            ((ToolTip)image.ToolTip).Content = "Output Window";
            TabItemOutput.Header = image;

            image = new Image();
            image.Source = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.SettingsTabIcon);
            image.Margin = new Thickness(5, 5, 5, 5);
            image.Width = 20.0f;
            image.Height = 20.0f;
            image.ToolTip = new ToolTip();
            ((ToolTip)image.ToolTip).Content = "Settings";
            TabItemSettings.Header = image;


            string versionText = "v?";
            string authorsText = "Yassine Riahi & Liam Flookes";
            string packageNameText = "FASTBuildMonitorVSIX";

            // Find out the VSIX info
            FASTBuildMonitorPackage.VSIXPackageInformation packageInfo = FASTBuildMonitorPackage._instance != null ? FASTBuildMonitorPackage._instance.GetCurrentVSIXPackageInformation() : null;

            if (packageInfo != null)
            {
                versionText = packageInfo._version.ToString();
                authorsText = packageInfo._authors;
                packageNameText = packageInfo._packageName;
            }

            AboutTextBlock.Text = string.Format("{0} v{1}\nCopyright (c) 2016 {2}.\nProvided under a MIT License, see license file on github.", packageNameText, versionText, authorsText);
        }


        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        static public bool _outputTextBoxPendingLayoutUpdate = false;
        private void OutputTextBox_LayoutUpdated(object sender, EventArgs e)
        {
            _outputTextBoxPendingLayoutUpdate = false;
        }

        /* Double-click handling */
        public class HitTestResult
        {
            public HitTestResult(BuildHost host, CPUCore core, BuildEvent ev)
            {
                _host = host;
                _core = core;
                _event = ev;
            }

            public BuildHost _host;
            public CPUCore _core;
            public BuildEvent _event;
        }

        private void EventsScrollViewer_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (_StaticWindow.MyTabControl.SelectedIndex == (int)eTABs.TAB_TimeLine)
                {
                    Point mousePosition = e.GetPosition(EventsScrollViewer);

                    mousePosition.X += EventsScrollViewer.HorizontalOffset;
                    mousePosition.Y += EventsScrollViewer.VerticalOffset;

                    HitTestResult result = HitTest(mousePosition);

                    if (result != null && result._event !=null)
                    {
                        //Console.WriteLine("\n\nHost: " + result._host._name);
                        //Console.WriteLine("core: " + result._core._coreIndex);
                        //Console.WriteLine("event name: " + result._event._name);

                        string filename = result._event._name.Substring(1, result._event._name.Length - 2);

                        result._event.HandleDoubleClickEvent();

                        e.Handled = true;
                    }
                }
            }
        }

        /* Mouse Pan handling */
        private static bool _isPanning = false;
        private static Point _panReferencePosition;

        private void EventsScrollViewer_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = false;

            if (e.ChangedButton == MouseButton.Left)
            {
                if (_StaticWindow.MyTabControl.SelectedIndex == (int)eTABs.TAB_TimeLine)
                {
                    Rect viewPort = new Rect(0.0f, 0.0f, EventsScrollViewer.ViewportWidth, EventsScrollViewer.ViewportHeight);

                    Point mousePosition = e.GetPosition(EventsScrollViewer);

                    if (viewPort.Contains(mousePosition))
                    {
                        _panReferencePosition = mousePosition;

                        StartPanning();

                        e.Handled = true;
                    }
                }
            }
        }

        private void EventsScrollViewer_MouseUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = false;

            if (e.ChangedButton == MouseButton.Left && _isPanning)
            {
                Rect viewPort = new Rect(0.0f, 0.0f, EventsScrollViewer.ViewportWidth, EventsScrollViewer.ViewportHeight);

                Point mousePosition = e.GetPosition(EventsScrollViewer);

                if (viewPort.Contains(mousePosition))
                {
                    StopPanning();

                    e.Handled = true;
                }
            }
        }

        private void StartPanning()
        {
            this.Cursor = Cursors.SizeAll;
            _isPanning = true;
        }

        private void StopPanning()
        {
            this.Cursor = Cursors.Arrow;
            _isPanning = false;
        }

        private void UpdateMousePanning()
        {
            if (_isPanning)
            {
                Point currentMousePosition = Mouse.GetPosition(EventsScrollViewer);

                Rect viewPort = new Rect(0.0f, 0.0f, EventsScrollViewer.ViewportWidth, EventsScrollViewer.ViewportHeight);

                if (viewPort.Contains(currentMousePosition))
                {
                    Vector posDelta = (currentMousePosition - _panReferencePosition) * -1.0f;

                    _panReferencePosition = currentMousePosition;

                    double newVerticalOffset = EventsScrollViewer.VerticalOffset + posDelta.Y;
                    newVerticalOffset = Math.Min(newVerticalOffset, EventsCanvas.Height - EventsScrollViewer.ViewportHeight);
                    newVerticalOffset = Math.Max(0.0f, newVerticalOffset);

                    double newHorizontaOffset = EventsScrollViewer.HorizontalOffset + posDelta.X;
                    newHorizontaOffset = Math.Min(newHorizontaOffset, EventsCanvas.Width - EventsScrollViewer.ViewportWidth);
                    newHorizontaOffset = Math.Max(0.0f, newHorizontaOffset);


                    //Console.WriteLine("Mouse (X: {0}, Y: {1})", currentMousePosition.X, currentMousePosition.Y);
                    //Console.WriteLine("Pan (X: {0}, Y: {1})", newHorizontaOffset, newVerticalOffset);

                    EventsScrollViewer.ScrollToHorizontalOffset(newHorizontaOffset);
                    TimeBarScrollViewer.ScrollToHorizontalOffset(newHorizontaOffset);
                    SystemGraphsScrollViewer.ScrollToHorizontalOffset(newHorizontaOffset);

                    EventsScrollViewer.ScrollToVerticalOffset(newVerticalOffset);
                    CoresScrollViewer.ScrollToVerticalOffset(newVerticalOffset);
                }
                else
                {
                    StopPanning();
                }
            }
        }

        /* Mouse Scrolling handling */
        private static Boolean _autoScrolling = true;

        private void ScrollViewer_ScrollChanged(Object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentWidthChange == 0)
            {
                if (EventsScrollViewer.HorizontalOffset == EventsScrollViewer.ScrollableWidth)
                {
                    _autoScrolling = true;
                }
                else
                {
                    _autoScrolling = false;
                }
            }

            if (_autoScrolling && e.ExtentWidthChange != 0)
            {
                EventsScrollViewer.ScrollToHorizontalOffset(EventsScrollViewer.ExtentWidth);

                TimeBarScrollViewer.ScrollToHorizontalOffset(EventsScrollViewer.ExtentWidth);

                SystemGraphsScrollViewer.ScrollToHorizontalOffset(EventsScrollViewer.ExtentWidth);
            }

            if (e.VerticalChange != 0)
            {
                //Console.WriteLine("Scroll V Offset: (cores: {0} - events: {1})", CoresScrollViewer.VerticalOffset, EventsScrollViewer.VerticalOffset);

                _StaticWindow.CoresScrollViewer.ScrollToVerticalOffset(EventsScrollViewer.VerticalOffset);

                UpdateViewport();
            }

            if (e.HorizontalChange != 0)
            {
                _StaticWindow.TimeBarScrollViewer.ScrollToHorizontalOffset(EventsScrollViewer.HorizontalOffset);

                _StaticWindow.SystemGraphsScrollViewer.ScrollToHorizontalOffset(EventsScrollViewer.HorizontalOffset);

                UpdateViewport();
            }
        }

        /* Mouse Zoom handling */
        static public double _zoomFactor = 1.0f;
        static private double _zoomFactorOld = 0.1f;

        void MainWindow_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            //handle the case where we can receive many events between 2 frames
            if (_zoomFactorOld == _zoomFactor)
            {
                _zoomFactorOld = _zoomFactor;
            }

            double zoomMultiplier = 1.0f;

            if (_zoomFactor > 3.0f)
            {
                if (_zoomFactor < 7.0f)
                {
                    zoomMultiplier = 3.0f;
                }
                else
                {
                    zoomMultiplier = 6.0f;
                }
            }
            else if (_zoomFactor < 0.5f)
            {
                if (_zoomFactor > 0.1f)
                {
                    zoomMultiplier = 0.3f;
                }
                else
                {
                    zoomMultiplier = 0.05f;
                }
            }

            //Accumulate some value
            double oldZoomValue = _zoomFactor;

            _zoomFactor += zoomMultiplier * (double)e.Delta / 1000.0f;
            _zoomFactor = Math.Min(_zoomFactor, 30.0f);
            _zoomFactor = Math.Max(_zoomFactor, 0.05f);

            if (oldZoomValue != _zoomFactor)
            {
                // if the zoom has changed the kick a new render update
                SetConditionalRenderUpdateFlag(true);
            }

            //Console.WriteLine("Zoom: {0} (multiplier: {1})", _zoomFactor, zoomMultiplier);

            //disable auto-scrolling when we are zooming
            _autoScrolling = false;

            e.Handled = true;
        }

        private void UpdateZoomTargetPosition()
        {
            if (_zoomFactorOld != _zoomFactor)
            {
                Point mouseScreenPosition = Mouse.GetPosition(_StaticWindow.EventsCanvas);

                //Find out the time position the mouse (canvas relative) was at pre-zoom
                double mouseTimePosition = mouseScreenPosition.X / (_zoomFactorOld * pix_per_second);

                double screenSpaceMousePositionX = mouseScreenPosition.X - EventsScrollViewer.HorizontalOffset;

                //Determine the new canvas relative mouse position post-zoom
                double newMouseCanvasPosition = mouseTimePosition * _zoomFactor * pix_per_second;

                double newHorizontalScrollOffset = Math.Max(0.0f, newMouseCanvasPosition - screenSpaceMousePositionX);

                _StaticWindow.EventsScrollViewer.ScrollToHorizontalOffset(newHorizontalScrollOffset);
                _StaticWindow.TimeBarScrollViewer.ScrollToHorizontalOffset(newHorizontalScrollOffset);
                _StaticWindow.SystemGraphsScrollViewer.ScrollToHorizontalOffset(newHorizontalScrollOffset);

                _zoomFactorOld = _zoomFactor;
            }
        }


        /* Input File IO feature */
        private FileStream _fileStream = null;
        private Int64 _fileStreamPosition = 0;
        private List<byte> _fileBuffer = new System.Collections.Generic.List<byte>();

        private bool CanRead()
        {
            return _fileStream != null && _fileStream.CanRead;
        }

        private bool HasFileContentChanged()
        {
            bool bFileChanged = false;

            if (_fileStream.Length < _fileStreamPosition)
            {
                // detect if the current file has been overwritten with less data
                bFileChanged = true;
            }
            else if (_fileBuffer.Count > 0)
            {
                // detect if the current file has been overwritten with different data

                int numBytesToCompare = Math.Min(_fileBuffer.Count, 256);

                byte[] buffer = new byte[numBytesToCompare];

                _fileStream.Seek(0, SeekOrigin.Begin);

                int numBytesRead = _fileStream.Read(buffer, 0, numBytesToCompare);
                Debug.Assert(numBytesRead == numBytesToCompare, "Could not read the expected amount of data from the log file...!");

                for (int i = 0; i < numBytesToCompare; ++i)
                {
                    if (buffer[i] != _fileBuffer[i])
                    {
                        bFileChanged = true;
                        break;
                    }
                }
            }

            return bFileChanged;
        }

        private bool BuildRestarted()
        {
            return CanRead() && HasFileContentChanged();
        }

        private void ResetState()
        {
            _fileStreamPosition = 0;
            _fileStream.Seek(0, SeekOrigin.Begin);

            _fileBuffer.Clear();

            _buildRunningState = eBuildRunningState.Ready;
            _buildStatus = eBuildStatus.AllClear;

            _buildStartTimeMS = 0;
            _latestTimeStampMS = 0;

            _hosts.Clear();
            _localHost = null;

            _lastProcessedPosition = 0;
            _bPreparingBuildsteps = false;

            _StaticWindow.EventsCanvas.Children.Clear();
            _StaticWindow.CoresCanvas.Children.Clear();

            // Start by adding a local host
            _localHost = new BuildHost(_cLocalHostName);
            _hosts.Add(_cLocalHostName, _localHost);

            // Always add the prepare build steps event first
            BuildEvent buildEvent = new BuildEvent(_cPrepareBuildStepsText, 0);
            _localHost.OnStartEvent(buildEvent);
            _bPreparingBuildsteps = true;

            // Reset the Output window text
            OutputTextBox.Text = "";

            // Change back the tabcontrol to the TimeLine automatically
            _StaticWindow.MyTabControl.SelectedIndex = (int)eTABs.TAB_TimeLine;

            ResetOutputWindowCombox();

            // progress status
            UpdateBuildProgress(0.0f);
            StatusBarProgressBar.Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#FF06B025"));

            // reset to autoscrolling ON
            _autoScrolling = true;

            // reset our zoom levels
            _zoomFactor = 1.0f;
            _zoomFactorOld = 0.1f;

            // target pid
            _targetPID = 0;
            _lastTargetPIDCheckTimeMS = 0;

            // live build session state
            _isLiveSession = false;

            // graphs
            SystemGraphsCanvas.Children.Clear();
            _systemPerformanceGraphs = new SystemPerformanceGraphsCanvas(SystemGraphsCanvas);

            // allow a free render update on the first frame after the reset
            SetConditionalRenderUpdateFlag(true);

            // reset the cached SteppedBuildTime value
            _sPreviousSteppedBuildTimeMS = 0;
        }

        /* Build State Management */
        public enum eBuildRunningState
        {
            Ready = 0,
            Running,
        }

        private static eBuildRunningState _buildRunningState;

        public void UpdateStatusBar()
        {
            switch (_buildRunningState)
            {
                case eBuildRunningState.Ready:
                    StatusBarBuildStatus.Text = "Ready";
                    break;
                case eBuildRunningState.Running:
                    StatusBarBuildStatus.Text = "Running";
                    break;
            }

            int numCores = 0;
            foreach (DictionaryEntry entry in _hosts)
            {
                BuildHost host = entry.Value as BuildHost;

                if (host._name.Contains(_cLocalHostName))
                {
                    numCores += host._cores.Count;
                }
                else
                {
                    numCores += host._cores.Count - 1;
                }
            }

            StatusBarDetails.Text = string.Format("{0} Agents - {1} Cores", _hosts.Count, numCores);
        }

        public enum eBuildStatus
        {
            AllClear = 0,
            HasWarnings,
            HasErrors,
        }
        private static eBuildStatus _buildStatus;

        public void UpdateBuildStatus(BuildEventState jobResult)
        {
            eBuildStatus newBuildStatus = _buildStatus;

            switch (jobResult)
            {
                case BuildEventState.FAILED:
                    newBuildStatus = eBuildStatus.HasErrors;
                    break;

                case BuildEventState.TIMEOUT:
                case BuildEventState.SUCCEEDED_COMPLETED_WITH_WARNINGS:
                    if ((int)_buildStatus < (int)eBuildStatus.HasWarnings)
                    {
                        newBuildStatus = eBuildStatus.HasWarnings;
                    }
                    break;
            }

            if (_buildStatus != newBuildStatus)
            {
                switch (newBuildStatus)
                {
                    case eBuildStatus.HasErrors:
                        StatusBarProgressBar.Foreground = Brushes.Red;
                        break;
                    case eBuildStatus.HasWarnings:
                        StatusBarProgressBar.Foreground = Brushes.Yellow;
                        break;
                }

                _buildStatus = newBuildStatus;
            }
        }

        static private float _currentProgressPCT = 0.0f;
        ToolTip _statusBarProgressToolTip = new ToolTip();

        public void UpdateBuildProgress(float progressPCT)
        {
            _currentProgressPCT = progressPCT;

            StatusBarBuildTime.Text = string.Format("Duration: {0}", GetTimeFormattedString2(GetCurrentBuildTimeMS()));

            StatusBarProgressBar.Value = _currentProgressPCT;


            StatusBarProgressBar.ToolTip = _statusBarProgressToolTip;

            _statusBarProgressToolTip.Content = string.Format("{0:0.00}%", _currentProgressPCT);
        }


        /* Target Process ID monitoring */
        private static int _targetPID = 0;

        private static bool _isLiveSession = false;

        private static bool IsTargetProcessRunning(int pid)
        {
            bool bIsRunning = false;

            System.Diagnostics.Process[] processlist = System.Diagnostics.Process.GetProcesses();
            foreach (System.Diagnostics.Process proc in processlist)
            {
                if (proc.Id == pid)
                {
                    bIsRunning = true;
                    break;
                }
            }

            return bIsRunning;
        }

        private static Int64 _lastTargetPIDCheckTimeMS = 0;
        const Int64 cTargetPIDCheckPeriodMS = 1 * 1000;

        private static bool PollIsTargetProcessRunning()
        {
            // assume the process is running
            bool bIsRunning = true;

            if (_targetPID != 0 && _buildRunningState == eBuildRunningState.Running)
            {
                Int64 currentTimeMS = GetCurrentSystemTimeMS();

                if ((currentTimeMS - _lastTargetPIDCheckTimeMS) > cTargetPIDCheckPeriodMS)
                {
                    bIsRunning = IsTargetProcessRunning(_targetPID);

                    _lastTargetPIDCheckTimeMS = currentTimeMS;
                }
            }

            return bIsRunning;
        }

        /* Time management */
        private static Int64 _buildStartTimeMS = 0;
        private static Int64 _latestTimeStampMS = 0;

        private static Int64 ConvertFileTimeToMS(Int64 fileTime)
        {
            // FileTime: Contains a 64-bit value representing the number of 100-nanosecond intervals since January 1, 1601 (UTC).
            return fileTime / (10 * 1000);
        }

        const double cTimeStepMS = 500.0f;

        public static Int64 GetCurrentSystemTimeMS()
        {
            Int64 currentTimeMS = DateTime.Now.ToFileTime() / (10 * 1000);

            return currentTimeMS;
        }

        private static Int64 _sPreviousSteppedBuildTimeMS = 0;

        public static Int64 GetCurrentBuildTimeMS(bool bUseTimeStep = false)
        {
            Int64 elapsedBuildTime = -_buildStartTimeMS;

            if (_buildRunningState == eBuildRunningState.Running)
            {
                Int64 currentTimeMS = GetCurrentSystemTimeMS();

                elapsedBuildTime += currentTimeMS;

                if (bUseTimeStep)
                {
                    elapsedBuildTime = (Int64)(Math.Truncate(elapsedBuildTime / cTimeStepMS) * cTimeStepMS);

                    if (_sPreviousSteppedBuildTimeMS != elapsedBuildTime)
                    {
                        // if we have advanced in terms of stepped build Time than force a render update
                        _StaticWindow.SetConditionalRenderUpdateFlag(true);

                        _sPreviousSteppedBuildTimeMS = elapsedBuildTime;
                    }
                }
            }
            else
            {
                elapsedBuildTime += _latestTimeStampMS;
            }

            return elapsedBuildTime;
        }

        private static Int64 RegisterNewTimeStamp(Int64 fileTime)
        {
            _latestTimeStampMS = ConvertFileTimeToMS(fileTime);

            return _latestTimeStampMS;
        }

        public class CPUCore : Canvas
        {
            public BuildHost _parent;
            public int _coreIndex = 0;
            public BuildEvent _activeEvent = null;
            public List<BuildEvent> _completedEvents = new List<BuildEvent>();

            public double _x = 0.0f;
            public double _y = 0.0f;

            //WPF stuff
            public TextBlock _textBlock = new TextBlock();
            public static Image _sLODImage = null;
            public ToolTip _toolTip = new ToolTip();
            public Line _lineSeparator = new Line();

            //LOD handling
            public bool _isLODBlockActive = false;
            public Rect _currentLODRect = new Rect();
            public int _currentLODCount = 0;


            public void StartNewLODBlock(Rect rect)
            {
                // Make sure the previous block is closed 
                Debug.Assert(_isLODBlockActive == false && _currentLODCount == 0);

                _currentLODRect = rect;
                _currentLODCount = 1;
                _isLODBlockActive = true;
            }

            public void CloseCurrentLODBlock()
            {
                // Make sure the current block has been started previously
                Debug.Assert(_isLODBlockActive == true && _currentLODCount > 0);

                _currentLODCount = 0;
                _isLODBlockActive = false;
            }

            public bool IsLODBlockActive()
            {
                return _isLODBlockActive;
            }

            public void UpdateCurrentLODBlock(double newWitdh)
            {
                // Make sure the current block has been started previously
                Debug.Assert(_isLODBlockActive == true && _currentLODCount > 0);

                _currentLODCount++;

                _currentLODRect.Width = newWitdh;
            }

            //ToolTip management
            private class VisibleElement // Represents an element (event or LOD block) that has been successfully rendered in the last frame
            {
                public VisibleElement(Rect rect, string toolTipText, BuildEvent buildEvent)
                {
                    _rect = rect;
                    _toolTipText = toolTipText;
                    _buildEvent = buildEvent;
                }

                public bool HitTest(Point localMousePosition)
                {
                    return _rect.Contains(localMousePosition);
                }

                public Rect _rect;  // boundaries of the element
                public string _toolTipText;
                public BuildEvent _buildEvent;
            }

            private List<VisibleElement> _visibleElements = new List<VisibleElement>();

            public void AddVisibleElement(Rect rect, string toolTipText, BuildEvent buildEvent = null)
            {
                _visibleElements.Add(new VisibleElement(rect, toolTipText, buildEvent));
            }

            public void ClearAllVisibleElements()
            {
                _visibleElements.Clear();
            }


            //......................
            public CPUCore(BuildHost parent, int coreIndex)
            {
                _parent = parent;

                _coreIndex = coreIndex;

                _textBlock.Text = string.Format("{0} (Core # {1})", parent._name, _coreIndex);

                _StaticWindow.CoresCanvas.Children.Add(_textBlock);

                _StaticWindow.EventsCanvas.Children.Add(this);


                this.Height = pix_height;

                if (_sLODImage == null)
                {

                    _sLODImage = new Image();
                    _sLODImage.Source = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.LODBlock);
                }

                this.ToolTip = _toolTip;
            }

            public bool ScheduleEvent(BuildEvent ev)
            {
                bool bOK = _activeEvent == null;

                if (bOK)
                {
                    _activeEvent = ev;

                    _activeEvent.Start(this);
                }

                return bOK;
            }

            public bool UnScheduleEvent(Int64 timeCompleted, string eventName, BuildEventState jobResult, string outputMessages, bool bForce = false)
            {
                bool bOK = (_activeEvent != null && (_activeEvent._name == eventName || bForce));

                if (bOK)
                {
                    if (!bForce && outputMessages.Length > 0)
                    {
                        _activeEvent.SetOutputMessages(outputMessages);
                    }

                    _activeEvent.Stop(timeCompleted, jobResult);

                    _completedEvents.Add(_activeEvent);

                    _activeEvent = null;
                }

                return bOK;
            }

            protected override void OnRender(DrawingContext dc)
            {
                // First let's reset the list of visible elements since we will be recalculating it
                ClearAllVisibleElements();

                foreach (BuildEvent ev in _completedEvents)
                {
                    ev.OnRender(dc);
                }

                // we need to close the currently active LOD block before rendering the active event
                if (IsLODBlockActive())
                {
                    // compute the absolute Rect given the origin of the current core
                    Rect absoluteRect = new Rect(_x + _currentLODRect.X, _y + _currentLODRect.Y, _currentLODRect.Width, _currentLODRect.Height);

                    if (IsObjectVisible(absoluteRect))
                    {
                        VisualBrush brush = new VisualBrush();
                        brush.Visual = _sLODImage;
                        brush.Stretch = Stretch.None;
                        brush.TileMode = TileMode.Tile;
                        brush.AlignmentY = AlignmentY.Top;
                        brush.AlignmentX = AlignmentX.Left;
                        brush.ViewportUnits = BrushMappingMode.Absolute;
                        brush.Viewport = new Rect(0, 0, 40, 20);

                        dc.DrawRectangle(brush, new Pen(Brushes.Black, 1), _currentLODRect);

                        AddVisibleElement(_currentLODRect, string.Format("{0} events", _currentLODCount));
                    }

                    CloseCurrentLODBlock();
                }

                if (_activeEvent != null)
                {
                    _activeEvent.OnRender(dc);
                }
            }

            public HitTestResult HitTest(Point localMousePosition)
            {
                foreach (VisibleElement element in _visibleElements)
                {
                    if (element.HitTest(localMousePosition))
                    {
                        return new HitTestResult(this._parent, this, element._buildEvent);
                    }
                }

                return null;
            }

            public bool UpdateToolTip(Point localMousePosition)
            {
                foreach (VisibleElement element in _visibleElements)
                {
                    if (element.HitTest(localMousePosition))
                    {
                        _toolTip.Content = element._toolTipText;

                        return true;
                    }
                }

                return false;
            }

            public void RenderUpdate(ref double X, ref double Y)
            {
                // WPF Layout update
                Canvas.SetLeft(_textBlock, X);
                Canvas.SetTop(_textBlock, Y + 2);

                if (_x != X)
                {
                    Canvas.SetLeft(this, X);
                    _x = X;
                }

                if (_y != Y)
                {
                    Canvas.SetTop(this, Y);
                    _y = Y;
                }

                double relX = 0.0f;

                foreach (BuildEvent ev in _completedEvents)
                {
                    ev.RenderUpdate(ref relX, 0);
                }

                if (_activeEvent != null)
                {
                    _activeEvent.RenderUpdate(ref relX, 0);
                }


                X = this.Width = X + relX + 40.0f;

                Y += 25;
            }
        }


        public class BuildHost
        {
            public string _name;
            public List<CPUCore> _cores = new List<CPUCore>();
            public bool bLocalHost = false;

            //WPF stuff
            public Line _lineSeparator = new Line();

            public BuildHost(string name)
            {
                _name = name;

                bLocalHost = name.Contains(_cLocalHostName);

                // Add line separator
                _StaticWindow.CoresCanvas.Children.Add(_lineSeparator);

                _lineSeparator.Stroke = new SolidColorBrush(Colors.LightGray);
                _lineSeparator.StrokeThickness = 1;
                DoubleCollection dashes = new DoubleCollection();
                dashes.Add(2);
                dashes.Add(2);
                _lineSeparator.StrokeDashArray = dashes;

                _lineSeparator.X1 = 10;
                _lineSeparator.X2 = 300;
            }

            public void OnStartEvent(BuildEvent newEvent)
            {
                bool bAssigned = false;
                for (int i = 0; i < _cores.Count; ++i)
                {
                    if (_cores[i].ScheduleEvent(newEvent))
                    {
                        //Console.WriteLine("START {0} (Core {1}) [{2}]", _name, i, newEvent._name);
                        bAssigned = true;
                        break;
                    }
                }

                // we discovered a new core
                if (!bAssigned)
                {
                    CPUCore core = new CPUCore(this, _cores.Count);

                    core.ScheduleEvent(newEvent);

                    //Console.WriteLine("START {0} (Core {1}) [{2}]", _name, _cores.Count, newEvent._name);

                    _cores.Add(core);
                }
            }

            public void OnCompleteEvent(Int64 timeCompleted, string eventName, BuildEventState jobResult, string outputMessages)
            {
                for (int i = 0; i < _cores.Count; ++i)
                {
                    if (_cores[i].UnScheduleEvent(timeCompleted, eventName, jobResult, outputMessages))
                    {
                        break;
                    }
                }
            }

            public HitTestResult HitTest(Point mousePosition)
            {
                HitTestResult result = null;

                foreach (CPUCore core in _cores)
                {
                    double x = Canvas.GetLeft(core);
                    double y = Canvas.GetTop(core);

                    Rect rect = new Rect(x, y, core.Width, core.Height);

                    if (rect.Contains(mousePosition))
                    {
                        Point localMousePosition = new Point(mousePosition.X - x, mousePosition.Y - y);
                        result = core.HitTest(localMousePosition);

                        break;
                    }
                }

                return result;
            }

            public bool UpdateToolTip(Point mousePosition)
            {
                foreach (CPUCore core in _cores)
                {
                    double x = Canvas.GetLeft(core);
                    double y = Canvas.GetTop(core);

                    Rect rect = new Rect(x, y, core.Width, core.Height);

                    if (rect.Contains(mousePosition))
                    {
                        Point localMousePosition = new Point(mousePosition.X - x, mousePosition.Y - y);
                        return core.UpdateToolTip(localMousePosition);
                    }
                }

                return false;

            }

            public void RenderUpdate(double X, ref double Y)
            {
                double maxX = 0.0f;

                //update all cores
                foreach (CPUCore core in _cores)
                {
                    double localX = X;

                    core.RenderUpdate(ref localX, ref Y);

                    maxX = Math.Max(maxX, localX);
                }

                //adjust the dynamic line separator
                _lineSeparator.Y1 = _lineSeparator.Y2 = Y + 10;

                Y += 20;

                UpdateEventsCanvasMaxSize(X, Y);
            }
        }

        public enum BuildEventState
        {
            UNKOWN = 0,
            IN_PROGRESS,
            FAILED,
            SUCCEEDED_COMPLETED,
            SUCCEEDED_COMPLETED_WITH_WARNINGS,
            SUCCEEDED_CACHED,
            SUCCEEDED_PREPROCESSED,
            TIMEOUT
        }

        public const double pix_space_between_events = 2;
        public const double pix_per_second = 20.0f;
        public const double pix_height = 20;
        public const double pix_LOD_Threshold = 2.0f;

        const double toolTip_TimeThreshold = 1.0f; //in Seconds


        public static BitmapImage GetBitmapImage(System.Drawing.Bitmap bitmap)
        {
            BitmapImage bitmapImage = new BitmapImage();

            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                memory.Position = 0;
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
            }

            return bitmapImage;
        }

        public static GifBitmapDecoder GetGifBitmapDecoder(string gifResourceName)
        {
            GifBitmapDecoder bitMapDecoder = null;

            object obj = FASTBuildMonitorVSIX.Resources.Images.ResourceManager.GetObject(gifResourceName, FASTBuildMonitorVSIX.Resources.Images.Culture);

            if (obj != null)
            {
                System.Drawing.Bitmap bitmapObject = obj as System.Drawing.Bitmap;

                MemoryStream memory = new MemoryStream();
                bitmapObject.Save(memory, System.Drawing.Imaging.ImageFormat.Gif);
                memory.Position = 0;
                bitMapDecoder = new GifBitmapDecoder(memory, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
            }

            return bitMapDecoder;
        }

        public class BuildEvent
        {
            // Attributes
            public CPUCore _core = null;

            public Int64 _timeStarted = 0;    // in ms
            public Int64 _timeFinished = 0;    // in ms

            public string _name;
            public string _fileName; // extracted from the full name

            public BuildEventState _state;

            public string _outputMessages;

            public string _toolTipText;

            // WPF rendering stuff
            public ImageBrush _brush = null;

            // Coordinates
            public Rect _bordersRect;
            public Rect _progressRect;

            // LOD/Culling
            public bool _isInLowLOD = false;
            public bool _isDirty = false;

            // Static Members
            public static bool _sbInitialized = false;
            public static ImageBrush _sSuccessCodeBrush = new ImageBrush();
            public static ImageBrush _sSuccessNonCodeBrush = new ImageBrush();
            public static ImageBrush _sSuccessPreprocessedBrush = new ImageBrush();
            public static ImageBrush _sSuccessCachedBrush = new ImageBrush();
            public static ImageBrush _sFailedBrush = new ImageBrush();
            public static ImageBrush _sTimeoutBrush = new ImageBrush();
            public static ImageBrush _sRunningBrush = new ImageBrush();

            // Constants
            private const int _cTextLabeloffset_X = 4;
            private const int _cTextLabeloffset_Y = 4;
            private const double _cMinTextLabelWidthThreshold = 50.0f; // The minimum element width to be eligible for text display
            private const double _cMinDotDotDotWidthThreshold = 20.0f; // The minimum element width to be eligible for a "..." display

            public static void StaticInitialize()
            {
                _sSuccessCodeBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Success_code);
                _sSuccessNonCodeBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Success_noncode);
                _sSuccessPreprocessedBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Success_preprocessed);
                _sSuccessCachedBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Success_cached);
                _sFailedBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Failed);
                _sTimeoutBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Timeout);
                _sRunningBrush.ImageSource = GetBitmapImage(FASTBuildMonitorVSIX.Resources.Images.Running);

                _sbInitialized = true;
            }

            public BuildEvent(string name, Int64 timeStarted)
            {
                // Lazy initialize static resources
                if (!_sbInitialized)
                {
                    StaticInitialize();
                }

                _name = name;

                _toolTipText = _name.Replace("\"", "");

                _fileName = System.IO.Path.GetFileName(_name.Replace("\"", ""));

                _timeStarted = timeStarted;

                _state = BuildEventState.IN_PROGRESS;
            }

            public void SetOutputMessages(string outputMessages)
            {
                char[] newLineSymbol = new char[1];
                newLineSymbol[0] = (char)12;

                // Todo: Remove this crap!
                _outputMessages = outputMessages.Replace(new string(newLineSymbol), Environment.NewLine);
            }

            public void Start(CPUCore core)
            {
                _core = core;

                _brush = _sRunningBrush;

                _toolTipText = "BUILDING: " + _name.Replace("\"", "");
            }

            public void Stop(Int64 timeFinished, BuildEventState jobResult)
            {
                _timeFinished = timeFinished;

                double totalTimeSeconds = (_timeFinished - _timeStarted) / 1000.0f;

                // uncomment to catch negative times
                Debug.Assert(totalTimeSeconds >= 0.0f);

                //if (totalTimeSeconds <=0.0f)
                //{
                //    totalTimeSeconds = 0.001f;
                //}

                _toolTipText = string.Format("{0}", _name.Replace("\"", "")) + "\nStatus: ";

                _state = jobResult;

                switch (_state)
                {
                    case BuildEventState.SUCCEEDED_COMPLETED:
                        if (_name.Contains(".obj"))
                        {
                            _brush = _sSuccessCodeBrush;
                        }
                        else
                        {
                            _brush = _sSuccessNonCodeBrush;
                        }
                        _toolTipText += "Success";

                        break;
                    case BuildEventState.SUCCEEDED_CACHED:
                        _brush = _sSuccessCachedBrush;

                        _toolTipText += "Success(Cached)";

                        break;

                    case BuildEventState.SUCCEEDED_PREPROCESSED:
                        _brush = _sSuccessPreprocessedBrush;

                        _toolTipText += "Success(Preprocess)";

                        break;
                    case BuildEventState.FAILED:

                        _brush = _sFailedBrush;
                        _toolTipText += "Errors";

                        break;
                    case BuildEventState.TIMEOUT:

                        _brush = _sTimeoutBrush;
                        _toolTipText += "Timeout";

                        break;
                    default:
                        break;
                }

                _toolTipText += "\nDuration: " + GetTimeFormattedString(_timeFinished - _timeStarted);
                _toolTipText += "\nStart Time: " + GetTimeFormattedString(_timeStarted);
                _toolTipText += "\nEnd Time: " + GetTimeFormattedString(_timeFinished);

                if (null != _outputMessages && _outputMessages.Length > 0)
                {
                    // show only an extract of the errors so we don't flood the visual
                    int textLength = Math.Min(_outputMessages.Length, 100);

                    _toolTipText += "\n" + _outputMessages.Substring(0, textLength);
                    _toolTipText += "... [Double-Click on the event to see more details]";

                    _outputMessages = string.Format("[Output {0}]: {1}", _name.Replace("\"", ""), Environment.NewLine) + _outputMessages;

                    _StaticWindow.AddOutputWindowFilterItem(this);
                }
            }

            public bool JumpToEventLineInOutputBox()
            {
                bool bSuccess = false;

                int index = _StaticWindow.OutputTextBox.Text.IndexOf(_name.Replace("\"", ""));

                int lineNumber = _StaticWindow.OutputTextBox.Text.Substring(0, index).Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length;

                _StaticWindow.OutputTextBox.ScrollToLine(lineNumber - 1);

                int position = _StaticWindow.OutputTextBox.GetCharacterIndexFromLineIndex(lineNumber - 1);
                if (position >= 0)
                {
                    int lineEnd = _StaticWindow.OutputTextBox.Text.IndexOf(Environment.NewLine, position);
                    if (lineEnd < 0)
                    {
                        lineEnd = _StaticWindow.OutputTextBox.Text.Length;
                    }

                    _StaticWindow.OutputTextBox.Select(position, lineEnd - position);
                }

                return bSuccess;
            }

            public bool HandleDoubleClickEvent()
            {
                bool bHandled = true;

                if (_state != BuildEventState.IN_PROGRESS && _outputMessages != null && _outputMessages.Length > 0)
                {
                    // Switch to the Output Window Tab item
                    _StaticWindow.MyTabControl.SelectedIndex = (int)eTABs.TAB_OUTPUT;

                    _StaticWindow.ChangeOutputWindowComboBoxSelection(this);
                }

                return bHandled;
            }

            public HitTestResult HitTest(Point localMousePosition)
            {
                HitTestResult result = null;

                if (_bordersRect.Contains(localMousePosition))
                {
                    result = new HitTestResult(this._core._parent, this._core, this);
                }

                return result;
            }

            public void RenderUpdate(ref double X, double Y)
            {
                long duration = 0;

                bool bIsCompleted = false;

                double OriginalWidthInPixels = 0.0f;
                double AdjustedWidthInPixels = 0.0f;

                double borderRectWidth = 0.0f;

                if (_state == BuildEventState.IN_PROGRESS)
                {
                    // Event is in progress
                    duration = (long)Math.Max(0.0f, GetCurrentBuildTimeMS(true) - _timeStarted);

                    Point textSize = TextUtils.ComputeTextSize(_fileName);

                    OriginalWidthInPixels = AdjustedWidthInPixels = _zoomFactor * pix_per_second * (double)duration / (double)1000;

                    borderRectWidth = OriginalWidthInPixels + pix_per_second * cTimeStepMS / 1000.0f;

                    borderRectWidth = Math.Max(Math.Min(_cMinTextLabelWidthThreshold * 2, textSize.X), borderRectWidth);

                    _toolTipText = "BUILDING: " + _name.Replace("\"", "") + "\nTime Elapsed: " + GetTimeFormattedString(duration);
                }
                else
                {
                    // Event is completed
                    bIsCompleted = true;
                    duration = _timeFinished - _timeStarted;

                    // Handle the zoom factor
                    OriginalWidthInPixels = _zoomFactor * pix_per_second * (double)duration / (double)1000;

                    // Try to compensate for the pixels lost with the spacing introduced between events
                    AdjustedWidthInPixels = Math.Max(0.0f, OriginalWidthInPixels - pix_space_between_events);

                    borderRectWidth = AdjustedWidthInPixels;
                }

                // Adjust the start time position if possible
                double desiredX = _zoomFactor * pix_per_second * (double)_timeStarted / (double)1000;
                if (desiredX > X)
                {
                    X = desiredX;
                }

                // Are we a Low LOD candidate?
                bool isInLowLOD = (AdjustedWidthInPixels <= pix_LOD_Threshold) && bIsCompleted;

                // Update the element size and figure out of anything changed since the last update
                Rect newBorderRect = new Rect(X, Y, borderRectWidth, pix_height);
                Rect newProgressRect = new Rect(X, Y, AdjustedWidthInPixels, pix_height);

                _isDirty = !_bordersRect.Equals(newBorderRect) || !_progressRect.Equals(newProgressRect) || isInLowLOD != _isInLowLOD;

                _isInLowLOD = isInLowLOD;
                _bordersRect = newBorderRect;
                _progressRect = newProgressRect;

                // Update our horizontal position on the time-line
                X = X + OriginalWidthInPixels;

                // Make sure we update our Canvas boundaries
                UpdateEventsCanvasMaxSize(X, Y);
            }

            public bool IsObjectVisibleInternal(Rect localRect)
            {
                Rect absoluteRect = new Rect(_core._x + localRect.X, _core._y + localRect.Y, localRect.Width, localRect.Height);

                return IsObjectVisible(absoluteRect);
            }

            public void OnRender(DrawingContext dc)
            {
                // if the current event is in lowLOD mode
                if (_isInLowLOD)
                {
                    bool bStartNewLODBlock = false;

                    if (_core.IsLODBlockActive())
                    {
                        // calculate the distance (in pixels) between the end of the current LOD block and the start of the next block
                        double distance = _bordersRect.X - (_core._currentLODRect.X + _core._currentLODRect.Width);

                        if (distance > 5.0f)
                        {
                            // if the distance is above the threshold close the current LOD block and start a new one
                            VisualBrush brush = new VisualBrush();
                            brush.Visual = CPUCore._sLODImage;
                            brush.Stretch = Stretch.None;
                            brush.TileMode = TileMode.Tile;
                            brush.AlignmentY = AlignmentY.Top;
                            brush.AlignmentX = AlignmentX.Left;
                            brush.ViewportUnits = BrushMappingMode.Absolute;
                            brush.Viewport = new Rect(0, 0, 40, 6);

                            if (IsObjectVisibleInternal(_core._currentLODRect))
                            {
#if ENABLE_RENDERING_STATS
                                _StaticWindow._numShapesDrawn++;
#endif
                                dc.DrawRectangle(brush, new Pen(Brushes.Gray, 1), _core._currentLODRect);

                                _core.AddVisibleElement(_core._currentLODRect, string.Format("{0} events", _core._currentLODCount));
                            }

                            _core.CloseCurrentLODBlock();

                            // start a new LOD block
                            bStartNewLODBlock = true;
                        }
                        else
                        {
                            // if an LOD block is currently active then append the current event to it
                            _core.UpdateCurrentLODBlock(Math.Max(_bordersRect.X + _bordersRect.Width - _core._currentLODRect.X, 0.0f));
                        }
                    }
                    else
                    {
                        bStartNewLODBlock = true;
                    }

                    if (bStartNewLODBlock)
                    {
                        _core.StartNewLODBlock(new Rect(_bordersRect.X, _bordersRect.Y, 0.0f, _bordersRect.Height));
                    }
                }
                else
                {
                    if (_core.IsLODBlockActive())
                    {
                        VisualBrush brush = new VisualBrush();
                        brush.Visual = CPUCore._sLODImage;
                        brush.Stretch = Stretch.None;
                        brush.TileMode = TileMode.Tile;
                        brush.AlignmentY = AlignmentY.Top;
                        brush.AlignmentX = AlignmentX.Left;
                        brush.ViewportUnits = BrushMappingMode.Absolute;
                        brush.Viewport = new Rect(0, 0, 40, 6);

                        if (IsObjectVisibleInternal(_core._currentLODRect))
                        {
#if ENABLE_RENDERING_STATS
                        _StaticWindow._numShapesDrawn++;
#endif
                            dc.DrawRectangle(brush, new Pen(Brushes.Gray, 1), _core._currentLODRect);

                            _core.AddVisibleElement(_core._currentLODRect, string.Format("{0} events", _core._currentLODCount));
                        }

                        _core.CloseCurrentLODBlock();
                    }

                    if (IsObjectVisibleInternal(_bordersRect))
                    {

                        _core.AddVisibleElement(_bordersRect, _toolTipText, this);

#if ENABLE_RENDERING_STATS
                    _StaticWindow._numShapesDrawn++;
#endif
                        dc.DrawImage(_brush.ImageSource, _progressRect);

                        SolidColorBrush colorBrush = Brushes.Black;

                        if (_state == BuildEventState.IN_PROGRESS)
                        {
                            // Draw an open rectangle
                            Point P0 = new Point(_bordersRect.X, _bordersRect.Y);
                            Point P1 = new Point(_bordersRect.X + _bordersRect.Width, _bordersRect.Y);
                            Point P2 = new Point(_bordersRect.X + _bordersRect.Width, _bordersRect.Y + _bordersRect.Height);
                            Point P3 = new Point(_bordersRect.X, _bordersRect.Y + _bordersRect.Height);

                            Pen pen = new Pen(Brushes.Gray, 1);

                            dc.DrawLine(pen, P0, P1);
                            dc.DrawLine(pen, P0, P3);
                            dc.DrawLine(pen, P3, P2);
                        }
                        else
                        {
                            switch(_state)
                            {
                                case BuildEventState.SUCCEEDED_PREPROCESSED:
                                //case BuildEventState.FAILED:
                                    colorBrush = Brushes.PaleTurquoise;
                                    break;
                            }

                            dc.DrawRectangle(new VisualBrush(), new Pen(Brushes.Gray, 1), _bordersRect);
                        }

                        string textToDisplay = null;

                        if (_bordersRect.Width > _cMinTextLabelWidthThreshold)
                        {
                            textToDisplay = _fileName;
                        }
                        //else if (_bordersRect.Width > _cMinDotDotDotWidthThreshold)
                        //{
                        //    textToDisplay = "...";
                        //}

                        if (textToDisplay != null)
                        {
#if ENABLE_RENDERING_STATS
                        _StaticWindow._numTextElementsDrawn++;
#endif
                            double allowedTextWidth = Math.Max(0.0f, _bordersRect.Width - 2 * _cTextLabeloffset_X);

                            TextUtils.DrawText(dc, textToDisplay, _bordersRect.X + _cTextLabeloffset_X, _bordersRect.Y + _cTextLabeloffset_Y, allowedTextWidth, true, colorBrush);
                        }
                    }
                }
            }
        }


        /* Commands parsing feature */
        private BuildEventState TranslateBuildEventState(string eventString)
        {
            BuildEventState output = BuildEventState.UNKOWN;

            switch (eventString)
            {
                case "FAILED":
                case "ERROR":
                    output = BuildEventState.FAILED;
                    break;
                case "SUCCESS":
                case "SUCCESS_COMPLETE":
                    output = BuildEventState.SUCCEEDED_COMPLETED;
                    break;
                case "SUCCESS_CACHED":
                    output = BuildEventState.SUCCEEDED_CACHED;
                    break;
                case "SUCCESS_PREPROCESSED":
                    output = BuildEventState.SUCCEEDED_PREPROCESSED;
                    break;
                case "TIMEOUT":
                    output = BuildEventState.TIMEOUT;
                    break;
            }

            return output;
        }


        private enum BuildEventCommand
        {
            UNKNOWN = -1,
            START_BUILD,
            STOP_BUILD,
            START_JOB,
            FINISH_JOB,
            PROGRESS_STATUS,
            GRAPH
        }

        private BuildEventCommand TranslateBuildEventCommand(string commandString)
        {
            BuildEventCommand output = BuildEventCommand.UNKNOWN;

            switch (commandString)
            {
                case "START_BUILD":
                    output = BuildEventCommand.START_BUILD;
                    break;
                case "STOP_BUILD":
                    output = BuildEventCommand.STOP_BUILD;
                    break;
                case "START_JOB":
                    output = BuildEventCommand.START_JOB;
                    break;
                case "FINISH_JOB":
                    output = BuildEventCommand.FINISH_JOB;
                    break;
                case "PROGRESS_STATUS":
                    output = BuildEventCommand.PROGRESS_STATUS;
                    break;
                case "GRAPH":
                    output = BuildEventCommand.GRAPH;
                    break;
            }

            return output;
        }


        const string _cLocalHostName = "local";
        const string _cPrepareBuildStepsText = "Preparing Build Steps";
        bool _bPreparingBuildsteps = false;
        Hashtable _hosts = new Hashtable();
        BuildHost _localHost = null;


        public static class CommandArgumentIndex
        {
            // Global arguments (apply to all commands)
            public const int TIME_STAMP = 0;
            public const int COMMAND_TYPE = 1;

            public const int START_BUILD_LOG_VERSION = 2;
            public const int START_BUILD_PID = 3;

            public const int START_JOB_HOST_NAME = 2;
            public const int START_JOB_EVENT_NAME = 3;

            public const int FINISH_JOB_RESULT = 2;
            public const int FINISH_JOB_HOST_NAME = 3;
            public const int FINISH_JOB_EVENT_NAME = 4;
            public const int FINISH_JOB_OUTPUT_MESSAGES = 5;

            public const int PROGRESS_STATUS_PROGRESS_PCT = 2;

            public const int GRAPH_GROUP_NAME = 2;
            public const int GRAPH_COUNTER_NAME = 3;
            public const int GRAPH_COUNTER_UNIT_TAG = 4;
            public const int GRAPH_COUNTER_VALUE = 5;
        }


        private int _lastProcessedPosition = 0;

        private void ProcessInputFileStream()
        {
            if (_fileStream == null)
            {
                string path = System.Environment.GetEnvironmentVariable("TEMP") + @"\FastBuild\FastBuildLog.log";

                if (!Directory.Exists(System.IO.Path.GetDirectoryName(path)))
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                }

                try
                {
                    _fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    ResetState();
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine("Exception! " + ex.ToString());
                    // the log file does not exist, bail out...
                    return;
                }
            }

            // The file has been emptied so we must reset our state and start over
            if (BuildRestarted())
            {
                ResetState();

                return;
            }

            // Read all the new data and append it to our _fileBuffer
            int numBytesToRead = (int)(_fileStream.Length - _fileStreamPosition);

            if (numBytesToRead > 0)
            {
                byte[] buffer = new byte[numBytesToRead];

                _fileStream.Seek(_fileStreamPosition, SeekOrigin.Begin);

                int numBytesRead = _fileStream.Read(buffer, 0, numBytesToRead);

                Debug.Assert(numBytesRead == numBytesToRead, "Could not read the expected amount of data from the log file...!");

                _fileStreamPosition += numBytesRead;

                _fileBuffer.AddRange(buffer);

                //Scan the current buffer looking for the last line position
                int newPayloadStart = _lastProcessedPosition;
                int newPayLoadSize = -1;
                for (int i = _fileBuffer.Count - 1; i > _lastProcessedPosition; --i)
                {
                    if (_fileBuffer[i] == '\n')
                    {
                        newPayLoadSize = i - newPayloadStart;
                        break;
                    }
                }

                if (newPayLoadSize > 0)
                {
                    // we received new events, allow the render update to kick
                    SetConditionalRenderUpdateFlag(true);

                    string newEventsRaw = System.Text.Encoding.Default.GetString(_fileBuffer.GetRange(_lastProcessedPosition, newPayLoadSize).ToArray());
                    string[] newEvents = newEventsRaw.Split(new char[] { '\n' });

                    foreach (string eventString in newEvents)
                    {
                        string[] tokens = Regex.Matches(eventString, @"[\""].+?[\""]|[^ ]+")
                                         .Cast<Match>()
                                         .Select(m => m.Value)
                                         .ToList().ToArray();

                        // TODO More error handling...
                        if (tokens.Length >= 2)
                        {
                            // let's get the command timestamp and update our internal time reference
                            Int64 eventFileTime = Int64.Parse(tokens[CommandArgumentIndex.TIME_STAMP]);
                            Int64 eventLocalTimeMS = RegisterNewTimeStamp(eventFileTime);

                            // parse the command
                            string commandString = tokens[CommandArgumentIndex.COMMAND_TYPE];
                            BuildEventCommand command = TranslateBuildEventCommand(commandString);

                            switch (command)
                            {
                                case BuildEventCommand.START_BUILD:
                                    if (_buildRunningState == eBuildRunningState.Ready)
                                    {
                                        ExecuteCommandStartBuild(tokens, eventLocalTimeMS);
                                    }
                                    break;
                                case BuildEventCommand.STOP_BUILD:
                                    if (_buildRunningState == eBuildRunningState.Running)
                                    {
                                        ExecuteCommandStopBuild(tokens, eventLocalTimeMS);
                                    }
                                    break;
                                case BuildEventCommand.START_JOB:
                                    if (_buildRunningState == eBuildRunningState.Running)
                                    {
                                        ExecuteCommandStartJob(tokens, eventLocalTimeMS);
                                    }
                                    break;
                                case BuildEventCommand.FINISH_JOB:
                                    if (_buildRunningState == eBuildRunningState.Running)
                                    {
                                        ExecuteCommandFinishJob(tokens, eventLocalTimeMS);
                                    }
                                    break;
                                case BuildEventCommand.PROGRESS_STATUS:
                                    if (_buildRunningState == eBuildRunningState.Running)
                                    {
                                        ExecuteCommandProgressStatus(tokens);
                                    }
                                    break;
                                case BuildEventCommand.GRAPH:
                                    if (_buildRunningState == eBuildRunningState.Running)
                                    {
                                        ExecuteCommandGraph(tokens, eventLocalTimeMS);
                                    }
                                    break;
                                default:
                                    // Skipping unknown commands
                                    break;
                            }
                        }
                    }

                    _lastProcessedPosition += newPayLoadSize;
                }
            }
            else if (_buildRunningState == eBuildRunningState.Running && PollIsTargetProcessRunning() == false)
            {
                // Detect canceled builds
                _latestTimeStampMS = GetCurrentSystemTimeMS();

                ExecuteCommandStopBuild(null, _latestTimeStampMS);
            }
        }

        // Commands handling
        private void ExecuteCommandStartBuild(string[] tokens, Int64 eventLocalTimeMS)
        {
            int logVersion = int.Parse(tokens[CommandArgumentIndex.START_BUILD_LOG_VERSION]);

            if (logVersion == FASTBuildMonitorControl.LOG_VERSION)
            {
                int targetPID = int.Parse(tokens[CommandArgumentIndex.START_BUILD_PID]);

                // remember our valid targetPID
                _targetPID = targetPID;

                // determine if we are in a live session (target PID is running when we receive a start build command)
                _isLiveSession = IsTargetProcessRunning(_targetPID);

                _systemPerformanceGraphs.OpenSession(_isLiveSession, _targetPID);

                // Record the start time
                _buildStartTimeMS = eventLocalTimeMS;

                _buildRunningState = eBuildRunningState.Running;

                // start the gif "building" animation
                StatusBarRunningGif.StartAnimation();

                ToolTip newToolTip = new ToolTip();
                StatusBarRunningGif.ToolTip = newToolTip;
                newToolTip.Content = "Build in Progress...";
            }
        }

        private void ExecuteCommandStopBuild(string[] tokens, Int64 eventLocalTimeMS)
        {
            Int64 timeStamp = (eventLocalTimeMS - _buildStartTimeMS);

            if (_bPreparingBuildsteps)
            {
                _localHost.OnCompleteEvent(timeStamp, _cPrepareBuildStepsText, BuildEventState.SUCCEEDED_COMPLETED, "");
            }

            // Stop all the active events currently running
            foreach (DictionaryEntry entry in _hosts)
            {
                BuildHost host = entry.Value as BuildHost;
                foreach (CPUCore core in host._cores)
                {
                    core.UnScheduleEvent(timeStamp, _cPrepareBuildStepsText, BuildEventState.TIMEOUT, "", true);
                }
            }

            _bPreparingBuildsteps = false;

            _buildRunningState = eBuildRunningState.Ready;

            StatusBarRunningGif.StopAnimation();
            StatusBarRunningGif.ToolTip = null;

            UpdateBuildProgress(100.0f);


            if (_isLiveSession)
            {
                _systemPerformanceGraphs.CloseSession();

                _isLiveSession = false;
            }
        }

        private void ExecuteCommandStartJob(string[] tokens, Int64 eventLocalTimeMS)
        {
            Int64 timeStamp = (eventLocalTimeMS - _buildStartTimeMS);

            string hostName = tokens[CommandArgumentIndex.START_JOB_HOST_NAME];
            string eventName = tokens[CommandArgumentIndex.START_JOB_EVENT_NAME];

            if (_bPreparingBuildsteps)
            {
                _localHost.OnCompleteEvent(timeStamp, _cPrepareBuildStepsText, BuildEventState.SUCCEEDED_COMPLETED, "");
            }

            BuildEvent newEvent = new BuildEvent(eventName, timeStamp);

            BuildHost host = null;
            if (_hosts.ContainsKey(hostName))
            {
                host = _hosts[hostName] as BuildHost;
            }
            else
            {
                // discovered a new host!
                host = new BuildHost(hostName);
                _hosts.Add(hostName, host);
            }

            host.OnStartEvent(newEvent);
        }

        private void ExecuteCommandFinishJob(string[] tokens, Int64 eventLocalTimeMS)
        {
            Int64 timeStamp = (eventLocalTimeMS - _buildStartTimeMS);

            string jobResultString = tokens[CommandArgumentIndex.FINISH_JOB_RESULT];
            string hostName = tokens[CommandArgumentIndex.FINISH_JOB_HOST_NAME];
            string eventName = tokens[CommandArgumentIndex.FINISH_JOB_EVENT_NAME];

            string eventOutputMessages = "";

            // Optional parameters
            if (tokens.Length > CommandArgumentIndex.FINISH_JOB_OUTPUT_MESSAGES)
            {
                eventOutputMessages = tokens[CommandArgumentIndex.FINISH_JOB_OUTPUT_MESSAGES].Substring(1, tokens[CommandArgumentIndex.FINISH_JOB_OUTPUT_MESSAGES].Length - 2);
            }

            BuildEventState jobResult = TranslateBuildEventState(jobResultString);

            foreach (DictionaryEntry entry in _hosts)
            {
                BuildHost host = entry.Value as BuildHost;
                host.OnCompleteEvent(timeStamp, eventName, jobResult, eventOutputMessages);
            }

            UpdateBuildStatus(jobResult);
        }

        private void ExecuteCommandProgressStatus(string[] tokens)
        {
            float progressPCT = float.Parse(tokens[CommandArgumentIndex.PROGRESS_STATUS_PROGRESS_PCT]);

            // Update the build status after each job's result
            UpdateBuildProgress(progressPCT);
        }


        private void ExecuteCommandGraph(string[] tokens, Int64 eventLocalTimeMS)
        {
            Int64 timeStamp = (eventLocalTimeMS - _buildStartTimeMS);

            string groupName = tokens[CommandArgumentIndex.GRAPH_GROUP_NAME]; 
            string counterName = tokens[CommandArgumentIndex.GRAPH_COUNTER_NAME].Substring(1, tokens[CommandArgumentIndex.GRAPH_COUNTER_NAME].Length - 2); // Remove the quotes at the start and end 
            string counterUnitTag = tokens[CommandArgumentIndex.GRAPH_COUNTER_UNIT_TAG];
            float value = float.Parse(tokens[CommandArgumentIndex.GRAPH_COUNTER_VALUE]);

            _systemPerformanceGraphs.HandleLogEvent(timeStamp, groupName, counterName, counterUnitTag, value);
        }

        private static bool IsObjectVisible(Rect objectRect)
        {
            // Todo: activate clipping optimization
            //return true;

            // make the viewport 10% larger
            const double halfIncPct = 10.0f / (100.0f * 2.0f);

            double x = Math.Max(0.0f, _viewport.X - _viewport.Width * halfIncPct);
            double y = Math.Max(0.0f, _viewport.Y - _viewport.Height * halfIncPct);
            double w = _viewport.Width * (1.0 + halfIncPct);
            double h = _viewport.Height * (1.0 + halfIncPct);

            Rect largerViewport = new Rect(x, y, w, h);

            return largerViewport.IntersectsWith(objectRect) || largerViewport.Contains(objectRect);
        }

        private static Rect _viewport = new Rect();

        private static double _maxX = 0.0f;
        private static double _maxY = 0.0f;

        private static void UpdateEventsCanvasMaxSize(double X, double Y)
        {
            _maxX = X > _maxX ? X : _maxX;
            _maxY = Y > _maxY ? Y : _maxY;
        }

#if ENABLE_RENDERING_STATS
    private int _numShapesDrawn = 0;            // (stats) number of shapes (ex: Rectangle) drawn on each frame
    private int _numTextElementsDrawn = 0;      // (stats) number of text elements drawn on each frame
#endif

        private void UpdateViewport()
        {
            Rect newViewport = new Rect(_StaticWindow.EventsScrollViewer.HorizontalOffset, _StaticWindow.EventsScrollViewer.VerticalOffset,
                                _StaticWindow.EventsScrollViewer.ViewportWidth, _StaticWindow.EventsScrollViewer.ViewportHeight);


            if (!_viewport.Equals(newViewport))
            {
                foreach (DictionaryEntry entry in _hosts)
                {
                    BuildHost host = entry.Value as BuildHost;
                    foreach (CPUCore core in host._cores)
                    {
                        core.InvalidateVisual();
                    }
                }

                _viewport = newViewport;
            }
        }

        HitTestResult HitTest(Point mousePosition)
        {
            HitTestResult result = null;

            foreach (DictionaryEntry entry in _hosts)
            {
                BuildHost host = entry.Value as BuildHost;

                result = host.HitTest(mousePosition);

                if (result != null)
                {
                    break;
                }
            }

            return result;
        }


        private void RenderUpdate()
        {
            _timeBar.RenderUpdate(10, 0, _zoomFactor);

            _systemPerformanceGraphs.RenderUpdate(10, 0, _zoomFactor);
                        
            // Update the tooltips
            Point mousePosition = Mouse.GetPosition(EventsScrollViewer);

            mousePosition.X += EventsScrollViewer.HorizontalOffset;
            mousePosition.Y += EventsScrollViewer.VerticalOffset;

            foreach (DictionaryEntry entry in _hosts)
            {
                BuildHost host = entry.Value as BuildHost;

                if (host.UpdateToolTip(mousePosition))
                {
                    break;
                }
            }
        }

        private void ConditionalRenderUpdate()
        {
            // Resolve ViewPort center/size in case of zoom in/out event
            UpdateZoomTargetPosition();

            // Update the viewport and decide if we have to redraw the Events canvas
            UpdateViewport();

            _maxX = 0.0f;
            _maxY = 0.0f;

            double X = 10;
            double Y = 10;

            // Always draw the local host first
            if (_localHost != null)
            {
                _localHost.RenderUpdate(X, ref Y);
            }

            foreach (DictionaryEntry entry in _hosts)
            {
                BuildHost host = entry.Value as BuildHost;

                if (host != _localHost)
                {
                    host.RenderUpdate(X, ref Y);
                }
            }

            //Console.WriteLine("Scroll V Offset: (cores: {0} - events: {1})", CoresScrollViewer.ScrollableHeight, EventsScrollViewer.ScrollableHeight);

            EventsCanvas.Width = TimeBarCanvas.Width = SystemGraphsCanvas.Width = _maxX + _viewport.Width * 0.25f;
            EventsCanvas.Height = CoresCanvas.Height = _maxY;

#if ENABLE_RENDERING_STATS
        Console.WriteLine("Render Stats (Shapes: {0} - Text: {1})", _numShapesDrawn, _numTextElementsDrawn);
        _numShapesDrawn = 0;
        _numTextElementsDrawn = 0;
#endif
        }


        // outputs a time string in the format: 00:00:00
        public static string GetTimeFormattedString(Int64 timeMS)
        {
            Int64 remainingTimeSeconds = timeMS / 1000;

            int hours = (int)(remainingTimeSeconds / (60 * 60));
            remainingTimeSeconds -= hours * 60 * 60;

            int minutes = (int)(remainingTimeSeconds / (60));
            remainingTimeSeconds -= minutes * 60;

            string formattedText;

            if (hours > 0)
            {
                formattedText = string.Format("{0}:{1:00}:{2:00}", hours, minutes, remainingTimeSeconds);
            }
            else
            {
                formattedText = string.Format("{0}:{1:00}", minutes, remainingTimeSeconds);
            }

            return formattedText;
        }

        // outputs a time string in the format: 0h 0m 0s
        public static string GetTimeFormattedString2(Int64 timeMS)
        {
            Int64 remainingTimeSeconds = timeMS / 1000;

            int hours = (int)(remainingTimeSeconds / (60 * 60));
            remainingTimeSeconds -= hours * 60 * 60;

            int minutes = (int)(remainingTimeSeconds / (60));
            remainingTimeSeconds -= minutes * 60;

            string formattedText;

            if (hours > 0)
            {
                formattedText = string.Format("{0}h {1}m {2}s", hours, minutes, remainingTimeSeconds);
            }
            else
            {
                formattedText = string.Format("{0}m {1}s", minutes, remainingTimeSeconds);
            }

            return formattedText;
        }


        TimeBar _timeBar = null;

        SystemPerformanceGraphsCanvas _systemPerformanceGraphs;

        bool _bDoUpdateRender = true;   // Controls the update of the rendered elements 

        private void SetConditionalRenderUpdateFlag( bool bAllowed)
        {
            // setting _bDoUpdateRender means that we are allowing RenderUpdate() to run on the very next frame (or the current frame)
            _bDoUpdateRender = bAllowed;
        }

        private bool IsConditionalRenderUpdateAllowed()
        {
            return _bDoUpdateRender;
        }

        private void HandleTick(object sender, EventArgs e)
        {
            try
            {
                // Process the input log for new events
                ProcessInputFileStream();

                // Handling Mouse panning, we do it here because it does not necessitate a RenderUpdate
                UpdateMousePanning();

                // Call the non-expensive Render Update every frame
                RenderUpdate();

                // Call the Conditional Render Update only when needed since it is expensive
                if (IsConditionalRenderUpdateAllowed())
                {
                    ConditionalRenderUpdate();

                    UpdateStatusBar();

                    SetConditionalRenderUpdateFlag(false);
                }

            }
            catch (System.Exception ex)
            {
                Console.WriteLine("Exception detected... Restarting! details: " + ex.ToString());
                ResetState();
            }
        }
    }
}
