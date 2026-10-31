using System.Globalization;
using System.Windows.Data;

namespace RadiationShieldingDesign
{
    public class DoubleFractionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                return d switch
                {
                    1.0 => "1",
                    0.5 => "1/2",
                    0.2 => "1/5",
                    0.125 => "1/8",
                    0.05 => "1/20",
                    0.025 => "1/40",
                    _ => d.ToString(culture)
                };
            }
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value?.ToString()?.Trim() ?? "";
            return s switch
            {
                "1" => 1.0,
                "1/2" => 0.5,
                "1/5" => 0.2,
                "1/8" => 0.125,
                "1/20" => 0.05,
                "1/40" => 0.025,
                _ when double.TryParse(s, NumberStyles.Float, culture, out var v) => v,
                _ => 1.0
            };
        }
    }
}