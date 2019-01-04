using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Input;

namespace smx_config
{
    public class DiagnosticsPanelButton: PanelSelectButton
    {
        public static readonly DependencyProperty PanelProperty = DependencyProperty.RegisterAttached("Panel",
            typeof(int), typeof(PanelSelectButton), new FrameworkPropertyMetadata(0));

        public int Panel {
            get { return (int) this.GetValue(PanelProperty); }
            set { this.SetValue(PanelProperty, value); }
        }
        // Which panel is currently selected.
        public static readonly DependencyProperty SelectedPanelProperty = DependencyProperty.RegisterAttached("SelectedPanel",
            typeof(int), typeof(PanelSelectButton), new FrameworkPropertyMetadata(0));

        public int SelectedPanel {
            get { return (int) this.GetValue(SelectedPanelProperty); }
            set { this.SetValue(SelectedPanelProperty, value); }
        }

        // True if this panel is being pressed.
        public static readonly DependencyProperty PressedProperty = DependencyProperty.Register("Pressed",
            typeof(bool), typeof(DiagnosticsPanelButton), new FrameworkPropertyMetadata(false));

        public bool Pressed {
            get { return (bool) GetValue(PressedProperty); }
            set { SetValue(PressedProperty, value); }
        }

        // True if a warning icon should be displayed for this panel.
        public static readonly DependencyProperty WarningProperty = DependencyProperty.Register("Warning",
            typeof(bool), typeof(DiagnosticsPanelButton), new FrameworkPropertyMetadata(false));

        public bool Warning {
            get { return (bool) GetValue(WarningProperty); }
            set { SetValue(WarningProperty, value); }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            OnConfigChange onConfigChange;
            onConfigChange = new OnConfigChange(this, delegate (LoadFromConfigDelegateArgs args) {
                int SelectedPad = Panel < 9? 0:1;
                int PanelIndex = Panel % 9;
                Pressed = args.controller[SelectedPad].inputs[PanelIndex];

                Warning = !args.controller[SelectedPad].test_data.bHaveDataFromPanel[PanelIndex] ||
                           args.controller[SelectedPad].test_data.AnySensorsOnPanelNotResponding(PanelIndex);
                        
            });
            onConfigChange.RefreshOnInputChange = true;
            onConfigChange.RefreshOnTestDataChange = true;
        }

        protected override void OnClick()
        {
            base.OnClick();

            // Select this panel.
            Console.WriteLine(SelectedPanel + " -> " + Panel);
            SelectedPanel = Panel;

            CurrentSMXDevice.singleton.FireConfigurationChanged(this);
        }
    }

