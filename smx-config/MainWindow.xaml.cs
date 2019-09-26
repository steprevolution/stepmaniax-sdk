using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;

namespace smx_config
{
    public partial class MainWindow: Window
    {
        OnConfigChange onConfigChange;
        ShowAutoLightsColor showAutoLightsColor = new ShowAutoLightsColor();

        public MainWindow()
        {
            InitializeComponent();

            onConfigChange = new OnConfigChange(this, delegate (LoadFromConfigDelegateArgs args)
            {
                LoadUIFromConfig(args);
            });

            // If we're controlling GIF animations and the firmware doesn't support
            // doing animations internally, confirm exiting, since you can minimize
            // to tray to keep playing animations.  If we're not controlling animations,
            // or the firmware supports doing them automatically, don't bug the user
            // with a prompt.
            Closing += delegate (object sender, System.ComponentModel.CancelEventArgs e)
            {
                LoadFromConfigDelegateArgs args = CurrentSMXDevice.singleton.GetState();

                // Don't use ActivePads here.  Even if P1 is selected for configuration,
                // we can still be controlling animations for P2, so check both connected
                // pads.
                bool shouldConfirmExit = false;
                for(int pad = 0; pad < 2; ++pad)
                {
                    SMX.SMXConfig config;
                    if(!SMX.SMX.GetConfig(pad, out config))
                        continue;

                    // If the firmware is version 4 or higher, it supports animations directly.
                    // The user can upload GIF animations and doesn't need to leave us running
                    // for them to work.  You can still use this tool to drive animations, but
                    // don't confirm exiting.
                    if(config.masterVersion >= 4)
                        continue;

                    // If AutoLightingUsePressedAnimations isn't set, the panel is using step
                    // coloring instead of pressed animations.  All firmwares support this.
                    // Don't confirm exiting for this mode.
                    if((config.configFlags & SMX.SMXConfigFlags.AutoLightingUsePressedAnimations) == 0)
                        continue;

                    shouldConfirmExit = true;
                }

                if(!shouldConfirmExit)
                    return;

                MessageBoxResult result = MessageBox.Show(
                    "Close StepManiaX configuration?\n\n" +
                    "GIF animations will keep playing if the application is minimized.",
                    "StepManiaX", System.Windows.MessageBoxButton.YesNo);
                if(result == MessageBoxResult.No)
                    e.Cancel = true;
            };
        }

        List<string> thresholdSliderNames = new List<string>()
        {
            "up-left", "up", "up-right", "left", "center", "right", "down-left", "down", "down-right", "cardinal", "corner", "aux",
        };

        Dictionary<string, string> thresholdToIcon = new Dictionary<string, string>()
        {
            { "up-left",   "Resources/pad_up_left.png" },
            { "up",        "Resources/pad_up.png" },
            { "up-right",  "Resources/pad_up_right.png" },
            { "left",      "Resources/pad_left.png" },
            { "center",    "Resources/pad_center.png" },
            { "right",     "Resources/pad_right.png" },
            { "down-left", "Resources/pad_down_left.png" },
            { "down",      "Resources/pad_down.png" },
            { "down-right","Resources/pad_down_right.png" },
            { "cardinal",  "Resources/pad_cardinal.png" },
            { "corner",    "Resources/pad_diagonal.png" },
            { "aux",       "Resources/pad_diagonal.png" },
        };

