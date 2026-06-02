using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PdfGrouping.App.Converters;

public class ProcessingColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)); // blue
        return new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10)); // green (success/idle)
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}