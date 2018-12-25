using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Shapes;
using System.Windows.Media.Imaging;
using System.ComponentModel;

namespace smx_config
{
    // The checkbox to enable and disable the advanced per-panel sliders.
    //
    // This is always enabled if the thresholds in the configuration are set to different
    // values.  If the user enables us, we'll remember that we were forced on.  If the user
    // disables us, we'll sync the thresholds back up and turn the ForcedOn flag off.
    public class AdvancedThresholdViewCheckbox: CheckBox
    {
        public static readonly DependencyProperty AdvancedModeEnabledProperty = DependencyProperty.Register("AdvancedModeEnabled",
            typeof(bool), typeof(AdvancedThresholdViewCheckbox), new FrameworkPropertyMetadata(false));
        public bool AdvancedModeEnabled {
            get { return (bool) GetValue(AdvancedModeEnabledProperty); }
            set { SetValue(AdvancedModeEnabledProperty, value); }
        }

        OnConfigChange onConfigChange;

        // If true, the user enabled advanced view and we should display it even if
        // the thresholds happen to be synced.  If false, we'll only show the advanced
        // view if we need to because the thresholds aren't synced.
        bool ForcedOn;
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            onConfigChange = new OnConfigChange(this, delegate (LoadFromConfigDelegateArgs args) {
                LoadUIFromConfig(ActivePad.GetFirstActivePadConfig(args));
            });
        }

        private void LoadUIFromConfig(SMX.SMXConfig config)
        {
            // The master version doesn't actually matter, but we use this as a signal that the panels
            // have a new enough firmware to support this.
            bool SupportsAdvancedMode = config.masterVersion != 0xFF && config.masterVersion >= 2;
            Visibility = SupportsAdvancedMode? Visibility.Visible:Visibility.Collapsed;

            // If the thresholds are different, force the checkbox on.  This way, if you load the application
            // with a platform with per-panel thresholds, and change the thresholds to no longer be different,
            // advanced mode stays forced on.  It'll only turn off if you uncheck the box, or if you exit
            // the application with synced thresholds and then restart it.
            if(SupportsAdvancedMode && !ConfigPresets.AreUnifiedThresholdsSynced(config))
                ForcedOn = true;

            // Enable advanced mode if the master says it's supported, and either the user has checked the
            // box to turn it on or the thresholds are different in the current configuration.
            AdvancedModeEnabled = SupportsAdvancedMode && ForcedOn;
        }

