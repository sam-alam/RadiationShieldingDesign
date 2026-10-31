using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RadiationShieldingDesign
{
    public class GaugeToBrushConverter : IValueConverter
    {
        // Map gauge label -> brush
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var key = value as string ?? "";
            return key switch
            {
                "1/64\"" => Brushes.MediumPurple,
                "1/32\"" => Brushes.DeepSkyBlue,
                "5/128\"" => Brushes.SteelBlue,
                "3/64\"" => Brushes.LimeGreen,
                "1/16\"" => Brushes.Goldenrod,
                "5/64\"" => Brushes.DarkMagenta,
                "3/32\"" => Brushes.OrangeRed,
                "1/8\"" => Brushes.Green,
                "5/32\"" => Brushes.Blue,
                "3/16\"" => Brushes.RosyBrown,
                "1/4\"" => Brushes.Red,
                "3/8\"" => Brushes.DarkRed,
                "1/2\"" => Brushes.IndianRed,
                "3/4\"" => Brushes.MediumVioletRed,
                "1\"" => Brushes.PaleVioletRed,
                "1-1/2\"" => Brushes.Tan,
                "2\"" => Brushes.Teal,
                _ => Brushes.White
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}