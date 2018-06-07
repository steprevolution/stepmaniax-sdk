using System;
using System.Windows;
using System.Runtime.InteropServices;

namespace smx_config
{
    public partial class App: Application
    {
        [DllImport("Kernel32")]
        private static extern void AllocConsole();
        [DllImport("SMX.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SMX_Internal_OpenConsole();

        App()
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionEventHandler;
            
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
        }

        private void UnhandledExceptionEventHandler(object sender, UnhandledExceptionEventArgs e)
        {
            string message = e.ExceptionObject.ToString();
            MessageBox.Show("SMXConfig encountered an unexpected error:\n\n" + message, "SMXConfig");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);

            // Shut down cleanly, to make sure we don't run any threaded callbacks during shutdown.
            Console.WriteLine("Application exiting");
            if(CurrentSMXDevice.singleton == null)
                return;
            CurrentSMXDevice.singleton.Shutdown();
            CurrentSMXDevice.singleton = null;
        }

    }
}
