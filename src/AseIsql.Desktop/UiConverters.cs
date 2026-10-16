using System.Globalization;
using System.Windows.Data;

namespace AseIsql.Desktop;

// DBMON's table headers are uppercase (text-transform). The display is transformed; column captions stay intact.
public sealed class UpperCaseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string text ? text.ToUpperInvariant() : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
