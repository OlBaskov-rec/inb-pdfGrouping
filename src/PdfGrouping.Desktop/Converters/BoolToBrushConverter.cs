using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PdfGrouping.Desktop.Converters;

/// <summary>
/// true → красная кисть (ошибка), false → нейтральная. Для подсветки статуса.
/// </summary>
public class BoolToBrushConverter : IValueConverter
{
    private static readonly IBrush Error = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
    private static readonly IBrush Normal = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Error : Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
