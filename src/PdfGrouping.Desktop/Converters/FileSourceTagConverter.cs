using System.Globalization;
using Avalonia.Data.Converters;
using PdfGrouping.Desktop.Localization;

namespace PdfGrouping.Desktop.Converters;

/// <summary>
/// Номер файла-источника (int) → локализованная метка «[N источник] ». Используется у диапазонов
/// в «Диапазоны страниц», когда загружено больше одного файла (см. ShowFileTags).
/// </summary>
public class FileSourceTagConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int n || n <= 0) return string.Empty;
        return Localizer.Instance.Format("Range_SourceTag", n);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