        protected override void OnClick()
        {
            if(AdvancedModeEnabled)
            {
                // Stop forcing advanced mode on, and sync the thresholds so we exit advanced mode.
                ForcedOn = false;

                foreach(Tuple<int,SMX.SMXConfig> activePad in ActivePad.ActivePads())
                {
                    int pad = activePad.Item1;
                    SMX.SMXConfig config = activePad.Item2;
                    ConfigPresets.SyncUnifiedThresholds(ref config);
                    SMX.SMX.SetConfig(pad, config);
                }
                CurrentSMXDevice.singleton.FireConfigurationChanged(this);
            }
            else
            {
                // Enable advanced mode.
                ForcedOn = true;
            }

            // Refresh the UI.
            LoadUIFromConfig(ActivePad.GetFirstActivePadConfig());
        }
    }

    // This implements the threshold slider widget for changing an upper/lower threshold pair.
    public class ThresholdSlider: Control
    {
        public static readonly DependencyProperty IconProperty = DependencyProperty.Register("Icon",
            typeof(ImageSource), typeof(ThresholdSlider), new FrameworkPropertyMetadata(null));

        public ImageSource Icon {
            get { return (ImageSource) GetValue(IconProperty); }
            set { SetValue(IconProperty, value); }
        }

        public static readonly DependencyProperty TypeProperty = DependencyProperty.Register("Type",
            typeof(string), typeof(ThresholdSlider), new FrameworkPropertyMetadata(""));

        public string Type {
            get { return (string) GetValue(TypeProperty); }
            set { SetValue(TypeProperty, value); }
        }

        public static readonly DependencyProperty AdvancedModeEnabledProperty = DependencyProperty.Register("AdvancedModeEnabled",
            typeof(bool), typeof(ThresholdSlider), new FrameworkPropertyMetadata(false, RefreshAdvancedModeEnabledCallback));

        public bool AdvancedModeEnabled {
            get { return (bool) GetValue(AdvancedModeEnabledProperty); }
            set { SetValue(AdvancedModeEnabledProperty, value); }
        }

        private static void RefreshAdvancedModeEnabledCallback(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            ThresholdSlider self = target as ThresholdSlider;
            self.RefreshVisibility();
        }

        DoubleSlider slider;
        Label LowerLabel, UpperLabel;

        OnConfigChange onConfigChange;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            slider = GetTemplateChild("Slider") as DoubleSlider;
            LowerLabel = GetTemplateChild("LowerValue") as Label;
            UpperLabel = GetTemplateChild("UpperValue") as Label;

            slider.ValueChanged += delegate(DoubleSlider slider) { SaveToConfig(); };

            onConfigChange = new OnConfigChange(this, delegate (LoadFromConfigDelegateArgs args) {
                LoadUIFromConfig(ActivePad.GetFirstActivePadConfig(args));
            });
        }

        private void SetValueToConfig(ref SMX.SMXConfig config)
        {
            if(config.masterVersion < 4)
            {
                byte lower = (byte) slider.LowerValue;
                byte upper = (byte) slider.UpperValue;

                switch(Type)
                {
                case "up-left":    config.panelThreshold0Low = lower;     config.panelThreshold0High = upper; break;
                case "up":         config.panelThreshold1Low = lower;     config.panelThreshold1High = upper; break;
                case "up-right":   config.panelThreshold2Low = lower;     config.panelThreshold2High = upper; break;
                case "left":       config.panelThreshold3Low = lower;     config.panelThreshold3High = upper; break;
                case "center":     config.panelThreshold4Low = lower;     config.panelThreshold4High = upper; break;
                case "right":      config.panelThreshold5Low = lower;     config.panelThreshold5High = upper; break;
                case "down-left":  config.panelThreshold6Low = lower;     config.panelThreshold6High = upper; break;
                case "down":       config.panelThreshold7Low = lower;     config.panelThreshold7High = upper; break;
                case "down-right": config.panelThreshold8Low = lower;     config.panelThreshold8High = upper; break;
                case "cardinal":   config.panelThreshold7Low = lower;     config.panelThreshold7High = upper; break;
                case "corner":     config.panelThreshold2Low = lower;     config.panelThreshold2High = upper; break;
                }
            } else {
                UInt16 lower = (UInt16) slider.LowerValue;
                UInt16 upper = (UInt16) slider.UpperValue;

                switch(Type)
                {
                case "up-left":    config.individualPanelFSRLow[0] = lower; config.individualPanelFSRHigh[0] = upper; break;
                case "up":         config.individualPanelFSRLow[1] = lower; config.individualPanelFSRHigh[1] = upper; break;
                case "up-right":   config.individualPanelFSRLow[2] = lower; config.individualPanelFSRHigh[2] = upper; break;
                case "left":       config.individualPanelFSRLow[3] = lower; config.individualPanelFSRHigh[3] = upper; break;
                case "center":     config.individualPanelFSRLow[4] = lower; config.individualPanelFSRHigh[4] = upper; break;
                case "right":      config.individualPanelFSRLow[5] = lower; config.individualPanelFSRHigh[5] = upper; break;
                case "down-left":  config.individualPanelFSRLow[6] = lower; config.individualPanelFSRHigh[6] = upper; break;
                case "down":       config.individualPanelFSRLow[7] = lower; config.individualPanelFSRHigh[7] = upper; break;
                case "down-right": config.individualPanelFSRLow[8] = lower; config.individualPanelFSRHigh[8] = upper; break;
                case "cardinal":   config.individualPanelFSRLow[7] = lower; config.individualPanelFSRHigh[7] = upper; break;
                case "corner":     config.individualPanelFSRLow[2] = lower; config.individualPanelFSRHigh[2] = upper; break;
                }
            }

            // If we're not in advanced mode, sync the cardinal value to each of the panel values.
            if(!AdvancedModeEnabled)
                ConfigPresets.SyncUnifiedThresholds(ref config);
        }

        private void GetValueFromConfig(SMX.SMXConfig config, out int lower, out int upper)
        {
            if(config.masterVersion < 4)
            {
                switch(Type)
                {
                case "up-left":    lower = config.panelThreshold0Low;     upper = config.panelThreshold0High; return;
                case "up":         lower = config.panelThreshold1Low;     upper = config.panelThreshold1High; return;
                case "up-right":   lower = config.panelThreshold2Low;     upper = config.panelThreshold2High; return;
                case "left":       lower = config.panelThreshold3Low;     upper = config.panelThreshold3High; return;
                case "center":     lower = config.panelThreshold4Low;     upper = config.panelThreshold4High; return;
                case "right":      lower = config.panelThreshold5Low;     upper = config.panelThreshold5High; return;
                case "down-left":  lower = config.panelThreshold6Low;     upper = config.panelThreshold6High; return;
                case "down":       lower = config.panelThreshold7Low;     upper = config.panelThreshold7High; return;
                case "down-right": lower = config.panelThreshold8Low;     upper = config.panelThreshold8High; return;
                case "cardinal":   lower = config.panelThreshold7Low;     upper = config.panelThreshold7High; return;
                case "corner":     lower = config.panelThreshold2Low;     upper = config.panelThreshold2High; return;
                default:
                    lower = upper = 0;
                    return;
                }
            } else {
                switch(Type)
                {
                case "up-left":    lower = config.individualPanelFSRLow[0];     upper = config.individualPanelFSRHigh[0]; return;
                case "up":         lower = config.individualPanelFSRLow[1];     upper = config.individualPanelFSRHigh[1]; return;
                case "up-right":   lower = config.individualPanelFSRLow[2];     upper = config.individualPanelFSRHigh[2]; return;
                case "left":       lower = config.individualPanelFSRLow[3];     upper = config.individualPanelFSRHigh[3]; return;
                case "center":     lower = config.individualPanelFSRLow[4];     upper = config.individualPanelFSRHigh[4]; return;
                case "right":      lower = config.individualPanelFSRLow[5];     upper = config.individualPanelFSRHigh[5]; return;
                case "down-left":  lower = config.individualPanelFSRLow[6];     upper = config.individualPanelFSRHigh[6]; return;
                case "down":       lower = config.individualPanelFSRLow[7];     upper = config.individualPanelFSRHigh[7]; return;
                case "down-right": lower = config.individualPanelFSRLow[8];     upper = config.individualPanelFSRHigh[8]; return;
                case "cardinal": lower = config.individualPanelFSRLow[7];       upper = config.individualPanelFSRHigh[7]; return;
                case "corner": lower = config.individualPanelFSRLow[2];         upper = config.individualPanelFSRHigh[2]; return;
                default:
                    lower = upper = 0;
                    return;
                }
            }
        }

        private void SaveToConfig()
        {
            if(UpdatingUI)
                return;

            // Apply the change and save it to the devices.
            foreach(Tuple<int,SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                int pad = activePad.Item1;
                SMX.SMXConfig config = activePad.Item2;

                SetValueToConfig(ref config);
                SMX.SMX.SetConfig(pad, config);
                CurrentSMXDevice.singleton.FireConfigurationChanged(this);
            }
        }

        bool UpdatingUI = false;
        private void LoadUIFromConfig(SMX.SMXConfig config)
        {
            // Make sure SaveToConfig doesn't treat these as the user changing values.
            UpdatingUI = true;

            // Set the range for the slider.
            if(config.masterVersion < 4)
            {
                // 8-bit load cell thresholds
                slider.Minimum = 20;
                slider.Maximum = 200;
            } else {
                // 16-bit FSR thresholds
                slider.Minimum = 5;
                slider.Maximum = 1023;
            }

            int lower, upper;
            GetValueFromConfig(config, out lower, out upper);

            // Firmware versions before 4 allowed 0xFF to be used to disable a threshold.
            // This isn't used in newer firmwares.
            if(config.masterVersion < 4 && lower == 0xFF)
            {
                LowerLabel.Content = "Off";
                UpperLabel.Content = "";
            }
            else
            {
                slider.LowerValue = lower;
                slider.UpperValue = upper;
                LowerLabel.Content = lower.ToString();
                UpperLabel.Content = upper.ToString();
            }

            RefreshVisibility();
            UpdatingUI = false;
        }

        void RefreshVisibility()
        {
            SMX.SMXConfig config = ActivePad.GetFirstActivePadConfig();
            this.Visibility = ShouldBeDisplayed(config)? Visibility.Visible:Visibility.Collapsed;
        }

        // Return true if this slider should be displayed.  Only display a slider if it affects
        // at least one panel which is enabled.
        private bool ShouldBeDisplayed(SMX.SMXConfig config)
        {
            bool[] enabledPanels = config.GetEnabledPanels();

            // Up and center are shown in both modes.
            switch(Type)
            {
            case "up-left":    return  AdvancedModeEnabled && enabledPanels[0];
            case "up":         return                         enabledPanels[1];
            case "up-right":   return  AdvancedModeEnabled && enabledPanels[2];
            case "left":       return  AdvancedModeEnabled && enabledPanels[3];
            case "center":     return                         enabledPanels[4];
            case "right":      return  AdvancedModeEnabled && enabledPanels[5];
            case "down-left":  return  AdvancedModeEnabled && enabledPanels[6];
            case "down":       return  AdvancedModeEnabled && enabledPanels[7];
            case "down-right": return  AdvancedModeEnabled && enabledPanels[8];

            // Show cardinal and corner if at least one panel they affect is enabled.
            case "cardinal":   return !AdvancedModeEnabled && (enabledPanels[3] || enabledPanels[5] || enabledPanels[8]);
            case "corner":     return !AdvancedModeEnabled && (enabledPanels[0] || enabledPanels[2] || enabledPanels[6] || enabledPanels[8]);
            default:           return true;
            }
        }
    }
    
    // A button with a selectable highlight.
    public class SelectableButton: Button
    {
        public static readonly DependencyProperty SelectedProperty = DependencyProperty.Register("Selected",
            typeof(bool), typeof(SelectableButton), new FrameworkPropertyMetadata(false));
        public bool Selected {
            get { return (bool) GetValue(SelectedProperty); }
            set { SetValue(SelectedProperty, value); }
        }
    }

    // A button that selects a preset, and shows a checkmark if that preset is set.
    public class PresetButton: Control
    {
        public static readonly DependencyProperty TypeProperty = DependencyProperty.Register("Type",
            typeof(string), typeof(PresetButton), new FrameworkPropertyMetadata(""));
        public string Type {
            get { return (string) GetValue(TypeProperty); }
            set { SetValue(TypeProperty, value); }
        }

        public static readonly DependencyProperty SelectedProperty = DependencyProperty.Register("Selected",
            typeof(bool), typeof(PresetButton), new FrameworkPropertyMetadata(true));
        public bool Selected {
            get { return (bool) GetValue(SelectedProperty); }
            set { SetValue(SelectedProperty, value); }
        }

        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register("Label",
            typeof(string), typeof(PresetButton), new FrameworkPropertyMetadata(""));
        public string Label {
            get { return (string) GetValue(LabelProperty); }
            set { SetValue(LabelProperty, value); }
        }

        private OnConfigChange onConfigChange;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            Button button = GetTemplateChild("PART_Button") as Button;
            button.Click += delegate(object sender, RoutedEventArgs e) { Select(); };

            onConfigChange = new OnConfigChange(this, delegate (LoadFromConfigDelegateArgs args) {
                foreach(Tuple<int,SMX.SMXConfig> activePad in ActivePad.ActivePads())
                {
                    SMX.SMXConfig config = activePad.Item2;
                    string CurrentPreset = ConfigPresets.GetPreset(config);
                    Selected = CurrentPreset == Type;
                    break;
                }
            });
        }

        private void Select()
        {
            foreach(Tuple<int,SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                int pad = activePad.Item1;
                SMX.SMXConfig config = activePad.Item2;

                ConfigPresets.SetPreset(Type, ref config);
                Console.WriteLine("PresetButton::Select (" + Type + "): " +
                    config.panelThreshold1Low + ", " + config.panelThreshold4Low + ", " + config.panelThreshold7Low + ", " + config.panelThreshold2Low);
                SMX.SMX.SetConfig(pad, config);
            }
            CurrentSMXDevice.singleton.FireConfigurationChanged(this);
        }
    }

    public class PresetWidget: Control
    {
        public static readonly DependencyProperty TypeProperty = DependencyProperty.Register("Type",
            typeof(string), typeof(PresetWidget), new FrameworkPropertyMetadata(""));
        public string Type {
            get { return (string) GetValue(TypeProperty); }
            set { SetValue(TypeProperty, value); }
        }

        public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register("Description",
            typeof(string), typeof(PresetWidget), new FrameworkPropertyMetadata(""));
        public string Description {
            get { return (string) GetValue(DescriptionProperty); }
            set { SetValue(DescriptionProperty, value); }
        }

        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register("Label",
            typeof(string), typeof(PresetWidget), new FrameworkPropertyMetadata(""));
        public string Label {
            get { return (string) GetValue(LabelProperty); }
            set { SetValue(LabelProperty, value); }
        }
    }

    public class PanelButton: ToggleButton
    {
        public static readonly DependencyProperty ButtonProperty = DependencyProperty.RegisterAttached("Button",
            typeof(string), typeof(PanelButton), new FrameworkPropertyMetadata(null));

        public string Button {
            get { return (string) this.GetValue(ButtonProperty); }
            set { this.SetValue(ButtonProperty, value); }
        }

        protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsPressedChanged(e);
        }
    }

    // A base class for buttons used to select a panel to work with.
    public class PanelSelectButton: Button
    {
        // Which panel this is (P1 0-8, P2 9-17):
        public static readonly DependencyProperty PanelProperty = DependencyProperty.RegisterAttached("Panel",
            typeof(int), typeof(PanelSelectButton), new FrameworkPropertyMetadata(0, RefreshIsSelectedCallback));

        public int Panel {
            get { return (int) this.GetValue(PanelProperty); }
            set { this.SetValue(PanelProperty, value); }
        }

        // Which panel is currently selected.  If this == Panel, this panel is selected.  This is
        // bound to ColorPicker.SelectedPanel, so changing this changes which panel the picker edits.
        public static readonly DependencyProperty SelectedPanelProperty = DependencyProperty.RegisterAttached("SelectedPanel",
            typeof(int), typeof(PanelSelectButton), new FrameworkPropertyMetadata(0, RefreshIsSelectedCallback));

        public int SelectedPanel {
            get { return (int) this.GetValue(SelectedPanelProperty); }
            set { this.SetValue(SelectedPanelProperty, value); }
        }

        // Whether this panel is selected.  This is true if Panel == SelectedPanel.
        public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.RegisterAttached("IsSelected",
            typeof(bool), typeof(PanelSelectButton), new FrameworkPropertyMetadata(false));

        public bool IsSelected {
            get { return (bool) this.GetValue(IsSelectedProperty); }
            set { this.SetValue(IsSelectedProperty, value); }
        }

        // When Panel or SelectedPanel change, update IsSelected.
        private static void RefreshIsSelectedCallback(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            PanelSelectButton self = target as PanelSelectButton;
            self.RefreshIsSelected();
        }

        private void RefreshIsSelected()
        {
            IsSelected = Panel == SelectedPanel;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            RefreshIsSelected();
        }
    }

    // This button shows the color configured for that panel, and chooses which color is being
    // edited by the ColorPicker.
    public class PanelColorButton: PanelSelectButton
    {
        // The color configured for this panel:
        public static readonly DependencyProperty PanelColorProperty = DependencyProperty.RegisterAttached("PanelColor",
            typeof(SolidColorBrush), typeof(PanelColorButton), new FrameworkPropertyMetadata(new SolidColorBrush()));

        public SolidColorBrush PanelColor {
            get { return (SolidColorBrush) this.GetValue(PanelColorProperty); }
            set { this.SetValue(PanelColorProperty, value); }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            OnConfigChange onConfigChange;
            onConfigChange = new OnConfigChange(this, delegate (LoadFromConfigDelegateArgs args) {
                int pad = Panel < 9? 0:1;
                LoadUIFromConfig(args.controller[pad].config);
            });
        }

        protected override void OnClick()
        {
            base.OnClick();

            // Select this panel.
            SelectedPanel = Panel;

            // Fire configuration changed, so the color slider updates to show this panel.
            CurrentSMXDevice.singleton.FireConfigurationChanged(this);
        }

        // Set PanelColor.  This widget doesn't change the color, it only reflects the current configuration.
        private void LoadUIFromConfig(SMX.SMXConfig config)
        {
            int PanelIndex = Panel % 9;

            // Hide color buttons for disabled panels.
            bool[] enabledPanels = config.GetEnabledPanels();
            Visibility = enabledPanels[PanelIndex]? Visibility.Visible:Visibility.Hidden;

            // If this panel is selected but its panel isn't enabled, try to select a
            // different panel.
            if(!enabledPanels[PanelIndex] && IsSelected)
            {
                for(int panel = 0; panel < 9; ++panel)
                {
                    if(enabledPanels[panel])
                    {
                        SelectedPanel = panel;
                        break;
                    }
                }
            }

            Color rgb = Helpers.UnscaleColor(Color.FromRgb(
                config.stepColor[PanelIndex*3+0],
                config.stepColor[PanelIndex*3+1],
                config.stepColor[PanelIndex*3+2]));
            PanelColor = new SolidColorBrush(rgb);
        }

        Point MouseDownPosition;

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            MouseDownPosition = e.GetPosition(null);
            base.OnMouseDown(e);
        }

        // Handle initiating drag.
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if(e.LeftButton == MouseButtonState.Pressed)
            {
                Point position = e.GetPosition(null);

                // Why do we have to handle drag thresholding manually?  This is the platform's job.
                // If we don't do this, clicks won't work at all.
                if (Math.Abs(position.X - MouseDownPosition.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - MouseDownPosition.Y) >= SystemParameters.MinimumVerticalDragDistance)
                {
                    DragDrop.DoDragDrop(this, Helpers.ColorToString(PanelColor.Color), DragDropEffects.Copy);
                }
            }

            base.OnMouseMove(e);
        }

        private bool HandleDrop(DragEventArgs e)
        {
            PanelColorButton Button = e.Source as PanelColorButton;
            if(Button == null)
                return false;

            // A color is being dropped from another button.  Don't just update our color, since
            // that will just change the button color and not actually apply it.
            DataObject data = e.Data as DataObject;
            if(data == null)
                return false;

            // Parse the color being dragged onto us.
            Color color = Helpers.ParseColorString(data.GetData(typeof(string)) as string);
            
            // Update the panel color.
            int PanelIndex = Panel % 9;
            int Pad = Panel < 9? 0:1;
            SMX.SMXConfig config;
            if(!SMX.SMX.GetConfig(Pad, out config))
                return false;

            // Light colors are 8-bit values, but we only use values between 0-170.  Higher values
            // don't make the panel noticeably brighter, and just draw more power.
            color = Helpers.ScaleColor(color);
            config.stepColor[PanelIndex*3+0] = color.R;
            config.stepColor[PanelIndex*3+1] = color.G;
            config.stepColor[PanelIndex*3+2] = color.B;

            SMX.SMX.SetConfig(Pad, config);
            CurrentSMXDevice.singleton.FireConfigurationChanged(this);
            return true;
        }

        protected override void OnDrop(DragEventArgs e)
        {
            if(!HandleDrop(e))
                base.OnDrop(e);
        }
    }

    // This is a Slider class with some added helpers.
    public class Slider2: Slider
    {
        public delegate void DragEvent();
        public event DragEvent StartedDragging, StoppedDragging;

        protected Thumb Thumb;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            Track track = Template.FindName("PART_Track", this) as Track;
            Thumb = track.Thumb;
        }

        // How are there no events for this?
        protected override void OnThumbDragStarted(DragStartedEventArgs e)
        {
            base.OnThumbDragStarted(e);
            StartedDragging?.Invoke();
        }

        protected override void OnThumbDragCompleted(DragCompletedEventArgs e)
        {
            base.OnThumbDragCompleted(e);
            StoppedDragging?.Invoke();
        }

        public Slider2()
        {
            // Fix the slider not dragging after clicking outside the thumb.
            // http://stackoverflow.com/a/30575638/136829
            bool clickedInSlider = false;
            MouseMove += delegate(object sender, MouseEventArgs args)
            {
                if(args.LeftButton == MouseButtonState.Released || !clickedInSlider || Thumb.IsDragging)
                    return;

                Thumb.RaiseEvent(new MouseButtonEventArgs(args.MouseDevice, args.Timestamp, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                    Source = args.Source,
                });
            };

            AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new RoutedEventHandler((sender, args) =>
            {
                clickedInSlider = true;
            }), true);

            AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new RoutedEventHandler((sender, args) =>
            {
                clickedInSlider = false;
            }), true);
        }
    };

    // This is the Slider inside a ColorPicker.
    public class ColorPickerSlider: Slider2
    {
        public ColorPickerSlider()
        {
        }
    };

    public class ColorPicker: Control
    {
        // Which panel is currently selected:
        public static readonly DependencyProperty SelectedPanelProperty = DependencyProperty.Register("SelectedPanel",
            typeof(int), typeof(ColorPicker), new FrameworkPropertyMetadata(0));

        public int SelectedPanel {
            get { return (int) this.GetValue(SelectedPanelProperty); }
            set { this.SetValue(SelectedPanelProperty, value); }
        }

        ColorPickerSlider HueSlider;
        public delegate void Event();

        public event Event StartedDragging, StoppedDragging;
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            HueSlider = GetTemplateChild("HueSlider") as ColorPickerSlider;
            HueSlider.ValueChanged += delegate(object sender, RoutedPropertyChangedEventArgs<double> e) {
                SaveToConfig();
            };

            HueSlider.StartedDragging += delegate() { StartedDragging?.Invoke(); };
            HueSlider.StoppedDragging += delegate() { StoppedDragging?.Invoke(); };

            DoubleCollection ticks = new DoubleCollection();
            // Add a tick at the minimum value, which is a negative value.  This is the
            // tick for white.
            ticks.Add(HueSlider.Minimum);

            // Add a tick for 0-359.  Don't add 360, since that's the same as 0.
            for(int i = 0; i < 360; ++i)
                ticks.Add(i);
            HueSlider.Ticks = ticks;

            OnConfigChange onConfigChange;
            onConfigChange = new OnConfigChange(this, delegate (LoadFromConfigDelegateArgs args) {
                int pad = SelectedPanel < 9? 0:1;
                LoadUIFromConfig(args.controller[pad].config);
            });
        }

        private void SaveToConfig()
        {
            if(UpdatingUI)
                return;

            // Apply the change and save it to the device.
            int pad = SelectedPanel < 9? 0:1;
            SMX.SMXConfig config;
            if(!SMX.SMX.GetConfig(pad, out config))
                    return;

            Color color = Helpers.FromHSV(HueSlider.Value, 1, 1);

            // If we're set to the minimum value, use white instead.
            if(HueSlider.Value == HueSlider.Minimum)
                color = Color.FromRgb(255,255,255);

            // Light colors are 8-bit values, but we only use values between 0-170.  Higher values
            // don't make the panel noticeably brighter, and just draw more power.
            int PanelIndex = SelectedPanel % 9;
            config.stepColor[PanelIndex*3+0] = Helpers.ScaleColor(color.R);
            config.stepColor[PanelIndex*3+1] = Helpers.ScaleColor(color.G);
            config.stepColor[PanelIndex*3+2] = Helpers.ScaleColor(color.B);

            SMX.SMX.SetConfig(pad, config);
            CurrentSMXDevice.singleton.FireConfigurationChanged(this);
        }

        bool UpdatingUI = false;
        private void LoadUIFromConfig(SMX.SMXConfig config)
        {
            // Make sure SaveToConfig doesn't treat these as the user changing values.
            UpdatingUI = true;

            // Reverse the scaling we applied in SaveToConfig.
            int PanelIndex = SelectedPanel % 9;
            Color rgb = Helpers.UnscaleColor(Color.FromRgb(
                config.stepColor[PanelIndex*3+0],
                config.stepColor[PanelIndex*3+1],
                config.stepColor[PanelIndex*3+2]));
            double h, s, v;
            Helpers.ToHSV(rgb, out h, out s, out v);

            // Check for white.  Since the conversion through LightsScaleFactor may not round trip
            // back to exactly #FFFFFF, give some room for error in the value (brightness).
            if(s <= 0.001 && v >= .90)
            {
                // This is white, so set it to the white block at the left edge of the slider.
                HueSlider.Value = HueSlider.Minimum;
            }
            else
            {
                HueSlider.Value = h;
            }

            UpdatingUI = false;
        }
    };

    // This widget selects which panels are enabled.  We only show one of these for both pads.
    class PanelSelector: Control
    {
        PanelButton[] EnabledPanelButtons;
        OnConfigChange onConfigChange;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            int[] PanelToIndex = new int[] {
                7, 8, 9,
                4, 5, 6,
                1, 2, 3,
            };

            EnabledPanelButtons = new PanelButton[9];
            for(int i = 0; i < 9; ++i)
                EnabledPanelButtons[i] = GetTemplateChild("EnablePanel" + PanelToIndex[i]) as PanelButton;

            foreach(PanelButton button in EnabledPanelButtons)
                button.Click += EnabledPanelButtonClicked;

            onConfigChange = new OnConfigChange(this, delegate (LoadFromConfigDelegateArgs args) {
                LoadUIFromConfig(ActivePad.GetFirstActivePadConfig(args));
            });
        }

        private void LoadUIFromConfig(SMX.SMXConfig config)
        {
            // The firmware configuration allows disabling each of the four sensors in a panel
            // individually, but currently we only have a UI for toggling the whole sensor.  Taking
            // individual sensors out isn't recommended.
            bool[] enabledPanels = {
                (config.enabledSensors[0] & 0xF0) != 0,
                (config.enabledSensors[0] & 0x0F) != 0,
                (config.enabledSensors[1] & 0xF0) != 0,
                (config.enabledSensors[1] & 0x0F) != 0,
                (config.enabledSensors[2] & 0xF0) != 0,
                (config.enabledSensors[2] & 0x0F) != 0,
                (config.enabledSensors[3] & 0xF0) != 0,
                (config.enabledSensors[3] & 0x0F) != 0,
                (config.enabledSensors[4] & 0xF0) != 0,
            };
            
            for(int i = 0; i < 9; ++i)
                EnabledPanelButtons[i].IsChecked = enabledPanels[i];
        }

        private int GetIndexFromButton(object sender)
        {
            for(int i = 0; i < 9; i++)
            {
                if(sender == EnabledPanelButtons[i])
                    return i;
            }

            return 0;
        }

        private void EnabledPanelButtonClicked(object sender, EventArgs e)
        {
            // One of the panel buttons on the panel toggle UI was clicked.  Toggle the
            // panel.
            int button = GetIndexFromButton(sender);
            Console.WriteLine("Clicked " + button);

            // Set the enabled sensor mask on both pads to the state of the UI.
            foreach(Tuple<int,SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                int pad = activePad.Item1;
                SMX.SMXConfig config = activePad.Item2;

                // This could be done algorithmically, but this is clearer.
                int[] PanelButtonToSensorIndex = {
                    0, 0, 1, 1, 2, 2, 3, 3, 4
                };
                byte[] PanelButtonToSensorMask = {
                    0xF0, 0x0F,
                    0xF0, 0x0F,
                    0xF0, 0x0F,
                    0xF0, 0x0F,
                    0xF0,
                };
                for(int i = 0; i < 5; ++i)
                    config.enabledSensors[i] = 0;

                for(int Panel = 0; Panel < 9; ++Panel)
                {
                    int index = PanelButtonToSensorIndex[Panel];
                    byte mask = PanelButtonToSensorMask[Panel];
                    if(EnabledPanelButtons[Panel].IsChecked == true)
                        config.enabledSensors[index] |= (byte) mask;
                }

                SMX.SMX.SetConfig(pad, config);
            }
        }
    };

    public class FrameImage: Image
    {
        // The source image.  Changing this after load isn't supported.
        public static readonly DependencyProperty ImageProperty = DependencyProperty.Register("Image",
            typeof(BitmapSource), typeof(FrameImage), new FrameworkPropertyMetadata(null, ImageChangedCallback));

        public BitmapSource Image {
            get { return (BitmapSource) this.GetValue(ImageProperty); }
            set { this.SetValue(ImageProperty, value); }
        }

        // Which frame is currently displayed:
        public static readonly DependencyProperty FrameProperty = DependencyProperty.Register("Frame",
            typeof(int), typeof(FrameImage), new FrameworkPropertyMetadata(0, FrameChangedCallback));

        public int Frame {
            get { return (int) this.GetValue(FrameProperty); }
            set { this.SetValue(FrameProperty, value); }
        }

        public static readonly DependencyProperty FramesXProperty = DependencyProperty.Register("FramesX",
            typeof(int), typeof(FrameImage), new FrameworkPropertyMetadata(0, ImageChangedCallback));

        public int FramesX {
            get { return (int) this.GetValue(FramesXProperty); }
            set { this.SetValue(FramesXProperty, value); }
        }

        private static void ImageChangedCallback(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            FrameImage self = target as FrameImage;
            self.Load();
        }

        private static void FrameChangedCallback(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            FrameImage self = target as FrameImage;
            self.Refresh();
        }

        private BitmapSource[] ImageFrames;

        private void Load()
        {
            if(Image == null || FramesX == 0)
            {
                ImageFrames = null;
                return;
            }

            // Split the image into frames.
            int FrameWidth = Image.PixelWidth / FramesX;
            int FrameHeight = Image.PixelHeight;
            ImageFrames = new BitmapSource[FramesX];
            for(int i = 0; i < FramesX; ++i)
                ImageFrames[i] = new CroppedBitmap(Image, new Int32Rect(FrameWidth*i, 0, FrameWidth, FrameHeight));

            Refresh();
        }

        private void Refresh()
        {
            if(ImageFrames == null || Frame >= ImageFrames.Length)
            {
                this.Source = null;
                return;
            }

            this.Source = ImageFrames[Frame];
        }
    };
}
