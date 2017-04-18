//------------------------------------------------------------------------------
// Copyright 2017 Yassine Riahi and Liam Flookes. 
// Provided under a MIT License, see license file on github.
//------------------------------------------------------------------------------

using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using Microsoft.VisualStudio.ExtensionManager;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;
using EnvDTE;
using EnvDTE80;

namespace FASTBuildMonitorVSIX
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // Info on this package for Help/About
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(FASTBuildMonitor))]
    [Guid(FASTBuildMonitorPackage.PackageGuidString)]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    public sealed class FASTBuildMonitorPackage : Package
    {
        public DTE2 _dte;

        public static FASTBuildMonitorPackage _instance = null;

        /// <summary>
        /// FASTBuildMonitorPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "73de7c44-188b-45d3-aab2-19af8724c5c9";

        /// <summary>
        /// Initializes a new instance of the <see cref="FASTBuildMonitor"/> class.
        /// </summary>
        public FASTBuildMonitorPackage()
        {
            // Inside this method you can place any initialization code that does not require
            // any Visual Studio service because at this point the package object is created but
            // not sited yet inside Visual Studio environment. The place to do all the other
            // initialization is the Initialize method.
        }

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            FASTBuildMonitorCommand.Initialize(this);
            base.Initialize();

            _instance = this;

            _dte = (DTE2)base.GetService(typeof(DTE));
        }

        public static int count = 0;

        public void ListWindows()
        {
            OutputWindow outWindow = _dte.ToolWindows.OutputWindow;
            outWindow.Parent.AutoHides = false;
            outWindow.Parent.Activate();

            //string test = window.ActivePane.Name;

            OutputWindowPane buildPane = null;
            try
            {
                buildPane = outWindow.OutputWindowPanes.Item("Build");
            }
            catch
            {
                buildPane = outWindow.OutputWindowPanes.Add("Build");
            }
            finally
            {
                //buildPane.Clear();
            }



            for (int i = count; i < count + 20; ++i)
            {
                buildPane.OutputString("Line " + i + "\n");
            }


            buildPane.Activate();

            try
            {
                if (buildPane.TextDocument != null)
                {
                    TextDocument doc = buildPane.TextDocument;
                    TextSelection sel = doc.Selection;

                    sel.StartOfDocument(false);
                    sel.EndOfDocument(true);

                    count += 20;


                    sel.GotoLine(count -5 );


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

        public class VSIXPackageInformation
        {
            public Version _version = null;
            public string _packageName;
            public string _moreInfoURL;
            public string _authors;
        }

        public VSIXPackageInformation GetCurrentVSIXPackageInformation()
        {
            VSIXPackageInformation outInfo = null;

            try
            {
                outInfo = new VSIXPackageInformation();

                // get ExtensionManager
                IVsExtensionManager manager = GetService(typeof(SVsExtensionManager)) as IVsExtensionManager;
                // get your extension by Product Id
                IInstalledExtension myExtension = manager.GetInstalledExtension("FASTBuildMonitorVSIX.44bf85a5-7635-4a2e-86d7-7b7f3bf757a8");
                // get current version
                outInfo._version = myExtension.Header.Version;
                outInfo._authors = myExtension.Header.Author;
                outInfo._packageName = myExtension.Header.Name;
                outInfo._moreInfoURL = myExtension.Header.MoreInfoUrl.OriginalString;
            }
            catch (System.Exception ex)
            {
                Console.WriteLine("Exception: " + ex.ToString());
            }

            return outInfo;
        }



    }

    #endregion
}
