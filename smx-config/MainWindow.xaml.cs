using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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

            // If we're controlling GIF animations and the firmware doesn't support
            // doing animations internally, confirm exiting, since you can minimize
            // to tray to keep playing animations.  If we're not controlling animations,
            // or the firmware supports doing them automatically, don't bug the user
            // with a prompt.
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
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
                    if((config.configFlags & SMX.SMXConfigFlags.SMXConfigFlags_AutoLightingUsePressedAnimations) == 0)
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

            StateChanged += delegate(object sender, EventArgs e)
            {
                // Closing the main window entirely when minimized to the tray would be
                // nice, but with WPF we don't really save memory by doing that, so
                // just hide the window.
                ShowInTaskbar = WindowState != WindowState.Minimized;
            };
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            Version1.Content = "SMXConfig version " + SMX.SMX.Version();
            Version2.Content = "SMXConfig version " + SMX.SMX.Version();

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
                button.Click += delegate(object sender, RoutedEventArgs e)
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

            foreach(Tuple<int,SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                SMX.SMXConfig config = activePad.Item2;

                // If we're in panel colors mode, clear the AutoLightingUsePressedAnimations flag.
                // Otherwise, set it.
                if(pressedPanelColors)
                    config.configFlags &= ~SMX.SMXConfigFlags.SMXConfigFlags_AutoLightingUsePressedAnimations;
                else
                    config.configFlags |= SMX.SMXConfigFlags.SMXConfigFlags_AutoLightingUsePressedAnimations;
                SMX.SMX.SetConfig(activePad.Item1, config);
            }

            CurrentSMXDevice.singleton.FireConfigurationChanged(null);
        }

        private void LoadUIFromConfig(LoadFromConfigDelegateArgs args)
        {
            bool EitherControllerConnected = args.controller[0].info.connected || args.controller[1].info.connected;
            Main.Visibility = EitherControllerConnected? Visibility.Visible:Visibility.Hidden;
            Searching.Visibility = EitherControllerConnected? Visibility.Hidden:Visibility.Visible;
            ConnectedPads.Visibility = EitherControllerConnected? Visibility.Visible:Visibility.Hidden;
            PanelColorP1.Visibility = args.controller[0].info.connected? Visibility.Visible:Visibility.Collapsed;
            PanelColorP2.Visibility = args.controller[1].info.connected? Visibility.Visible:Visibility.Collapsed;

            // Show the color slider or GIF UI depending on which one is set in flags.
            // If both pads are turned on, just use the first one.
            foreach(Tuple<int,SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                SMX.SMXConfig config = activePad.Item2;

                // If SMXConfigFlags_AutoLightingUsePressedAnimations is set, show the GIF UI.
                // If it's not set, show the color slider UI.
                SMX.SMXConfigFlags flags = config.configFlags;
                bool usePressedAnimations = (flags & SMX.SMXConfigFlags.SMXConfigFlags_AutoLightingUsePressedAnimations) != 0;
                ColorPickerGroup.Visibility = usePressedAnimations? Visibility.Collapsed:Visibility.Visible;
                GIFGroup.Visibility = usePressedAnimations? Visibility.Visible:Visibility.Collapsed;

                // Tell the color mode buttons which one is selected, to set the button highlight.
                PanelColorsButton.Selected = !usePressedAnimations;
                GIFAnimationsButton.Selected = usePressedAnimations;

                break;
            }

            RefreshConnectedPadList(args);
            RefreshUploadPadText(args);
            RefreshSelectedColorPicker();

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
                bool uploadPossible = Helpers.PanelLoadErrors == null;
                
                LeaveRunning.Visibility = uploadsSupported? Visibility.Collapsed:Visibility.Visible;
                LeaveRunningOrUpload.Visibility = uploadsSupported && uploadPossible? Visibility.Visible:Visibility.Collapsed;
                LeaveRunningCantUpload.Visibility = uploadsSupported && !uploadPossible? Visibility.Visible:Visibility.Collapsed;

                // If we have an error reason, set it.  This is only visible when
                // we're showing LeaveRunningCantUpload.
                if(Helpers.PanelLoadErrors != null)
                    UploadErrorReason.Text = Helpers.PanelLoadErrors;
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
            if(result == null || !(bool) result)
                return;

            byte[] buf = Helpers.ReadBinaryFile(dialog.FileName);
            SMX.SMX.LightsType type = pressed? SMX.SMX.LightsType.LightsType_Pressed:SMX.SMX.LightsType.LightsType_Released;

            foreach(Tuple<int,SMX.SMXConfig> activePad in ActivePad.ActivePads())
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

                // Try to prepare animations for upload.  This updates Helpers.PanelLoadErrors.
                Helpers.PrepareLoadedAnimations();

                // Refresh after loading a GIF to update the "Leave this application running" text.
                CurrentSMXDevice.singleton.FireConfigurationChanged(null);
            }
        }

        // The "Upload animation to pad" button was clicked.
        private void UploadGIFs(object sender, RoutedEventArgs e)
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
    }
} 
