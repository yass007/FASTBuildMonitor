using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Input;
using System.Diagnostics;


namespace FASTBuildMonitorVSIX
{
    class TimeBar : Canvas
    {
        public TimeBar(Canvas parentCanvas)
        {
            _parentCanvas = parentCanvas;

            this.Width = _parentCanvas.Width;
            this.Height = _parentCanvas.Height;

            _parentCanvas.Children.Add(this);
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawGeometry(Brushes.Black, new Pen(Brushes.Black, 1), _geometry);

            _textTags.ForEach(tag => FASTBuildMonitorControl.DrawText(dc, tag._text, tag._x, tag._y, 100, false, Brushes.Black));
        }

        void UpdateGeometry(double X, double Y, double zoomFactor)
        {
            // Clear old geometry
            _geometry.Clear();

            _textTags.Clear();

            // Open a StreamGeometryContext that can be used to describe this StreamGeometry 
            // object's contents.
            using (StreamGeometryContext ctx = _geometry.Open())
            {
                Int64 totalTimeMS = 0;

                Int64 numSteps = FASTBuildMonitorControl.GetCurrentBuildTimeMS() / (_bigTimeUnit * 1000);
                Int64 remainder = FASTBuildMonitorControl.GetCurrentBuildTimeMS() % (_bigTimeUnit * 1000);

                numSteps += remainder > 0 ? 2 : 1;

                Int64 timeLimitMS = numSteps * _bigTimeUnit * 1000;

                while (totalTimeMS <= timeLimitMS)
                {
                    bool bDrawBigMarker = totalTimeMS % (_bigTimeUnit * 1000) == 0;

                    double x = X + zoomFactor * FASTBuildMonitorControl.pix_per_second * totalTimeMS / 1000.0f;

                    //if (x >= _savedTimebarViewPort.X && x <= _savedTimebarViewPort.Y)
                    {
                        double height = bDrawBigMarker ? 5.0f : 2.0f;

                        ctx.BeginFigure(new Point(x, Y), true /* is filled */, false /* is closed */);

                        // Draw a line to the next specified point.
                        ctx.LineTo(new Point(x, Y + height), true /* is stroked */, false /* is smooth join */);

                        if (bDrawBigMarker)
                        {
                            string formattedText = FASTBuildMonitorControl.GetTimeFormattedString(totalTimeMS);

                            Point textSize = FASTBuildMonitorControl.ComputeTextSize(formattedText);

                            double horizontalCorrection = textSize.X / 2.0f;

                            TextTag newTag = new TextTag(formattedText, x - horizontalCorrection, Y + height + 2);

                            _textTags.Add(newTag);
                        }
                    }

                    totalTimeMS += _smallTimeUnit * 1000;
                }
            }
        }

        bool UpdateTimeUnits()
        {
            bool bNeedsToUpdateGeometry = false;

            const double pixChunkSize = 100.0f;

            double timePerChunk = pixChunkSize / (FASTBuildMonitorControl._zoomFactor * FASTBuildMonitorControl.pix_per_second);

            int newBigTimeUnit = 0;
            int newSmallTimeUnit = 0;

            if (timePerChunk > 30.0f)
            {
                newBigTimeUnit = 60;
                newSmallTimeUnit = 10;
            }
            else if (timePerChunk > 10.0f)
            {
                newBigTimeUnit = 30;
                newSmallTimeUnit = 6;
            }
            else if (timePerChunk > 5.0f)
            {
                newBigTimeUnit = 10;
                newSmallTimeUnit = 2;
            }
            else
            {
                newBigTimeUnit = 5;
                newSmallTimeUnit = 1;
            }

            Point newTimebarViewPort = new Point(FASTBuildMonitorControl._StaticWindow.EventsScrollViewer.HorizontalOffset, FASTBuildMonitorControl._StaticWindow.EventsScrollViewer.HorizontalOffset + FASTBuildMonitorControl._StaticWindow.EventsScrollViewer.ViewportWidth);

            if (FASTBuildMonitorControl._zoomFactor != _savedZoomFactor || FASTBuildMonitorControl.GetCurrentBuildTimeMS() != _savedBuildTime || newTimebarViewPort != _savedTimebarViewPort)
            {
                _bigTimeUnit = newBigTimeUnit;
                _smallTimeUnit = newSmallTimeUnit;

                _savedZoomFactor = FASTBuildMonitorControl._zoomFactor;

                _savedBuildTime = FASTBuildMonitorControl.GetCurrentBuildTimeMS();

                _savedTimebarViewPort = newTimebarViewPort;

                this.InvalidateVisual();

                bNeedsToUpdateGeometry = true;
            }

            return bNeedsToUpdateGeometry;
        }

        public void RenderUpdate(double X, double Y, double zoomFactor)
        {
            if (UpdateTimeUnits())
            {
                this.InvalidateVisual();

                UpdateGeometry(X, Y, zoomFactor);
            }
        }

        private class TextTag
        {
            public TextTag(string text, double x, double y)
            {
                _text = text;
                _x = x;
                _y = y;
            }

            public string _text;
            public double _x;
            public double _y;
        }

        List<TextTag> _textTags = new List<TextTag>();

        StreamGeometry _geometry = new StreamGeometry();

        int _bigTimeUnit = 0;
        int _smallTimeUnit = 0;

        double _savedZoomFactor = 0.0f;
        double _savedBuildTime = 0.0f;
        Point _savedTimebarViewPort = new Point();

        Canvas _parentCanvas = null;
    }

}
