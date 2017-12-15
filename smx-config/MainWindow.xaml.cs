using System;
using System.Windows;

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
            P1Connected.IsEnabled = args.controller[0].info.connected;
            P2Connected.IsEnabled = args.controller[1].info.connected;
            PanelColorP1.Visibility = args.controller[0].info.connected? Visibility.Visible:Visibility.Collapsed;
            PanelColorP2.Visibility = args.controller[1].info.connected? Visibility.Visible:Visibility.Collapsed;
        }

        private void FactoryReset_Click(object sender, RoutedEventArgs e)
        {
            for(int pad = 0; pad < 2; ++pad)
                SMX.SMX.FactoryReset(pad);
            CurrentSMXDevice.singleton.FireConfigurationChanged(null);
        }
    }
} 
