using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RadiationShieldingDesign
{
    public class BoolToBrushConverter : IValueConverter
    {
        public Brush TrueBrush { get; set; } = Brushes.LightGray;
        public Brush FalseBrush { get; set; } = Brushes.White;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? TrueBrush : FalseBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}