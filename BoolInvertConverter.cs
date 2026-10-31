using System.Globalization;
using System.Windows.Data;

namespace RadiationShieldingDesign
{
    public sealed class BoolInvertConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => value is bool b ? !b : value;
    }
}