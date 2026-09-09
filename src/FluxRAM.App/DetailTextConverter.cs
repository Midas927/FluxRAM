using System.Globalization;
using System.Windows.Data;

namespace FluxRAM.App;

public sealed class DetailTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;
        var separator = text.Contains(" | ", StringComparison.Ordinal) ? " | " : "  ";
        var index = text.IndexOf(separator, StringComparison.Ordinal);
        return parameter as string == "Title"
            ? index < 0 ? text : text[..index]
            : index < 0 ? string.Empty : text[(index + separator.Length)..];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
