using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RadiationShieldingDesign
{
    public sealed class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool v && v;
            return b ? Visibility.Collapsed : Visibility.Visible;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is Visibility v) ? v != Visibility.Visible : false;
    }
}