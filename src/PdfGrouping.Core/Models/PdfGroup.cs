using System.Collections.ObjectModel;

namespace PdfGrouping.Core.Models;

/// <summary>
/// Группа = один будущий выходной PDF: метка (имя файла) + набор диапазонов страниц.
/// </summary>
public class PdfGroup
{
    public string Label { get; set; } = string.Empty;

    public ObservableCollection<PageRange> Ranges { get; set; } = new();

    public int TotalPages
    {
        get
        {
            int total = 0;
            foreach (var r in Ranges)
                total += r.PageCount;
            return total;
        }
    }

    public override string ToString() => $"{Label} ({TotalPages} стр.)";
}
