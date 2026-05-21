using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MarkdownViewer.Wpf.Converters
{
    /// <summary>
    /// Converts boolean values to Visibility enum values
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                var invert = parameter is string text &&
                    text.Equals("invert", StringComparison.OrdinalIgnoreCase);

                return invert
                    ? (boolValue ? Visibility.Collapsed : Visibility.Visible)
                    : (boolValue ? Visibility.Visible : Visibility.Collapsed);
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                var invert = parameter is string text &&
                    text.Equals("invert", StringComparison.OrdinalIgnoreCase);

                return invert
                    ? visibility == Visibility.Collapsed
                    : visibility == Visibility.Visible;
            }

            return false;
        }
    }
}
