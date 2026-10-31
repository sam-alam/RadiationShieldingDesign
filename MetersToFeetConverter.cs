using System.Globalization;
using System.Windows.Data;

namespace RadiationShieldingDesign
{
    // Returns a single formatted string: "X.XXX m (Y.YY ft)"
    public class MetersToFeetConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double m)
            {
                double ft = m / 0.3048;
                return $"{m:F3} m ({ft:F2} ft)";
            }
            return "0.000 m (0.00 ft)";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}