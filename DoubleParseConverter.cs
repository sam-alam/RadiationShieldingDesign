using System.Globalization;
using System.Windows.Data;

namespace RadiationShieldingDesign
{

    public sealed class DoubleParseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return d;
            if (value is string s2 && double.TryParse(s2, NumberStyles.Float, CultureInfo.CurrentCulture, out var d2))
                return d2;
            return 0.0;
        }
    }
}
