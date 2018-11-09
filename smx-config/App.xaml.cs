using System;
using System.Windows;
using System.Runtime.InteropServices;
using System.IO;

namespace smx_config
{
    public partial class App: Application
    {
        [DllImport("Kernel32")]
        private static extern void AllocConsole();
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SMX_Internal_OpenConsole();

        private System.Windows.Forms.NotifyIcon trayIcon;
        private MainWindow window;

        App()
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionEventHandler;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // If we're being launched on startup, but the LaunchOnStartup setting is false,
            // then the user turned off auto-launching but we're still being launched for some
            // reason (eg. a renamed launch shortcut that we couldn't find to remove).  As
            // a safety so we don't launch when the user doesn't want us to, just exit in this
            // case.
            if(Helpers.LaunchedOnStartup() && !LaunchOnStartup.Enable)
            {
                Shutdown();
                return;
            }

            LaunchOnStartup.Enable = true;
            if(!SMX.SMX.DLLExists())
            {
                MessageBox.Show("SMXConfig encountered an unexpected error.\n\nSMX.dll couldn't be found:\n\n" + Helpers.GetLastWin32ErrorString(), "SMXConfig");
                Current.Shutdown();
                return;
            }
            
            if(!SMX.SMX.DLLAvailable())
            {
                MessageBox.Show("SMXConfig encountered an unexpected error.\n\nSMX.dll failed to load:\n\n" + Helpers.GetLastWin32ErrorString(), "SMXConfig");
                Current.Shutdown();
                return;
            }

            if(Helpers.GetDebug())
                SMX_Internal_OpenConsole();

            CurrentSMXDevice.singleton = new CurrentSMXDevice();

            // Load animations, and tell the SDK to handle auto-lighting as long as
            // we're running.
            Helpers.LoadSavedPanelAnimations();
            Helpers.PrepareLoadedAnimations();
            SMX.SMX.LightsAnimation_SetAuto(true);

            CreateTrayIcon();

            // Create the main window.
            ToggleMainWindow();
        }

        // Open or close the main window.
        //
        // We don't create our UI until the first time it's opened, so we use
        // less memory when we're launched on startup.  However, when we're minimized
        // back to the tray, we don't destroy the main window.  WPF is just too
        // leaky to recreate the main window each time it's called due to internal
        // circular references.  Instead, we just focus on minimizing CPU overhead.
        void ToggleMainWindow()
        {
            if(window == null)
            {
                window = new MainWindow();
                window.Closed += MainWindowClosed;
                window.Show();
            }
            else if(window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
                window.Activate();
            }
            else
                window.WindowState = WindowState.Minimized;
        }

        private void MainWindowClosed(object sender, EventArgs e)
        {
            window = null;
        }

        private void UnhandledExceptionEventHandler(object sender, UnhandledExceptionEventArgs e)
        {
            string message = e.ExceptionObject.ToString();
            MessageBox.Show("SMXConfig encountered an unexpected error:\n\n" + message, "SMXConfig");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);

            Console.WriteLine("Application exiting");

            // Remove the tray icon.
            if(trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon = null;
            }

            // Shut down cleanly, to make sure we don't run any threaded callbacks during shutdown.
            if(CurrentSMXDevice.singleton != null)
            {
                CurrentSMXDevice.singleton.Shutdown();
                CurrentSMXDevice.singleton = null;
            }
        }

        // Create a tray icon.  For some reason there's no WPF interface for this,
        // so we have to use Forms.
        void CreateTrayIcon()
        {
            Stream iconStream = GetResourceStream(new Uri( "pack://application:,,,/Resources/window%20icon%20grey.ico")).Stream;
            System.Drawing.Icon icon = new System.Drawing.Icon(iconStream);

            trayIcon = new System.Windows.Forms.NotifyIcon();
            trayIcon.Text = "StepManiaX";
            trayIcon.Visible = true;

            // Show or hide the application window on click.
            trayIcon.Click += delegate (object sender, EventArgs e) { ToggleMainWindow();  };
            trayIcon.DoubleClick += delegate (object sender, EventArgs e) { ToggleMainWindow();  };

            CurrentSMXDevice.singleton.ConfigurationChanged += delegate(LoadFromConfigDelegateArgs args) {
                RefreshTrayIcon(args);
            };

            // Do the initial refresh.
            RefreshTrayIcon(CurrentSMXDevice.singleton.GetState(), true);
        }

        // Refresh the tray icon when we're connected or disconnected.
        bool wasConnected;
        void RefreshTrayIcon(LoadFromConfigDelegateArgs args, bool force=false)
        {
            if(trayIcon == null)
                return;

            bool EitherControllerConnected = false;
            for(int pad = 0; pad < 2; ++pad)
                if(args.controller[pad].info.connected)
                    EitherControllerConnected = true;

            // Skip the refresh if the connected state didn't change.
            if(wasConnected == EitherControllerConnected && !force)
                return;
            wasConnected = EitherControllerConnected;

            trayIcon.Text = EitherControllerConnected? "StepManiaX (connected)":"StepManiaX (disconnected)";

            // Set the tray icon.
            string filename = EitherControllerConnected? "window%20icon.ico":"window%20icon%20grey.ico";
            Stream iconStream = GetResourceStream(new Uri( "pack://application:,,,/Resources/" + filename)).Stream;
            trayIcon.Icon = new System.Drawing.Icon(iconStream);
        }
    }
}
