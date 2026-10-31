using System.Globalization;
using System.Windows.Data;

namespace RadiationShieldingDesign
{
    public class LenientDoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && double.TryParse(s, NumberStyles.Any, culture, out var d))
                return d;
            return Binding.DoNothing;
        }
    }
}