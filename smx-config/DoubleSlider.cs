using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace smx_config
{
    // A slider with two handles, and a handle connecting them.  Dragging the handle drags both
    // of the sliders.
    class DoubleSlider: Control
    {
        public delegate void ValueChangedDelegate(DoubleSlider slider);
        public event ValueChangedDelegate ValueChanged;
        private void FireValueChanged() { ValueChanged?.Invoke(this); }

        // The minimum value for either knob.
        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register("Minimum",
            typeof(double), typeof(DoubleSlider), new FrameworkPropertyMetadata(0.0));

        public double Minimum {
            get { return (double) GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }

        // The maximum value for either knob.
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register("Maximum",
            typeof(double), typeof(DoubleSlider), new FrameworkPropertyMetadata(20.0));

        public double Maximum {
            get { return (double) GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        // The minimum distance between the two values.
        public static readonly DependencyProperty MinimumDistanceProperty = DependencyProperty.Register("MinimumDistance",
            typeof(double), typeof(DoubleSlider), new FrameworkPropertyMetadata(0.0));

        public double MinimumDistance {
            get { return (double) GetValue(MinimumDistanceProperty); }
            set { SetValue(MinimumDistanceProperty, value); }
        }

        // Clamp value between minimum and maximum.
        private double CoerceValueToLimits(double value)
        {
            return Math.Min(Math.Max(value, Minimum), Maximum);
        }

        // Note that we only clamp LowerValue and UpperValue to the min/max values.  We don't
        // clamp them to each other or to MinimumDistance here, since that complicates setting
        // properties a lot.  We only clamp to those when the user manipulates the control, not
        // when we set values directly.
        private static object LowerValueCoerceValueCallback(DependencyObject target, object valueObject)
        {
            DoubleSlider slider = target as DoubleSlider;
            double value = (double)valueObject;
            value = slider.CoerceValueToLimits(value);
            return value;
        }

        private static object UpperValueCoerceValueCallback(DependencyObject target, object valueObject)
        {
            DoubleSlider slider = target as DoubleSlider;
            double value = (double)valueObject;
            value = slider.CoerceValueToLimits(value);
            return value;
        }

        public static readonly DependencyProperty LowerValueProperty = DependencyProperty.Register("LowerValue",
            typeof(double), typeof(DoubleSlider),
            new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsArrange, null, LowerValueCoerceValueCallback));

        public double LowerValue {
            get { return (double) GetValue(LowerValueProperty); }
            set { SetValue(LowerValueProperty, value); }
        }

        public static readonly DependencyProperty UpperValueProperty = DependencyProperty.Register("UpperValue",
            typeof(double), typeof(DoubleSlider),
            new FrameworkPropertyMetadata(15.0, FrameworkPropertyMetadataOptions.AffectsArrange, null, UpperValueCoerceValueCallback));

        public double UpperValue {
            get { return (double) GetValue(UpperValueProperty); }
            set { SetValue(UpperValueProperty, value); }
        }

        private Thumb Middle;

        Thumb UpperThumb;
        Thumb LowerThumb;

        private RepeatButton DecreaseButton;
        private RepeatButton IncreaseButton;

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            arrangeSize = base.ArrangeOverride(arrangeSize);

            // Figure out the X position of the upper and lower thumbs.  Note that we have to provide
            // our width to GetValueToSize, since ActualWidth isn't available yet.
            double valueToSize = GetValueToSize(arrangeSize.Width);
            double UpperPointX = (UpperValue-Minimum) * valueToSize;
            double LowerPointX = (LowerValue-Minimum) * valueToSize;

            // Move the upper and lower handles out by this much, and extend this middle.  This
            // makes the middle handle bigger.
            double OffsetOutwards = 5;
            Middle.Arrange(new Rect(LowerPointX-OffsetOutwards-1, 0,
                Math.Max(1, UpperPointX-LowerPointX+OffsetOutwards*2+2), arrangeSize.Height));

            // Right-align the lower thumb and left-align the upper thumb.
            LowerThumb.Arrange(new Rect(LowerPointX-LowerThumb.Width-OffsetOutwards, 0, LowerThumb.Width, arrangeSize.Height));
            UpperThumb.Arrange(new Rect(UpperPointX                 +OffsetOutwards, 0, UpperThumb.Width, arrangeSize.Height));

            DecreaseButton.Arrange(new Rect(0, 0, Math.Max(1, LowerPointX), Math.Max(1, arrangeSize.Height)));
            IncreaseButton.Arrange(new Rect(UpperPointX, 0, Math.Max(1, arrangeSize.Width - UpperPointX), arrangeSize.Height));
            return arrangeSize;
        }
        
        private void MoveValue(double delta)
        {
            if(delta > 0)
            {
                // If this increase will be clamped when changing the upper value, reduce it
                // so it clamps the lower value too.  This way, the distance between the upper
                // and lower value stays the same.
                delta = Math.Min(delta, Maximum - UpperValue);
                UpperValue += delta;
                LowerValue += delta;
            }
            else
            {
                delta *= -1;
                delta = Math.Min(delta, LowerValue - Minimum);
                LowerValue -= delta;
                UpperValue -= delta;
            }

            FireValueChanged();
        }

        private double GetValueToSize()
        {
            return GetValueToSize(this.ActualWidth);
        }

        private double GetValueToSize(double width)
        {
            double Range = Maximum - Minimum;
            return Math.Max(0.0, (width - UpperThumb.RenderSize.Width) / Range);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            LowerThumb = GetTemplateChild("PART_LowerThumb") as Thumb;
            UpperThumb = GetTemplateChild("PART_UpperThumb") as Thumb;
            Middle = GetTemplateChild("PART_Middle") as Thumb;
            DecreaseButton = GetTemplateChild("PART_DecreaseButton") as RepeatButton;
            IncreaseButton = GetTemplateChild("PART_IncreaseButton") as RepeatButton;
            DecreaseButton.Click += delegate(object sender, RoutedEventArgs e) { MoveValue(-1); };
            IncreaseButton.Click += delegate(object sender, RoutedEventArgs e) { MoveValue(+1); };

            LowerThumb.DragDelta += delegate(object sender, DragDeltaEventArgs e)
            {
                double sizeToValue = 1 / GetValueToSize();

                double NewValue = LowerValue + e.HorizontalChange * sizeToValue;
                NewValue = Math.Min(NewValue, UpperValue - MinimumDistance);
                LowerValue = NewValue;
                FireValueChanged();
            };

            UpperThumb.DragDelta += delegate(object sender, DragDeltaEventArgs e)
            {
                double sizeToValue = 1 / GetValueToSize();
                double NewValue = UpperValue + e.HorizontalChange * sizeToValue;
                NewValue = Math.Max(NewValue, LowerValue + MinimumDistance);
                UpperValue = NewValue;
                FireValueChanged();
            };

            Middle.DragDelta += delegate(object sender, DragDeltaEventArgs e)
            {
                // Convert the pixel delta to a value change.
                double sizeToValue = 1 / GetValueToSize();
                Console.WriteLine("drag: " + e.HorizontalChange + ", " + sizeToValue + ", " + e.HorizontalChange * sizeToValue);
                MoveValue(e.HorizontalChange * sizeToValue);
            };

            InvalidateArrange();
        }
    }

    class DoubleSliderThumb: Thumb
    {
        public static readonly DependencyProperty ShowUpArrowProperty = DependencyProperty.Register("ShowUpArrow",
            typeof(bool), typeof(DoubleSliderThumb));

        public bool ShowUpArrow {
            get { return (bool) this.GetValue(ShowUpArrowProperty); }
            set { this.SetValue(ShowUpArrowProperty, value); }
        }

        public static readonly DependencyProperty ShowDownArrowProperty = DependencyProperty.Register("ShowDownArrow",
            typeof(bool), typeof(DoubleSliderThumb));

        public bool ShowDownArrow {
            get { return (bool) this.GetValue(ShowDownArrowProperty); }
            set { this.SetValue(ShowDownArrowProperty, value); }
        }
    }
}
