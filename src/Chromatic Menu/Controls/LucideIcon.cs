using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ChromaticMenu.Controls
{
    public class LucideIcon : Control
    {
        static LucideIcon()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(LucideIcon),
                new FrameworkPropertyMetadata(typeof(LucideIcon)));
        }

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(string), typeof(LucideIcon),
                new PropertyMetadata(null, OnIconChanged));

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(Geometry), typeof(LucideIcon),
                new PropertyMetadata(null));

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(LucideIcon),
                new PropertyMetadata(2.0));

        public string Icon
        {
            get => (string)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public Geometry Data
        {
            get => (Geometry)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LucideIcon iconControl)
            {
                iconControl.UpdateGeometry();
            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdateGeometry();
        }

        private void UpdateGeometry()
        {
            if (string.IsNullOrWhiteSpace(Icon))
            {
                Data = null;
                return;
            }

            string key = Icon.StartsWith("Icon.") ? Icon : "Icon." + Icon;

            // Look up in Application resources
            if (Application.Current != null && Application.Current.TryFindResource(key) is Geometry geom)
            {
                Data = geom;
            }
            else
            {
                Data = null;
            }
        }
    }
}
