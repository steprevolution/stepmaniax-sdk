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
using System.Collections.Generic;

namespace smx_config
{
    // The checkbox to enable and disable the advanced per-panel sliders (Settings.Default.AdvancedMode).
    public class AdvancedThresholdViewCheckbox: CheckBox
    {
        public static readonly DependencyProperty AdvancedModeEnabledProperty = DependencyProperty.Register("AdvancedModeEnabled",
            typeof(bool), typeof(AdvancedThresholdViewCheckbox), new FrameworkPropertyMetadata(false));
        public bool AdvancedModeEnabled {
            get { return (bool) GetValue(AdvancedModeEnabledProperty); }
            set { SetValue(AdvancedModeEnabledProperty, value); }
        }

        OnConfigChange onConfigChange;

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

            // If the controller doesn't support advanced mode, make sure advanced mode is disabled.
            if(!SupportsAdvancedMode)
                Properties.Settings.Default.AdvancedMode = false;

            AdvancedModeEnabled = Properties.Settings.Default.AdvancedMode;
        }

        protected override void OnClick()
        {
            Properties.Settings.Default.AdvancedMode = !Properties.Settings.Default.AdvancedMode;
            if(!Properties.Settings.Default.AdvancedMode)
            {
                // Sync thresholds when we exit advanced mode.  XXX: not needed since MainWindow is recreating
                // sliders anyway
                ThresholdSettings.SyncSliderThresholds();
            }

            // Refresh the UI.
            LoadUIFromConfig(ActivePad.GetFirstActivePadConfig());
        }
    }

    // This implements the threshold slider widget for changing an upper/lower threshold pair.
    public class ThresholdSlider: Control
    {
        public static readonly DependencyProperty TypeProperty = DependencyProperty.Register("Type",
            typeof(string), typeof(ThresholdSlider), new FrameworkPropertyMetadata(""));

        public string Type {
            get { return (string) GetValue(TypeProperty); }
            set { SetValue(TypeProperty, value); }
        }

        // If false, this threshold hasn't been enabled by the user.  The slider will be greyed out.  This
        // is different from our own IsEnabled, since setting that to false would also disable EnabledCheckbox,
        // preventing it from being turned back on.
        public static readonly DependencyProperty ThresholdEnabledProperty = DependencyProperty.Register("ThresholdEnabled",
            typeof(bool), typeof(ThresholdSlider), new FrameworkPropertyMetadata(true));

        public bool ThresholdEnabled {
            get { return (bool) GetValue(ThresholdEnabledProperty); }
            set { SetValue(ThresholdEnabledProperty, value); }
        }

        // This is set to true if the slider is enabled and the low/high values are displayed.  We set this to
        // false when the slider is disabled (or has no selected sensors, for custom-sliders).
        public static readonly DependencyProperty SliderActiveProperty = DependencyProperty.Register("SliderActive",
            typeof(bool), typeof(ThresholdSlider), new FrameworkPropertyMetadata(true));

        public bool SliderActive {
            get { return (bool) GetValue(SliderActiveProperty); }
            set { SetValue(SliderActiveProperty, value); }
        }

        public static readonly DependencyProperty AdvancedModeEnabledProperty = DependencyProperty.Register("AdvancedModeEnabled",
            typeof(bool), typeof(ThresholdSlider), new FrameworkPropertyMetadata(false));

        public bool AdvancedModeEnabled {
            get { return (bool) GetValue(AdvancedModeEnabledProperty); }
            set { SetValue(AdvancedModeEnabledProperty, value); }
        }

        DoubleSlider slider;
        Label LowerLabel, UpperLabel;
        Image ThresholdWarning;
        PlatformSensorDisplay SensorDisplay;

        OnConfigChange onConfigChange;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            slider = GetTemplateChild("Slider") as DoubleSlider;
            LowerLabel = GetTemplateChild("LowerValue") as Label;
            UpperLabel = GetTemplateChild("UpperValue") as Label;
            ThresholdWarning = GetTemplateChild("ThresholdWarning") as Image;
            SensorDisplay = GetTemplateChild("PlatformSensorDisplay") as PlatformSensorDisplay;

            slider.ValueChanged += delegate(DoubleSlider slider) { SaveToConfig(); };

            // Show the edit button for the custom-sensors slider.
            Button EditCustomSensorsButton = GetTemplateChild("EditCustomSensorsButton") as Button;
            EditCustomSensorsButton.Visibility = Type == "custom-sensors"? Visibility.Visible:Visibility.Hidden;
            EditCustomSensorsButton.Click += delegate(object sender, RoutedEventArgs e)
            {
                SetCustomSensors dialog = new SetCustomSensors();
                dialog.Owner = Window.GetWindow(this);
                dialog.ShowDialog();
            };

            onConfigChange = new OnConfigChange(this, delegate (LoadFromConfigDelegateArgs args) {
                LoadUIFromConfig(ActivePad.GetFirstActivePadConfig(args));
            });
        }

        private void RefreshSliderActiveProperty()
        {
            if(Type == "custom-sensors")
                SliderActive = ThresholdSettings.GetCustomSensors().Count > 0;
            else
                SliderActive = ThresholdEnabled;
        }

        // Return the panel/sensors this widget controls.
        //
        // This returns values for FSRs.  We don't configure individual sensors with load cells,
        // and the sensor value will be ignored.
        private List<ThresholdSettings.PanelAndSensor> GetControlledSensors(bool includeOverridden)
        {
            return ThresholdSettings.GetControlledSensorsForSliderType(Type, AdvancedModeEnabled, includeOverridden);
        }


        private void SetValueToConfig(ref SMX.SMXConfig config)
        {
            List<ThresholdSettings.PanelAndSensor> panelAndSensors = GetControlledSensors(false);
            foreach(ThresholdSettings.PanelAndSensor panelAndSensor in panelAndSensors)
            {
                if(!config.fsr())
                {
                    byte lower = (byte) slider.LowerValue;
                    byte upper = (byte) slider.UpperValue;
                    config.panelSettings[panelAndSensor.panel].loadCellLowThreshold = lower;
                    config.panelSettings[panelAndSensor.panel].loadCellHighThreshold = upper;
                } else {
                    byte lower = (byte) slider.LowerValue;
                    byte upper = (byte) slider.UpperValue;
                    config.panelSettings[panelAndSensor.panel].fsrLowThreshold[panelAndSensor.sensor] = lower;
                    config.panelSettings[panelAndSensor.panel].fsrHighThreshold[panelAndSensor.sensor] = upper;
                }
            }
        }

        private void GetValueFromConfig(SMX.SMXConfig config, out int lower, out int upper)
        {
            lower = upper = 0;

            // Use the first controlled sensor.  The rest should be the same.
            foreach(ThresholdSettings.PanelAndSensor panelAndSensor in GetControlledSensors(false))
            {
                if(!config.fsr())
                {
                    lower = config.panelSettings[panelAndSensor.panel].loadCellLowThreshold;
                    upper = config.panelSettings[panelAndSensor.panel].loadCellHighThreshold;
                } else {
                    lower = config.panelSettings[panelAndSensor.panel].fsrLowThreshold[panelAndSensor.sensor];
                    upper = config.panelSettings[panelAndSensor.panel].fsrHighThreshold[panelAndSensor.sensor];
                }
                return;
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

            RefreshSliderActiveProperty();

            // Set the range for the slider.
            if(config.fsr())
            {
                // 16-bit FSR thresholds.
                slider.Minimum = 5;
                slider.Maximum = 250;
                slider.MinimumDistance = 5;
            } else {
                // 8-bit load cell thresholds
                slider.Minimum = 20;
                slider.Maximum = 200;
                slider.MinimumDistance = 10;
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

            List<ThresholdSettings.PanelAndSensor> controlledSensors = GetControlledSensors(false);
            bool ShowThresholdWarning = false;
            foreach(ThresholdSettings.PanelAndSensor panelAndSensor in controlledSensors)
            {
                if(config.ShowThresholdWarning(panelAndSensor.panel, panelAndSensor.sensor))
                    ShowThresholdWarning = true;
            }

            ThresholdWarning.Visibility = ShowThresholdWarning? Visibility.Visible:Visibility.Hidden;

            // SensorDisplay shows which sensors we control.  If this sensor is enabled, show the
            // sensors this sensor controls.
            // 
            // If we're disabled, the icon will be empty.  That looks
            // weird, so in that case we show 
            // Set the icon next to the slider to show which sensors we control.
            List<ThresholdSettings.PanelAndSensor> defaultControlledSensors = GetControlledSensors(true);
            SensorDisplay.SetFromPanelAndSensors(controlledSensors, defaultControlledSensors);

            UpdatingUI = false;
        }
    }

    // The checkbox next to the threshold slider to turn it on or off.  This is only used
    // for inner-sensors and outer-sensors, and hides itself automatically for others.
    public class ThresholdEnabledButton: CheckBox
    {
        // Which threshold slider this is for.  This is bound to ThresholdSlider.Type above.
        public static readonly DependencyProperty TypeProperty = DependencyProperty.Register("Type",
            typeof(string), typeof(ThresholdEnabledButton), new FrameworkPropertyMetadata(""));
        public string Type {
            get { return (string) GetValue(TypeProperty); }
            set { SetValue(TypeProperty, value); }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            if(Type != "inner-sensors" && Type != "outer-sensors")
            {
                Visibility = Visibility.Hidden;
                IsChecked = true;
                return;
            }

            Checked += delegate(object sender, RoutedEventArgs e) { SaveToSettings(); };
            Unchecked += delegate(object sender, RoutedEventArgs e) { SaveToSettings(); };

            OnConfigChange onConfigChange;
            onConfigChange = new OnConfigChange(this, delegate(LoadFromConfigDelegateArgs args) {
                LoadFromSettings();
            });
        }

        private void LoadFromSettings()
        {
            if(Type == "inner-sensors")
                IsChecked = Properties.Settings.Default.UseInnerSensorThresholds;
            else if(Type == "outer-sensors")
                IsChecked = Properties.Settings.Default.UseOuterSensorThresholds;
        }

        private void SaveToSettings()
        {
            if(Type == "inner-sensors")
                Properties.Settings.Default.UseInnerSensorThresholds = (bool) IsChecked;
            else if(Type == "outer-sensors")
                Properties.Settings.Default.UseOuterSensorThresholds = (bool) IsChecked;

            Properties.Settings.Default.Save();

            // Sync thresholds after enabling or disabling a slider.
            ThresholdSettings.SyncSliderThresholds();

            CurrentSMXDevice.singleton.FireConfigurationChanged(this);
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
        // Whether this button is selected.
        public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.RegisterAttached("IsSelected",
            typeof(bool), typeof(PanelSelectButton), new FrameworkPropertyMetadata(false));

        public bool IsSelected {
            get { return (bool) this.GetValue(IsSelectedProperty); }
            set { this.SetValue(IsSelectedProperty, value); }
        }
    }

    // A button that selects which color is being set.
    public abstract class ColorButton: PanelSelectButton
    {
        // The color configured for this panel:
        public static readonly DependencyProperty PanelColorProperty = DependencyProperty.RegisterAttached("PanelColor",
            typeof(SolidColorBrush), typeof(ColorButton), new FrameworkPropertyMetadata(new SolidColorBrush()));

        public SolidColorBrush PanelColor {
            get { return (SolidColorBrush) this.GetValue(PanelColorProperty); }
            set { this.SetValue(PanelColorProperty, value); }
        }

        // Return 0 if this is for the P1 pad, or 1 if it's for P2.
        protected abstract int getPadNo();

        // Return true if this panel is enabled and should be selectable.
        public abstract bool isEnabled(LoadFromConfigDelegateArgs args);

        // Get and set our color to the pad configuration.
        abstract public Color getColor();
        abstract public void setColor(Color color);

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            OnConfigChange onConfigChange;
            onConfigChange = new OnConfigChange(this, delegate (LoadFromConfigDelegateArgs args) {
                LoadUIFromConfig(args);
            });
        }

        // Set PanelColor.  This widget doesn't change the color, it only reflects the current configuration.
        private void LoadUIFromConfig(LoadFromConfigDelegateArgs args)
        {
            SMX.SMXConfig config = args.controller[getPadNo()].config;

            // Hide disabled color buttons.
            Visibility = isEnabled(args)? Visibility.Visible:Visibility.Hidden;

            Color rgb = getColor();
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

            // Parse the color being dragged onto us, and set it.
            Color color = Helpers.ParseColorString(data.GetData(typeof(string)) as string);
            setColor(color);

            return true;
        }

        protected override void OnDrop(DragEventArgs e)
        {
            if(!HandleDrop(e))
                base.OnDrop(e);
        }
    }

    // A ColorButton for setting a panel color.
    public class PanelColorButton: ColorButton
    {
        // Which panel this is (P1 0-8, P2 9-17):
        public static readonly DependencyProperty PanelProperty = DependencyProperty.RegisterAttached("Panel",
            typeof(int), typeof(PanelColorButton), new FrameworkPropertyMetadata(0));

        public int Panel {
            get { return (int) this.GetValue(PanelProperty); }
            set { this.SetValue(PanelProperty, value); }
        }
        
        protected override int getPadNo()
        {
            return Panel < 9? 0:1;
        }

        // A panel is enabled if it's enabled in the panel mask, which can be
        // changed on the advanced tab.
        public override bool isEnabled(LoadFromConfigDelegateArgs args)
        {
            int pad = getPadNo();
            SMX.SMXConfig config = args.controller[pad].config;

            if(!args.controller[pad].info.connected)
                return false;

            int PanelIndex = Panel % 9;
            bool[] enabledPanels = config.GetEnabledPanels();
            return enabledPanels[PanelIndex];
        }

        public override void setColor(Color color)
        {
            // Apply the change and save it to the device.
            int pad = getPadNo();
            SMX.SMXConfig config;
            if(!SMX.SMX.GetConfig(pad, out config))
                return;

            // Light colors are 8-bit values, but we only use values between 0-170.  Higher values
            // don't make the panel noticeably brighter, and just draw more power.
            int PanelIndex = Panel % 9;
            config.stepColor[PanelIndex*3+0] = Helpers.ScaleColor(color.R);
            config.stepColor[PanelIndex*3+1] = Helpers.ScaleColor(color.G);
            config.stepColor[PanelIndex*3+2] = Helpers.ScaleColor(color.B);

            SMX.SMX.SetConfig(pad, config);
            CurrentSMXDevice.singleton.FireConfigurationChanged(this);
        }

        // Return the color set for this panel in config.
        public override Color getColor()
        {
            int pad = getPadNo();
            SMX.SMXConfig config;
            if(!SMX.SMX.GetConfig(pad, out config))
                return Color.FromRgb(0,0,0);

            int PanelIndex = Panel % 9;
            return Helpers.UnscaleColor(Color.FromRgb(
                config.stepColor[PanelIndex*3+0],
                config.stepColor[PanelIndex*3+1],
                config.stepColor[PanelIndex*3+2]));
        }
    }

    public class FloorColorButton: ColorButton
    {
        // 0 if this is for P1, 1 for P2.
        public static readonly DependencyProperty PadProperty = DependencyProperty.RegisterAttached("Pad",
            typeof(int), typeof(FloorColorButton), new FrameworkPropertyMetadata(0));

        public int Pad {
            get { return (int) this.GetValue(PadProperty); }
            set { this.SetValue(PadProperty, value); }
        }
        protected override int getPadNo() { return Pad; }

        // The floor color button is available if the firmware is v4 or greater.
        public override bool isEnabled(LoadFromConfigDelegateArgs args)
        {
            int pad = getPadNo();
            SMX.SMXConfig config = args.controller[pad].config;
            return config.masterVersion >= 4;
        }
        
        public override void setColor(Color color)
        {
            // Apply the change and save it to the device.
            int pad = getPadNo();
            SMX.SMXConfig config;
            if(!SMX.SMX.GetConfig(pad, out config))
                    return;

            config.platformStripColor[0] = color.R;
            config.platformStripColor[1] = color.G;
            config.platformStripColor[2] = color.B;

            SMX.SMX.SetConfig(pad, config);
            CurrentSMXDevice.singleton.FireConfigurationChanged(this);
        }

        // Return the color set for this panel in config.
        public override Color getColor()
        {
            int pad = getPadNo();
            SMX.SMXConfig config;
            if(!SMX.SMX.GetConfig(pad, out config))
                return Color.FromRgb(0,0,0);

            return Color.FromRgb(config.platformStripColor[0], config.platformStripColor[1], config.platformStripColor[2]);
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
        ColorPickerSlider HueSlider;
        public delegate void Event();

        // The selected ColorButton.  This handles getting and setting the color to the
        // config.
        ColorButton _colorButton;
        public ColorButton colorButton {
            get { return _colorButton; }
            set {
                _colorButton = value;

                // Refresh on change.
                LoadFromConfigDelegateArgs args = CurrentSMXDevice.singleton.GetState();
                LoadUIFromConfig(args);
            }
        }
        

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
            onConfigChange = new OnConfigChange(this, delegate(LoadFromConfigDelegateArgs args) {
                LoadUIFromConfig(args);
            });
        }

        private void SaveToConfig()
        {
            if(UpdatingUI || _colorButton == null)
                return;

            Color color = Helpers.FromHSV(HueSlider.Value, 1, 1);

            // If we're set to the minimum value, use white instead.
            if(HueSlider.Value == HueSlider.Minimum)
                color = Color.FromRgb(255,255,255);

            _colorButton.setColor(color);
        }

        bool UpdatingUI = false;
        private void LoadUIFromConfig(LoadFromConfigDelegateArgs args)
        {
            if(UpdatingUI || _colorButton == null)
                return;

            // Make sure SaveToConfig doesn't treat these as the user changing values.
            UpdatingUI = true;

            // Reverse the scaling we applied in SaveToConfig.
            Color rgb = _colorButton.getColor();
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

                // If we're not in "light all panels" mode, sync up autoLightPanelMask
                // with the new enabledSensors.
                config.refreshAutoLightPanelMask();

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

    public class LightAllPanelsCheckbox: CheckBox
    {
        public static readonly DependencyProperty LightAllPanelsProperty = DependencyProperty.Register("LightAllPanels",
            typeof(bool), typeof(LightAllPanelsCheckbox), new FrameworkPropertyMetadata(false));
        public bool LightAllPanels {
            get { return (bool) GetValue(LightAllPanelsProperty); }
            set { SetValue(LightAllPanelsProperty, value); }
        }

        OnConfigChange onConfigChange;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            onConfigChange = new OnConfigChange(this, delegate (LoadFromConfigDelegateArgs args) {
                LoadUIFromConfig(ActivePad.GetFirstActivePadConfig(args));
            });
        }

        private void LoadUIFromConfig(SMX.SMXConfig config)
        {
            LightAllPanels = config.getLightAllPanelsMode();
        }

        protected override void OnClick()
        {
            //SMX.SMXConfig firstConfig = ActivePad.GetFirstActivePadConfig();
            foreach(Tuple<int,SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                int pad = activePad.Item1;
                SMX.SMXConfig config = activePad.Item2;
                config.setLightAllPanelsMode(!LightAllPanels);
                SMX.SMX.SetConfig(pad, config);
            }
            CurrentSMXDevice.singleton.FireConfigurationChanged(this);

            // Refresh the UI.
            //LoadUIFromConfig(firstConfig);
        }
    }

    public class EnableCenterTopSensorCheckbox: CheckBox
    {
        public static readonly DependencyProperty EnableSensorProperty = DependencyProperty.Register("EnableSensor",
            typeof(bool), typeof(EnableCenterTopSensorCheckbox), new FrameworkPropertyMetadata(false));
        public bool EnableSensor {
            get { return (bool) GetValue(EnableSensorProperty); }
            set { SetValue(EnableSensorProperty, value); }
        }

        OnConfigChange onConfigChange;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            onConfigChange = new OnConfigChange(this, delegate (LoadFromConfigDelegateArgs args) {
                LoadUIFromConfig(ActivePad.GetFirstActivePadConfig(args));
            });
        }

        private void LoadUIFromConfig(SMX.SMXConfig config)
        {
            // Center panel, top sensor:
            bool enabled = config.panelSettings[4].fsrHighThreshold[2] < 255;
            EnableSensor = enabled;
        }

        protected override void OnClick()
        {
            foreach(Tuple<int,SMX.SMXConfig> activePad in ActivePad.ActivePads())
            {
                int pad = activePad.Item1;
                SMX.SMXConfig config = activePad.Item2;

                // Disable the sensor by setting its high threshold to 255, and enable it by syncing it up
                // with the other thresholds.
                if(!EnableSensor)
                    config.panelSettings[4].fsrHighThreshold[2] = config.panelSettings[4].fsrHighThreshold[0];
                else
                    config.panelSettings[4].fsrHighThreshold[2] = 255;
                SMX.SMX.SetConfig(pad, config);
            }
            CurrentSMXDevice.singleton.FireConfigurationChanged(this);
        }
    }

    public class PanelIconWithSensorsSensor: Control
    {
        // 0: black
        // 1: dim highlight
        // 2: bright highlight
        public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register("Highlight",
            typeof(int), typeof(PanelIconWithSensorsSensor), new FrameworkPropertyMetadata(0));
        public int Highlight {
            get { return (int) GetValue(HighlightProperty); }
            set { SetValue(HighlightProperty, value); }
        }
    }

    // A control with one button for each of four sensors:
    class PanelIconWithSensors: Control
    {
        PanelIconWithSensorsSensor[] panelIconWithSensorsSensor;
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            panelIconWithSensorsSensor = new PanelIconWithSensorsSensor[4];
            for(int sensor = 0; sensor < 4; ++sensor)
                panelIconWithSensorsSensor[sensor] = GetTemplateChild("Sensor" + sensor) as PanelIconWithSensorsSensor;
        }


        public PanelIconWithSensorsSensor GetSensorControl(int sensor)
        {
            return panelIconWithSensorsSensor[sensor];
        }
    }

    public class PlatformSensorDisplay: Control
    {
        PanelIconWithSensors[] panelIconWithSensors;
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            panelIconWithSensors = new PanelIconWithSensors[9];
            for(int panel = 0; panel < 9; ++panel)
                panelIconWithSensors[panel] = GetTemplateChild("Panel" + panel) as PanelIconWithSensors;
        }

        private PanelIconWithSensorsSensor GetSensor(int panel, int sensor)
        {
            return panelIconWithSensors[panel].GetSensorControl(sensor);
        }

        // Highlight the sensors included in panelAndSensors, and dimly highlight the sensors in
        // disabledPanelAndSensors.  If a sensor is in both lists, panelAndSensors takes priority.
        public void SetFromPanelAndSensors(
            List<ThresholdSettings.PanelAndSensor> panelAndSensors,
            List<ThresholdSettings.PanelAndSensor> disabledPanelAndSensors)
        {
            UnhighlightAllSensors();

            foreach(ThresholdSettings.PanelAndSensor panelAndSensor in disabledPanelAndSensors)
                GetSensor(panelAndSensor.panel, panelAndSensor.sensor).Highlight = 1;
            foreach(ThresholdSettings.PanelAndSensor panelAndSensor in panelAndSensors)
                GetSensor(panelAndSensor.panel, panelAndSensor.sensor).Highlight = 2;
        }

        // Clear all sensor highlighting.
        public void UnhighlightAllSensors()
        {
            for(int panel = 0; panel < 9; ++panel)
            {
                for(int sensor = 0; sensor < 4; ++sensor)
                    GetSensor(panel, sensor).Highlight = 0;
            }
        }
    }
}
