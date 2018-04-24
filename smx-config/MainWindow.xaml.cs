using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace smx_config
{
    public partial class MainWindow: Window
    {
        OnConfigChange onConfigChange;
        ShowAutoLightsColor showAutoLightsColor = new ShowAutoLightsColor();

        public MainWindow()
        {
            InitializeComponent();

            onConfigChange = new OnConfigChange(this, delegate(LoadFromConfigDelegateArgs args) {
                LoadUIFromConfig(args);
            });
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            AutoLightsColor.StartedDragging += delegate() { showAutoLightsColor.Start(); };
            AutoLightsColor.StoppedDragging += delegate() { showAutoLightsColor.Stop(); };
            AutoLightsColor.StoppedDragging += delegate() { showAutoLightsColor.Stop(); };

            // This doesn't happen at the same time AutoLightsColor is used, since they're on different tabs.
            Diagnostics.SetShowAllLights += delegate(bool on)
            {
                if(on)
                    showAutoLightsColor.Start();
                else
                    showAutoLightsColor.Stop();
            };

            SetAllPanelsToCurrentColor.Click += delegate(object sender, RoutedEventArgs e)
            {
                int SelectedPanel = AutoLightsColor.SelectedPanel % 9;
                int SelectedPad = AutoLightsColor.SelectedPanel < 9? 0:1;

                // Get the color of the selected pad.
                SMX.SMXConfig copyFromConfig;
                if(!SMX.SMX.GetConfig(SelectedPad, out copyFromConfig))
                    return;

                // Don't use ActivePad.ActivePads here, since the lights UI handles multiple pads on its own.
                for(int pad = 0; pad < 2; ++pad)
                {
                    SMX.SMXConfig config;
                    if(!SMX.SMX.GetConfig(pad, out config))
                        continue;

                    // Set all stepColors to the color of the selected panel.
                    for(int i = 0; i < 9; ++i)
                    {
                        config.stepColor[i*3+0] = copyFromConfig.stepColor[SelectedPanel*3+0];
                        config.stepColor[i*3+1] = copyFromConfig.stepColor[SelectedPanel*3+1];
                        config.stepColor[i*3+2] = copyFromConfig.stepColor[SelectedPanel*3+2];
                    }

                    SMX.SMX.SetConfig(pad, config);
                }
                CurrentSMXDevice.singleton.FireConfigurationChanged(null);
            };
        }

        private void LoadUIFromConfig(LoadFromConfigDelegateArgs args)
        {
            bool EitherControllerConnected = args.controller[0].info.connected || args.controller[1].info.connected;
            Main.Visibility = EitherControllerConnected? Visibility.Visible:Visibility.Hidden;
            Searching.Visibility = EitherControllerConnected? Visibility.Hidden:Visibility.Visible;
            ConnectedPads.Visibility = EitherControllerConnected? Visibility.Visible:Visibility.Hidden;
            PanelColorP1.Visibility = args.controller[0].info.connected? Visibility.Visible:Visibility.Collapsed;
            PanelColorP2.Visibility = args.controller[1].info.connected? Visibility.Visible:Visibility.Collapsed;

            RefreshConnectedPadList(args);

            // If a second controller has connected and we're on Both, see if we need to prompt
            // to sync configs.  We only actually need to do this if a controller just connected.
            if(args.ConfigurationChanged)
                CheckConfiguringBothPads(args);
        }

        private void ConnectedPadList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem selection = ConnectedPadList.SelectedItem as ComboBoxItem;
            ActivePad.SelectedPad newSelection;
            if(selection == ConnectedPadList_P1)
                newSelection = ActivePad.SelectedPad.P1;
            else if(selection == ConnectedPadList_P2)
                newSelection = ActivePad.SelectedPad.P2;
            else
                newSelection = ActivePad.SelectedPad.Both;
            if(ActivePad.selectedPad == newSelection)
                return;

            ActivePad.selectedPad = newSelection;

            // Before firing and updating UI, run CheckConfiguringBothPads to see if we should
            // sync the config and/or change the selection again.
            CheckConfiguringBothPads(CurrentSMXDevice.singleton.GetState());

            CurrentSMXDevice.singleton.FireConfigurationChanged(null);
        }

        // If the user selects "Both", or connects a second pad while on "Both", both pads need
        // to have the same configuration, since we're configuring them together.  Check if the
        // configuration is out of sync, and ask the user before syncing them up so we don't
        // clobber P2's configuration if this wasn't intended.
        //
        // If the user cancels, change the pad selection to P1 so we don't clobber P2.
        private void CheckConfiguringBothPads(LoadFromConfigDelegateArgs args)
        {
            // Check if we're actually in "Both" mode with two controllers connected.  If not,
            // we don't have to do anything.
            bool Pad1Connected = args.controller[0].info.connected;
            bool Pad2Connected = args.controller[1].info.connected;
            if(ActivePad.selectedPad != ActivePad.SelectedPad.Both || !Pad1Connected || !Pad2Connected)
                return;

            // If the two pads have the same configuration, there's nothing to do.
            SMX.SMXConfig config1 = args.controller[0].config;
            SMX.SMXConfig config2 = args.controller[1].config;
            if(ConfigurationsSynced(config1, config2))
                return;

            string messageBoxText = "The two pads have different settings.  Do you want to " +
                "match P2 settings to P1 and configure both pads together?  (This won't affect panel colors.)";
            MessageBoxResult result = MessageBox.Show(messageBoxText, "StepManiaX", MessageBoxButton.YesNo, MessageBoxImage.None);
            if(result == MessageBoxResult.Yes)
            {
                SyncP2FromP1(config1, config2);
                return;
            }
            else
            {
                // Switch to P1.
                ActivePad.selectedPad = ActivePad.SelectedPad.P1;
                RefreshConnectedPadList(CurrentSMXDevice.singleton.GetState());
            }

        }

        // Return true if the two pads have the same configuration, so we can configure them together
        // without clobbering separate configurations.
        bool ConfigurationsSynced(SMX.SMXConfig config1, SMX.SMXConfig config2)
        {
            if(!Enumerable.SequenceEqual(config1.GetLowThresholds(), config2.GetLowThresholds()))
                return false;
            if(!Enumerable.SequenceEqual(config1.GetHighThresholds(), config2.GetHighThresholds()))
                return false;
            if(!Enumerable.SequenceEqual(config1.enabledSensors, config2.enabledSensors))
                return false;
            return true;
        }

        // Copy the P2 pad configuration to P1.
        //
        // This only copies settings that we actually configure, and it doesn't copy pad
        // colors, which is separate from pad selection.
        void SyncP2FromP1(SMX.SMXConfig config1, SMX.SMXConfig config2)
        {
            // Copy P1's configuration to P2.
            Array.Copy(config1.enabledSensors, config2.enabledSensors, config1.enabledSensors.Count());
            config2.SetLowThresholds(config1.GetLowThresholds());
            config2.SetHighThresholds(config1.GetHighThresholds());
            SMX.SMX.SetConfig(1, config2);
            CurrentSMXDevice.singleton.FireConfigurationChanged(null);
        }

        // Refresh which items are visible in the connected pads list, and which item is displayed as selected.
        private void RefreshConnectedPadList(LoadFromConfigDelegateArgs args)
        {
            bool TwoControllersConnected = args.controller[0].info.connected && args.controller[1].info.connected;

            // Only show the dropdown if two controllers are connected.
            ConnectedPadList.Visibility = TwoControllersConnected? Visibility.Visible:Visibility.Collapsed;

            // Only show the P1/P2 text if only one controller is connected, since it takes the place
            // of the dropdown.
            P1Connected.Visibility = (!TwoControllersConnected && args.controller[0].info.connected)? Visibility.Visible:Visibility.Collapsed;
            P2Connected.Visibility = (!TwoControllersConnected && args.controller[1].info.connected)? Visibility.Visible:Visibility.Collapsed;

            if(!TwoControllersConnected)
                return;

            // Set the current selection.
            ActivePad.SelectedPad selectedPad = ActivePad.selectedPad;
            switch(ActivePad.selectedPad)
            {
            case ActivePad.SelectedPad.P1: ConnectedPadList.SelectedItem = ConnectedPadList_P1; break;
            case ActivePad.SelectedPad.P2: ConnectedPadList.SelectedItem = ConnectedPadList_P2; break;
            case ActivePad.SelectedPad.Both: ConnectedPadList.SelectedItem = ConnectedPadList_Both; break;
            }
        }


        private void FactoryReset_Click(object sender, RoutedEventArgs e)
        {
            foreach(Tuple<int,SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                int pad = activePad.Item1;
                SMX.SMX.FactoryReset(pad);
            }
            CurrentSMXDevice.singleton.FireConfigurationChanged(null);
        }

        private void ExportSettings(object sender, RoutedEventArgs e)
        {
            // Save the current thresholds on the first available pad as a preset.
            foreach(Tuple<int,SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                int pad = activePad.Item1;
                SMX.SMXConfig config = activePad.Item2;
                string json = SMXHelpers.ExportSettingsToJSON(config);

                Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog();
                dialog.FileName = "StepManiaX settings";
                dialog.DefaultExt = ".smxcfg";
                dialog.Filter = "StepManiaX settings (.smxcfg)|*.smxcfg";
                bool? result = dialog.ShowDialog();
                if(result == null || !(bool) result)
                    return;

                System.IO.File.WriteAllText(dialog.FileName, json);
                return;
            }
        }

        private void ImportSettings(object sender, RoutedEventArgs e)
        {
            // Prompt for a file to read.
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.FileName = "StepManiaX settings";
            dialog.DefaultExt = ".smxcfg";
            dialog.Filter = "StepManiaX settings (.smxcfg)|*.smxcfg";
            bool? result = dialog.ShowDialog();
            if(result == null || !(bool) result)
                return;

            string json = Helpers.ReadFile(dialog.FileName);

            // Apply settings from the file to all active pads.
            foreach(Tuple<int,SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                int pad = activePad.Item1;
                SMX.SMXConfig config = activePad.Item2;

                SMXHelpers.ImportSettingsFromJSON(json, ref config);
                SMX.SMX.SetConfig(pad, config);
            }

            CurrentSMXDevice.singleton.FireConfigurationChanged(null);
        }
    }
} 
