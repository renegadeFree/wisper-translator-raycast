using System.Globalization;
using System.Windows.Data;

namespace WisperTranslator.App.Converters;

/// <summary>Moltiplica un valore (usato per derivare il font del testo originale).</summary>
public sealed class ScaleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var scale = parameter is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 1.0;
        return value is double number ? number * scale : value ?? 0d;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