        bool IsThresholdSliderShown(string type)
        {
            bool AdvancedModeEnabled = (bool)AdvancedModeEnabledCheckbox.IsChecked;
            SMX.SMXConfig config = ActivePad.GetFirstActivePadConfig();
            bool[] enabledPanels = config.GetEnabledPanels();

            // Check the list of sensors this slider controls.  If the list is empty, don't show it.
            // For example, if the user adds all four sensors on the up panel to aux, the up button
            // has nothing left to control, so we'll hide it.
            List<ThresholdSettings.PanelAndSensor> panelAndSensors = ThresholdSettings.GetControlledSensorsForSliderType(type, AdvancedModeEnabled);
            if(panelAndSensors.Count == 0)
                return false;

            // Hide thresholds that only affect panels that are disabled, so we don't show
            // corner panel sliders in advanced mode if the corner panels are disabled.  We
            // don't handle this in GetControlledSensorsForSliderType, since we do want cardinal
            // and corner to write thresholds to disabled panels, so they're in sync if they're
            // turned back on.
            switch(type)
            {
            case "up-left": return enabledPanels[0];
            case "up": return enabledPanels[1];
            case "up-right": return enabledPanels[2];
            case "left": return enabledPanels[3];
            case "center": return enabledPanels[4];
            case "right": return enabledPanels[5];
            case "down-left": return enabledPanels[6];
            case "down": return enabledPanels[7];
            case "down-right": return enabledPanels[8];

            // Show cardinal and corner if at least one panel they affect is enabled.
            case "cardinal": return enabledPanels[3] || enabledPanels[5] || enabledPanels[8];
            case "corner": return enabledPanels[0] || enabledPanels[2] || enabledPanels[6] || enabledPanels[8];
            default: return true;
            }
        }

        ThresholdSlider CreateThresholdSlider(string type)
        {
            ThresholdSlider slider = new ThresholdSlider();
            slider.Type = type;
            string iconPath = "pack://application:,,,/" + thresholdToIcon[type];
            slider.Icon = (new ImageSourceConverter()).ConvertFromString(iconPath) as ImageSource;
            return slider;
        }