    public class LevelBar: Control
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value",
            typeof(double), typeof(LevelBar), new FrameworkPropertyMetadata(0.5, ValueChangedCallback));

        public double Value {
            get { return (double) GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public static readonly DependencyProperty ErrorProperty = DependencyProperty.Register("Error",
            typeof(bool), typeof(LevelBar), new FrameworkPropertyMetadata(false, ValueChangedCallback));

        public bool Error {
            get { return (bool) GetValue(ErrorProperty); }
            set { SetValue(ErrorProperty, value); }
        }

        private Rectangle Fill, Back;

        private static void ValueChangedCallback(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            LevelBar self = target as LevelBar;
            self.Refresh();
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            Fill = Template.FindName("Fill", this) as Rectangle;
            Back = Template.FindName("Back", this) as Rectangle;
            Refresh();
        }

        private void Refresh()
        {
            // If Error is true, fill the bar red.
            double FillHeight = Error? 1:Value;
            Fill.Height = Math.Round(Math.Max(FillHeight, 0) * (Back.Height - 2));

            if(Error)
            {
                Fill.Fill = new SolidColorBrush(Color.FromRgb(255,0,0));
            }
            else
            {
                // Scale from green (#FF0000) to yellow (#FFFF00) as we go from 0 to .4.
                double ColorValue = Value / 0.4;
                Byte Yellow = (Byte) (Math.Max(0, Math.Min(255, ColorValue * 255)) );
                Fill.Fill = new SolidColorBrush(Color.FromRgb(255,Yellow,0));
            }
        }
    }

    public class DiagnosticsControl: Control
    {
        // Which panel is currently selected:
        public static readonly DependencyProperty SelectedPanelProperty = DependencyProperty.Register("SelectedPanel",
            typeof(int), typeof(DiagnosticsControl), new FrameworkPropertyMetadata(0));

        public int SelectedPanel {
            get { return (int) this.GetValue(SelectedPanelProperty); }
            set { this.SetValue(SelectedPanelProperty, value); }
        }

        private LevelBar[] LevelBars;
        private Label[] LevelBarText;
        private ComboBox DiagnosticMode;
        private FrameImage CurrentDIP;
        private FrameImage ExpectedDIP;
        private FrameworkElement NoResponseFromPanel;
        private FrameworkElement NoResponseFromSensors;
        private FrameworkElement P1Diagnostics, P2Diagnostics;
        private FrameworkElement DIPLabelLeft, DIPLabelRight;

        public delegate void ShowAllLightsEvent(bool on);
        public event ShowAllLightsEvent SetShowAllLights;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            LevelBars = new LevelBar[4];
            LevelBars[0] = Template.FindName("SensorBar1", this) as LevelBar;
            LevelBars[1] = Template.FindName("SensorBar2", this) as LevelBar;
            LevelBars[2] = Template.FindName("SensorBar3", this) as LevelBar;
            LevelBars[3] = Template.FindName("SensorBar4", this) as LevelBar;

            LevelBarText = new Label[4];
            LevelBarText[0] = Template.FindName("SensorBarLevel1", this) as Label;
            LevelBarText[1] = Template.FindName("SensorBarLevel2", this) as Label;
            LevelBarText[2] = Template.FindName("SensorBarLevel3", this) as Label;
            LevelBarText[3] = Template.FindName("SensorBarLevel4", this) as Label;

            DiagnosticMode = Template.FindName("DiagnosticMode", this) as ComboBox;
            CurrentDIP = Template.FindName("CurrentDIP", this) as FrameImage;
            ExpectedDIP = Template.FindName("ExpectedDIP", this) as FrameImage;
            NoResponseFromPanel = Template.FindName("NoResponseFromPanel", this) as FrameworkElement;
            NoResponseFromSensors = Template.FindName("NoResponseFromSensors", this) as FrameworkElement;
            P1Diagnostics = Template.FindName("P1Diagnostics", this) as FrameworkElement;
            P2Diagnostics = Template.FindName("P2Diagnostics", this) as FrameworkElement;

            // Only show the mode dropdown in debug mode.  In regular use, just show calibrated values.
            DiagnosticMode.Visibility = Helpers.GetDebug()? Visibility.Visible:Visibility.Collapsed;

            DIPLabelRight = Template.FindName("DIPLabelRight", this) as FrameworkElement;
            DIPLabelLeft = Template.FindName("DIPLabelLeft", this) as FrameworkElement;

            Button Recalibrate = Template.FindName("Recalibrate", this) as Button;
            Recalibrate.Click += delegate(object sender, RoutedEventArgs e)
            {
                for(int pad = 0; pad < 2; ++pad)
                    SMX.SMX.ForceRecalibration(pad);
            };
            
            Button LightAll = Template.FindName("LightAll", this) as Button;
            LightAll.PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            {
                SetShowAllLights?.Invoke(true);
            };
            LightAll.PreviewMouseUp += delegate(object sender, MouseButtonEventArgs e)
            {
                SetShowAllLights?.Invoke(false);
            };

            // Update the test mode when the dropdown is changed.
            DiagnosticMode.AddHandler(ComboBox.SelectionChangedEvent, new RoutedEventHandler(delegate(object sender, RoutedEventArgs e)
            {
                for(int pad = 0; pad < 2; ++pad)
                    SMX.SMX.SetTestMode(pad, GetTestMode());
            }));

            OnConfigChange onConfigChange;
            onConfigChange = new OnConfigChange(this, delegate (LoadFromConfigDelegateArgs args) {
                Refresh(args);
            });
            onConfigChange.RefreshOnTestDataChange = true;

            Loaded += delegate(object sender, RoutedEventArgs e)
            {
                for(int pad = 0; pad < 2; ++pad)
                    SMX.SMX.SetTestMode(pad, GetTestMode());
            };

            Unloaded += delegate(object sender, RoutedEventArgs e)
            {
                for(int pad = 0; pad < 2; ++pad)
                    SMX.SMX.SetTestMode(pad, SMX.SMX.SensorTestMode.Off);
            };
        }

        private SMX.SMX.SensorTestMode GetTestMode()
        {
            switch(DiagnosticMode.SelectedIndex)
            {
            case 0: return SMX.SMX.SensorTestMode.CalibratedValues;
            case 1: return SMX.SMX.SensorTestMode.UncalibratedValues;
            case 2: return SMX.SMX.SensorTestMode.Noise;
            case 3:
            default: return SMX.SMX.SensorTestMode.Tare;
            }
        }

        private void Refresh(LoadFromConfigDelegateArgs args)
        {
            P1Diagnostics.Visibility = args.controller[0].info.connected? Visibility.Visible:Visibility.Collapsed;
            P2Diagnostics.Visibility = args.controller[1].info.connected? Visibility.Visible:Visibility.Collapsed;

            // Update the displayed DIP switch icons.
            int SelectedPad = SelectedPanel < 9? 0:1;
            int PanelIndex = SelectedPanel % 9;
            int dip = args.controller[SelectedPad].test_data.iDIPSwitchPerPanel[PanelIndex];
            CurrentDIP.Frame = dip;
            ExpectedDIP.Frame = PanelIndex;

            // Show or hide the sensor error text.
            bool AnySensorsNotResponding = false;
            if(args.controller[SelectedPad].test_data.bHaveDataFromPanel[PanelIndex])
                AnySensorsNotResponding = args.controller[SelectedPad].test_data.AnySensorsOnPanelNotResponding(PanelIndex);
            NoResponseFromSensors.Visibility = AnySensorsNotResponding? Visibility.Visible:Visibility.Collapsed;

            // Adjust the DIP labels to match the PCB.
            SMX.SMXConfig config = ActivePad.GetFirstActivePadConfig(args);
            bool DIPLabelsOnLeft = config.masterVersion < 4;
            DIPLabelRight.Visibility = DIPLabelsOnLeft? Visibility.Collapsed:Visibility.Visible;
            DIPLabelLeft.Visibility = DIPLabelsOnLeft? Visibility.Visible:Visibility.Collapsed;

            // Update the level bar from the test mode data for the selected panel.
            for(int sensor = 0; sensor < 4; ++sensor)
            {
                var controllerData = args.controller[SelectedPad];
                Int16 value = controllerData.test_data.sensorLevel[PanelIndex*4+sensor];

                if(GetTestMode() == SMX.SMX.SensorTestMode.Noise)
                {
                    // In noise mode, we receive standard deviation values squared.  Display the square
                    // root, since the panels don't do this for us.  This makes the numbers different
                    // than the configured value (square it to convert back), but without this we display
                    // a bunch of 4 and 5-digit numbers that are too hard to read.
                    value = (Int16) Math.Sqrt(value);
                }

                LevelBarText[sensor].Visibility = Visibility.Visible;
                if(!args.controller[SelectedPad].test_data.bHaveDataFromPanel[PanelIndex])
                {
                    LevelBars[sensor].Value = 0;
                    LevelBarText[sensor].Visibility = Visibility.Hidden;
                    LevelBarText[sensor].Content = "-";
                    LevelBars[sensor].Error = false;
                }
                else if(args.controller[SelectedPad].test_data.bBadSensorInput[PanelIndex*4+sensor])
                {
                    LevelBars[sensor].Value = 0;
                    LevelBarText[sensor].Content = "!";
                    LevelBars[sensor].Error = true;
                }
                else
                {
                    // Very slightly negative values happen due to noise.  They don't indicate a
                    // problem, but they're confusing in the UI, so clamp them away.
                    if(value < 0 && value >= -10)
                        value = 0;

                    // Scale differently depending on if this is an FSR panel or a load cell panel.
                    bool isFSR = controllerData.config.masterVersion >= 4 && controllerData.test_data.bFSRPerPanel[PanelIndex*4+sensor];
                    float maxValue = isFSR? 1023:500;
                    LevelBars[sensor].Value = value / maxValue;
                    LevelBarText[sensor].Content = value;
                    LevelBars[sensor].Error = false;
                }
            }

            if(!args.controller[SelectedPad].test_data.bHaveDataFromPanel[PanelIndex])
            {
                NoResponseFromPanel.Visibility = Visibility.Visible;
                NoResponseFromSensors.Visibility = Visibility.Collapsed;
                return;
            }

            NoResponseFromPanel.Visibility = Visibility.Collapsed;
        }
    }
}
