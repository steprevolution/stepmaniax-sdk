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
            if(Helpers.GetDebug())
                SMX_Internal_OpenConsole();
            CurrentSMXDevice.singleton = new CurrentSMXDevice();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);

            // Shut down cleanly, to make sure we don't run any threaded callbacks during shutdown.
            Console.WriteLine("Application exiting");
            CurrentSMXDevice.singleton.Shutdown();
            CurrentSMXDevice.singleton = null;
        }

    }
}