        void CreateThresholdSliders()
        {
            SMX.SMXConfig config = ActivePad.GetFirstActivePadConfig();
            bool[] enabledPanels = config.GetEnabledPanels();

            // remove the threshold sliders from xaml, create them all here
            //
            // remove the AdvancedModeEnabled binding and ShouldBeDisplayed, handle that here
            // by creating the ones we need
            //
            // then we can add custom sliders too
            ThresholdSliderContainer.Children.Clear();
            foreach(string sliderName in thresholdSliderNames)
            {
                if(!IsThresholdSliderShown(sliderName))
                    continue;

                ThresholdSlider slider = CreateThresholdSlider(sliderName);
                DockPanel.SetDock(slider, Dock.Top);
                slider.Margin = new Thickness(0, 8, 0, 0);
                ThresholdSliderContainer.Children.Add(slider);
            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            // Add our WndProc hook.
            HwndSource source = (HwndSource)PresentationSource.FromVisual(this);
            source.AddHook(new HwndSourceHook(WndProcHook));

            Version1.Content = "SMXConfig version " + SMX.SMX.Version();
            Version2.Content = "SMXConfig version " + SMX.SMX.Version();

            AutoLightsColor.StartedDragging += delegate () { showAutoLightsColor.Start(); };
            AutoLightsColor.StoppedDragging += delegate () { showAutoLightsColor.Stop(); };
            AutoLightsColor.StoppedDragging += delegate () { showAutoLightsColor.Stop(); };

            CreateThresholdSliders();

            // This doesn't happen at the same time AutoLightsColor is used, since they're on different tabs.
            Diagnostics.SetShowAllLights += delegate (bool on)
            {
                if(on)
                    showAutoLightsColor.Start();
                else
                    showAutoLightsColor.Stop();
            };

            SetAllPanelsToCurrentColor.Click += delegate (object sender, RoutedEventArgs e)
            {
                // Get the color of the selected color button, and apply it to all other buttons.
                Color color = selectedButton.getColor();

                ColorButton[] colorButtons = getColorPickerButtons();
                foreach(ColorButton button in colorButtons)
                {
                    // Only apply this to panel colors, not the floor color.
                    if((button as PanelColorButton) == null)
                        continue;

                    button.setColor(color);
                }

                CurrentSMXDevice.singleton.FireConfigurationChanged(null);
            };

            // Listen to clicks on the panel color buttons.
            ColorButton[] buttons = getColorPickerButtons();
            foreach(ColorButton button in buttons)
            {
                button.Click += delegate (object sender, RoutedEventArgs e)
                {
                    ColorButton clickedButton = sender as ColorButton;
                    selectedButton = clickedButton;

                    RefreshSelectedColorPicker();
                };
            }
        }

        private void PressedColorModeButton(object sender, RoutedEventArgs e)
        {
            // The user pressed either the "panel colors" or "GIF animations" button.
            bool pressedPanelColors = sender == PanelColorsButton;

            foreach(Tuple<int, SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                SMX.SMXConfig config = activePad.Item2;

                // If we're in panel colors mode, clear the AutoLightingUsePressedAnimations flag.
                // Otherwise, set it.
                if(pressedPanelColors)
                    config.configFlags &= ~SMX.SMXConfigFlags.AutoLightingUsePressedAnimations;
                else
                    config.configFlags |= SMX.SMXConfigFlags.AutoLightingUsePressedAnimations;
                SMX.SMX.SetConfig(activePad.Item1, config);
            }

            CurrentSMXDevice.singleton.FireConfigurationChanged(null);
        }

        private void LoadUIFromConfig(LoadFromConfigDelegateArgs args)
        {
            bool EitherControllerConnected = args.controller[0].info.connected || args.controller[1].info.connected;
            Main.Visibility = EitherControllerConnected ? Visibility.Visible : Visibility.Hidden;
            Searching.Visibility = EitherControllerConnected ? Visibility.Hidden : Visibility.Visible;
            ConnectedPads.Visibility = EitherControllerConnected ? Visibility.Visible : Visibility.Hidden;
            PanelColorP1.Visibility = args.controller[0].info.connected ? Visibility.Visible : Visibility.Collapsed;
            PanelColorP2.Visibility = args.controller[1].info.connected ? Visibility.Visible : Visibility.Collapsed;
            EnableCenterTopSensorCheckbox.Visibility =
            P1_Floor.Visibility =
            P2_Floor.Visibility =
                args.firmwareVersion() >= 5 ? Visibility.Visible : Visibility.Collapsed;

            // Show the color slider or GIF UI depending on which one is set in flags.
            // If both pads are turned on, just use the first one.
            foreach(Tuple<int, SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                SMX.SMXConfig config = activePad.Item2;

                // If SMXConfigFlags_AutoLightingUsePressedAnimations is set, show the GIF UI.
                // If it's not set, show the color slider UI.
                SMX.SMXConfigFlags flags = config.configFlags;
                bool usePressedAnimations = (flags & SMX.SMXConfigFlags.AutoLightingUsePressedAnimations) != 0;
                ColorPickerGroup.Visibility = usePressedAnimations ? Visibility.Collapsed : Visibility.Visible;
                GIFGroup.Visibility = usePressedAnimations ? Visibility.Visible : Visibility.Collapsed;

                // Tell the color mode buttons which one is selected, to set the button highlight.
                PanelColorsButton.Selected = !usePressedAnimations;
                GIFAnimationsButton.Selected = usePressedAnimations;

                break;
            }

            RefreshConnectedPadList(args);
            RefreshUploadPadText(args);
            RefreshSelectedColorPicker();

            // If a device has connected or disconnected, refresh the displayed threshold
            // sliders.  Don't do this otherwise, or we'll do this when the sliders are
            // dragged.
            if(args.ConnectionsChanged)
                CreateThresholdSliders();

            // Show the threshold warning explanation if any panels are showing the threshold warning icon.
            bool ShowThresholdWarningText = false;
            foreach(Tuple<int, SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                SMX.SMXConfig config = activePad.Item2;
                for(int panelIdx = 0; panelIdx < 9; ++panelIdx)
                {
                    for(int sensor = 0; sensor < 4; ++sensor)
                    {
                        if(config.ShowThresholdWarning(panelIdx, sensor))
                            ShowThresholdWarningText = true;
                    }
                }
            }
            ThresholdWarningText.Visibility = ShowThresholdWarningText ? Visibility.Visible : Visibility.Hidden;

            // If a second controller has connected and we're on Both, see if we need to prompt
            // to sync configs.  We only actually need to do this if a controller just connected.
            if(args.ConfigurationChanged)
                CheckConfiguringBothPads(args);
        }

        ColorButton selectedButton;

        // Return all color picker buttons.
        ColorButton[] getColorPickerButtons()
        {
            return new ColorButton[] {
                P1_0, P1_1, P1_2,
                P1_3, P1_4, P1_5,
                P1_6, P1_7, P1_8,
                P1_Floor,

                P2_0, P2_1, P2_2,
                P2_3, P2_4, P2_5,
                P2_6, P2_7, P2_8,
                P2_Floor,
            };
        }

        // Update the selected color picker based on the value of selectedButton.
        private void RefreshSelectedColorPicker()
        {
            LoadFromConfigDelegateArgs args = CurrentSMXDevice.singleton.GetState();

            ColorButton[] buttons = getColorPickerButtons();

            // If our selected button isn't enabled (or no button is selected), try to select a
            // different one.
            if(selectedButton == null || !selectedButton.isEnabled(args))
            {
                foreach(ColorButton button in buttons)
                {
                    if(button.isEnabled(args))
                    {
                        selectedButton = button;
                        break;
                    }
                }
            }

            // Tell the buttons which one is selected.
            foreach(ColorButton button in buttons)
                button.IsSelected = button == selectedButton;

            // Tell the color slider which button is selected.
            AutoLightsColor.colorButton = selectedButton;
        }

        // Update which of the "Leave this application running", etc. blocks to display.
        private void RefreshUploadPadText(LoadFromConfigDelegateArgs args)
        {
            foreach(Tuple<int, SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                SMX.SMXConfig config = activePad.Item2;

                bool uploadsSupported = config.masterVersion >= 4;
                LeaveRunning.Visibility = uploadsSupported ? Visibility.Collapsed : Visibility.Visible;
                break;
            }
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
            ConnectedPadList.Visibility = TwoControllersConnected ? Visibility.Visible : Visibility.Collapsed;

            // Only show the P1/P2 text if only one controller is connected, since it takes the place
            // of the dropdown.
            P1Connected.Visibility = (!TwoControllersConnected && args.controller[0].info.connected) ? Visibility.Visible : Visibility.Collapsed;
            P2Connected.Visibility = (!TwoControllersConnected && args.controller[1].info.connected) ? Visibility.Visible : Visibility.Collapsed;

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
            foreach(Tuple<int, SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                int pad = activePad.Item1;
                SMX.SMX.FactoryReset(pad);
            }
            CurrentSMXDevice.singleton.FireConfigurationChanged(null);
        }

        private void AdvancedModeEnabledCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            CreateThresholdSliders();
        }

