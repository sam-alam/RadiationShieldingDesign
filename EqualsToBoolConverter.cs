using System.Globalization;
using System.Windows.Data;

namespace RadiationShieldingDesign
{
    public sealed class EqualsToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b && parameter is not null)
                return parameter.ToString()!; // safe, not null here

            return Binding.DoNothing; // never return null
        }
    }
}