using System;
using System.Collections.Generic;
using System.Windows;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Controls;
using System.ComponentModel;

namespace smx_config
{
    // The state and configuration of a pad.
    public struct LoadFromConfigDelegateArgsPerController
    {
        public SMX.SMXInfo info;
        public SMX.SMXConfig config;
        public SMX.SMXSensorTestModeData test_data;

        // The panels that are activated.  Note that to receive notifications from OnConfigChange
        // when inputs change state, set RefreshOnInputChange to true.  Otherwise, this field will
        // be filled in but notifications won't be sent due to only inputs changing.
        public bool[] inputs;
    }

    public struct LoadFromConfigDelegateArgs
    {
        // This indicates which fields changed since the last call.
        public bool ConfigurationChanged, InputChanged, TestDataChanged;

        // Data for each of two controllers:
        public LoadFromConfigDelegateArgsPerController[] controller;

        // The control that changed the configuration (passed to FireConfigurationChanged).
        public object source;
    };

    // This class tracks the device we're currently configuring, and runs a callback when
    // it changes.
    class CurrentSMXDevice
    {
        public static CurrentSMXDevice singleton;

        // This is fired when FireConfigurationChanged is called, and when the current device
        // changes.
        public delegate void ConfigurationChangedDelegate(LoadFromConfigDelegateArgs args);
        public event ConfigurationChangedDelegate ConfigurationChanged;

        private bool[] WasConnected = new bool[2] { false, false };
        private bool[][] LastInputs = new bool[2][];
        private SMX.SMXSensorTestModeData[] LastTestData = new SMX.SMXSensorTestModeData[2];
        private Dispatcher MainDispatcher;

        public CurrentSMXDevice()
        {
            // Grab the main thread's dispatcher, so we can invoke into it.
            MainDispatcher = Dispatcher.CurrentDispatcher;

            // Set our update callback.  This will be called when something happens: connection or disconnection,
            // inputs changed, configuration updated, test data updated, etc.  It doesn't specify what's changed,
            // we simply check the whole state.
            SMX.SMX.Start(delegate(int PadNumber, SMX.SMX.SMXUpdateCallbackReason reason) {
                // Console.WriteLine("... " + reason);
                // This is called from a thread, with SMX's internal mutex locked.  We must not call into SMX
                // or do anything with the UI from here.  Just queue an update back into the UI thread.
                MainDispatcher.InvokeAsync(delegate() {
                    switch(reason)
                    {
                    case SMX.SMX.SMXUpdateCallbackReason.Updated:
                        CheckForChanges();
                        break;
                    case SMX.SMX.SMXUpdateCallbackReason.FactoryResetCommandComplete:
                        Console.WriteLine("SMX_FactoryResetCommandComplete");
                        FireConfigurationChanged(null);
                        break;
                    }
                });
            });
        }

        public void Shutdown()
        {
            SMX.SMX.Stop();
        }

        private void CheckForChanges()
        {
            LoadFromConfigDelegateArgs args = GetState();

            // Mark which parts have changed.
            //
            // For configuration, we only check for connection state changes.  Actual configuration
            // changes are fired by controls via FireConfigurationChanged.
            for(int pad = 0; pad < 2; ++pad)
            {
                LoadFromConfigDelegateArgsPerController controller = args.controller[pad];
                if(WasConnected[pad] != controller.info.connected)
                {
                    args.ConfigurationChanged = true;
                    WasConnected[pad] = controller.info.connected;
                }

                if(LastInputs[pad] == null || !Enumerable.SequenceEqual(controller.inputs, LastInputs[pad]))
                {
                    args.InputChanged = true;
                    LastInputs[pad] = controller.inputs;
                }

                if(!controller.test_data.Equals(LastTestData[pad]))
                {
                    args.TestDataChanged = true;
                    LastTestData[pad] = controller.test_data;
                }
            }

            // Only fire the delegate if something has actually changed.
            if(args.ConfigurationChanged || args.InputChanged || args.TestDataChanged)
                ConfigurationChanged?.Invoke(args);
        }

        public void FireConfigurationChanged(object source)
        {
            LoadFromConfigDelegateArgs args = GetState();
            args.ConfigurationChanged = true;
            args.source = source;
            ConfigurationChanged?.Invoke(args);
        }