        private void ExportSettings(object sender, RoutedEventArgs e)
        {
            // Save the current thresholds on the first available pad as a preset.
            foreach(Tuple<int, SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                int pad = activePad.Item1;
                SMX.SMXConfig config = activePad.Item2;
                string json = SMXHelpers.ExportSettingsToJSON(config);

                Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog();
                dialog.FileName = "StepManiaX settings";
                dialog.DefaultExt = ".smxcfg";
                dialog.Filter = "StepManiaX settings (.smxcfg)|*.smxcfg";
                bool? result = dialog.ShowDialog();
                if(result == null || !(bool)result)
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
            if(result == null || !(bool)result)
                return;

            string json = Helpers.ReadFile(dialog.FileName);

            // Apply settings from the file to all active pads.
            foreach(Tuple<int, SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                int pad = activePad.Item1;
                SMX.SMXConfig config = activePad.Item2;

                SMXHelpers.ImportSettingsFromJSON(json, ref config);
                SMX.SMX.SetConfig(pad, config);
            }

            CurrentSMXDevice.singleton.FireConfigurationChanged(null);
        }

        private void LoadGIF(object sender, RoutedEventArgs e)
        {
            // If the "load idle GIF" button was pressed, load the released animation.
            // Otherwise, load the pressed animation.
            bool pressed = sender == this.LoadPressed;

            // Prompt for a file to read.
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.FileName = "Select an animated GIF";
            dialog.DefaultExt = ".gif";
            dialog.Filter = "Animated GIF (.gif)|*.gif";
            bool? result = dialog.ShowDialog();
            if(result == null || !(bool)result)
                return;

            byte[] buf = Helpers.ReadBinaryFile(dialog.FileName);
            SMX.SMX.LightsType type = pressed ? SMX.SMX.LightsType.LightsType_Pressed : SMX.SMX.LightsType.LightsType_Released;

            foreach(Tuple<int, SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                int pad = activePad.Item1;

                // Load the animation.
                string error;
                if(!SMX.SMX.LightsAnimation_Load(buf, pad, type, out error))
                {
                    // Any errors here are problems with the GIF, so there's no point trying
                    // to load it for the second pad if the first returns an error.  Just show the
                    // error and stop.
                    MessageBox.Show(error, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);

                    // Return without saving to settings on error.
                    return;
                }

                // Save the GIF to disk so we can load it quickly later.
                Helpers.SaveAnimationToDisk(pad, type, buf);

                // Refresh after loading a GIF to update the "Leave this application running" text.
                CurrentSMXDevice.singleton.FireConfigurationChanged(null);
            }

            // For firmwares that support it, upload the animation to the pad now.  Otherwise,
            // we'll run the animation directly.
            foreach(Tuple<int, SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                int pad = activePad.Item1;

                SMX.SMXConfig config;
                if(!SMX.SMX.GetConfig(pad, out config))
                    continue;

                if(config.masterVersion >= 4)
                    UploadLatestGIF();

                break;
            }
        }

        private void SetAuxSensors_Click(object sender, RoutedEventArgs e)
        {
            SetAuxSensors dialog = new SetAuxSensors();
            dialog.ShowDialog();
        }

        private void UploadLatestGIF()
        {
            // Create a progress window.  Center it on top of the main window.
            ProgressWindow dialog = new ProgressWindow();
            dialog.Left = (Left + Width/2) - (dialog.Width/2);
            dialog.Top = (Top + Height/2) - (dialog.Height/2);
            dialog.Title = "Storing animations on pad...";

            int[] CurrentProgress = new int[] { 0, 0 };

            // Upload graphics for all connected pads.  If two pads are connected
            // we can start both of these simultaneously, and they'll be sent in
            // parallel.
            int total = 0;
            foreach(Tuple<int,SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                int pad = activePad.Item1;
                SMX.SMX.LightsUpload_BeginUpload(pad, delegate(int progress) {
                    // This is called from a thread, so dispatch back to the main thread.
                    Dispatcher.Invoke(delegate() {
                        // Store progress, so we can sum both pads.
                        CurrentProgress[pad] = progress;

                        dialog.SetProgress(CurrentProgress[0] + CurrentProgress[1]);
                        if(progress == 100)
                            dialog.Close();
                    });
                });

                // Each pad that we start uploading to is 100 units of progress.
                total += 100;
                dialog.SetTotal(total);
            }

            // Show the progress window as a modal dialog.  This function won't return
            // until we call dialog.Close above.
            dialog.ShowDialog();
        }

        const int WM_SYSCOMMAND = 0x0112;
        const int SC_MINIMIZE = 0xF020;
        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            App application = (App) Application.Current;

            if(msg == WM_SYSCOMMAND && ((int)wparam & 0xFFF0) == SC_MINIMIZE)
            {
                // Cancel minimize, and call MinimizeToTray instead.
                handled = true;
                application.MinimizeToTray(); 
            }

            return IntPtr.Zero;
        }

        private void MainTab_Selected(object sender, RoutedEventArgs e)
        {
            // Refresh the threshold sliders, in case the enabled panels were changed
            // on the advanced tab.
            CreateThresholdSliders();
        }
    }
} 
