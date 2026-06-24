using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PdfGrouping.Desktop.Converters;

/// <summary>
/// Угол (градусы) → <see cref="RotateTransform"/>. Нужно потому, что трансформации внутри
/// RenderTransform не наследуют DataContext — привязку вешаем на сам RenderTransform.
/// </summary>
public class AngleToTransformConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new RotateTransform(value is double d ? d : 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