        public LoadFromConfigDelegateArgs GetState()
        {
            LoadFromConfigDelegateArgs args = new LoadFromConfigDelegateArgs();
            args.controller = new LoadFromConfigDelegateArgsPerController[2];

            for(int pad = 0; pad < 2; ++pad)
            {
                LoadFromConfigDelegateArgsPerController controller;
                controller.test_data = new SMX.SMXSensorTestModeData();

                // Expand the inputs mask to an array.
                UInt16 Inputs = SMX.SMX.GetInputState(pad);
                controller.inputs = new bool[9];
                for(int i = 0; i < 9; ++i)
                    controller.inputs[i] = (Inputs & (1 << i)) != 0;
                SMX.SMX.GetInfo(pad, out controller.info);
                SMX.SMX.GetConfig(pad, out controller.config);
                SMX.SMX.GetTestData(pad, out controller.test_data);
                args.controller[pad] = controller;
            }

            return args;
        }

    }

    // Call a delegate on configuration change.  Configuration changes are notified by calling
    // FireConfigurationChanged.  Listeners won't receive notifications for changes that they
    // fired themselves.
    public class OnConfigChange
    {
        public delegate void LoadFromConfigDelegate(LoadFromConfigDelegateArgs args);
        private readonly Control Owner;
        private readonly LoadFromConfigDelegate Callback;
        private bool _RefreshOnInputChange = false;

        // If set to true, the callback will be invoked on input changes in addition to configuration
        // changes.  This can cause the callback to be run at any time, such as while the user is
        // interacting with the control.
        public bool RefreshOnInputChange {
            get { return _RefreshOnInputChange; }
            set {_RefreshOnInputChange = value; }
        }

        private bool _RefreshOnTestDataChange = false;

        // Like RefreshOnInputChange, but enables callbacks when test data changes.
        public bool RefreshOnTestDataChange {
            get { return _RefreshOnTestDataChange; }
            set { _RefreshOnTestDataChange = value; }
        }

        // Owner is the Control that we're calling.  This callback will be disable when the
        // control is unloaded, and we won't call it if it's the same control that fired
        // the change via FireConfigurationChanged.
        //
        // In addition, the callback is called when the control is Loaded, to load the initial
        // state.
        public OnConfigChange(Control owner, LoadFromConfigDelegate callback)
        {
            Owner = owner;
            Callback = callback;

            Owner.Loaded += delegate(object sender, RoutedEventArgs e)
            {
                if(CurrentSMXDevice.singleton != null)
                    CurrentSMXDevice.singleton.ConfigurationChanged += ConfigurationChanged;
                Refresh();
            };

            Owner.Unloaded += delegate(object sender, RoutedEventArgs e)
            {
                if(CurrentSMXDevice.singleton != null)
                    CurrentSMXDevice.singleton.ConfigurationChanged -= ConfigurationChanged;
            };
        }

        private void ConfigurationChanged(LoadFromConfigDelegateArgs args)
        {
            if(args.ConfigurationChanged ||
                (RefreshOnInputChange && args.InputChanged) ||
                (RefreshOnTestDataChange && args.TestDataChanged))
            {
                Callback(args);
            }
        }

        private void Refresh()
        {
            if(CurrentSMXDevice.singleton != null)
                Callback(CurrentSMXDevice.singleton.GetState());
        }
    };


    public class OnInputChange
    {
        public delegate void LoadFromConfigDelegate(LoadFromConfigDelegateArgs args);
        private readonly Control Owner;
        private readonly LoadFromConfigDelegate Callback;

        // Owner is the Control that we're calling.  This callback will be disable when the
        // control is unloaded, and we won't call it if it's the same control that fired
        // the change via FireConfigurationChanged.
        //
        // In addition, the callback is called when the control is Loaded, to load the initial
        // state.
        public OnInputChange(Control owner, LoadFromConfigDelegate callback)
        {
            Owner = owner;
            Callback = callback;

            // This is available when the application is running, but will be null in the XAML designer.
            if(CurrentSMXDevice.singleton == null)
                return;

            Owner.Loaded += delegate(object sender, RoutedEventArgs e)
            {
                CurrentSMXDevice.singleton.ConfigurationChanged += ConfigurationChanged;
                Refresh();
            };

            Owner.Unloaded += delegate(object sender, RoutedEventArgs e)
            {
                CurrentSMXDevice.singleton.ConfigurationChanged -= ConfigurationChanged;
            };
        }

        private void ConfigurationChanged(LoadFromConfigDelegateArgs args)
        {
            Callback(args);
        }

        private void Refresh()
        {
            if(CurrentSMXDevice.singleton != null)
                Callback(CurrentSMXDevice.singleton.GetState());
        }
    };
}
