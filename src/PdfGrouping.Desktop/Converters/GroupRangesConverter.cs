using System.Globalization;
using Avalonia.Data.Converters;
using PdfGrouping.Core.Models;
using PdfGrouping.Desktop.Localization;

namespace PdfGrouping.Desktop.Converters;

/// <summary>
/// Форматирует диапазоны сформированной группы в ТОМ ЖЕ стиле и с той же меткой источника, что
/// список «Диапазоны страниц» («[N источник] Стр. X–Y (N стр.)»), вместо прежнего компактного
/// «X–Y, X2–Y2». Метка источника — только когда диапазоны группы взяты из НЕСКОЛЬКИХ разных файлов.
/// </summary>
public class GroupRangesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IEnumerable<PageRange> ranges) return string.Empty;
        var list = ranges.ToList();
        if (list.Count == 0) return "—";

        var loc = Localizer.Instance;
        bool multiFile = list.Select(r => r.SourceFile).Distinct().Count() > 1;
        string pagesWord = loc.Get("Range_PagesShort");
        string unitWord = loc.Get("Unit_PagesShort");

        return string.Join(", ", list.Select(r =>
        {
            string tag = multiFile ? loc.Format("Range_SourceTag", r.FileNumber) : string.Empty;
            return $"{tag}{pagesWord} {r.StartPage}–{r.EndPage} ({r.PageCount} {unitWord})";
        }));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
