using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using PdfGrouping.App.Models;

namespace PdfGrouping.App.Converters;

public class RangesToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ObservableCollection<PageRange> ranges && ranges.Count > 0)
            return string.Join(", ", ranges.Select(r => $"{r.StartPage}–{r.EndPage}"));
        return "—";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}