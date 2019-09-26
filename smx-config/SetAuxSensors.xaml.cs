using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace smx_config
{
    public class SensorSelectionButton: ToggleButton
    {
    }

    // A control with one button for each of four sensors:
    class SensorSelector: Control
    {
        // The panel we're editing (0-8).
        public static readonly DependencyProperty PanelProperty = DependencyProperty.RegisterAttached("Panel",
            typeof(int), typeof(SensorSelector), new FrameworkPropertyMetadata(0));

        public int Panel {
            get { return (int) this.GetValue(PanelProperty); }
            set { this.SetValue(PanelProperty, value); }
        }

        ToggleButton[] SensorSelectionButtons = new ToggleButton[4];
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            for(int sensor = 0; sensor < 4; ++sensor)
            {
                int ThisSensor = sensor; // bind
                SensorSelectionButtons[sensor] = GetTemplateChild("Sensor" + sensor) as ToggleButton;
                SensorSelectionButtons[sensor].Click += delegate(object sender, RoutedEventArgs e)
                {
                    ClickedSensorButton(ThisSensor);
                };
            }

            // These settings are stored in the application settings, not on the pad.  However,
            // we treat changes to this as config changes, so we can use the same OnConfigChange
            // method for updating.
            OnConfigChange onConfigChange;
            onConfigChange = new OnConfigChange(this, delegate(LoadFromConfigDelegateArgs args) {
                LoadUIFromConfig(args);
            });
        }

        private void ClickedSensorButton(int sensor)
        {
            // Toggle the clicked sensor.
            Console.WriteLine("Clicked sensor " + sensor);
            List<ThresholdSettings.PanelAndSensor> auxSensors = ThresholdSettings.GetAuxSensors();
            bool enabled = !IsSensorEnabled(auxSensors, sensor);

            if(enabled)
                auxSensors.Add(new ThresholdSettings.PanelAndSensor(Panel, sensor));
            else
                auxSensors.Remove(new ThresholdSettings.PanelAndSensor(Panel, sensor));
            ThresholdSettings.SetAuxSensors(auxSensors);

            CurrentSMXDevice.singleton.FireConfigurationChanged(this);
        }

        // Return true if the given sensor is marked as an aux sensor.
        bool IsSensorEnabled(List<ThresholdSettings.PanelAndSensor> auxSensors, int sensor)
        {
            foreach(ThresholdSettings.PanelAndSensor panelAndSensor in auxSensors)
            {
                if(panelAndSensor.panel == Panel && panelAndSensor.sensor == sensor)
                    return true;
            }
            return false;
        }

        private void LoadUIFromConfig(LoadFromConfigDelegateArgs args)
        {
            // Check the selected aux sensors.
            List<ThresholdSettings.PanelAndSensor> auxSensors = ThresholdSettings.GetAuxSensors();
            for(int sensor = 0; sensor < 4; ++sensor)
                SensorSelectionButtons[sensor].IsChecked = IsSensorEnabled(auxSensors, sensor);
        }
    }

    // This dialog sets which sensors are controlled by the auxilliary threshold.  The actual
    // work is done by SensorSelector above.
    public partial class SetAuxSensors: Window
    {
        public SetAuxSensors()
        {
            InitializeComponent();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
} 
