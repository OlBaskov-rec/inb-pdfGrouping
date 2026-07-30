using System.Collections.ObjectModel;
using System.Linq;

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

    /// <summary>
    /// Диапазоны в виде строки, напр. «1–10, 25–30». Если группа собрана из НЕСКОЛЬКИХ разных
    /// файлов — перед каждым диапазоном добавляется имя его файла, напр. «книга1.pdf:1–10,
    /// книга2.pdf:5–9», иначе легко перепутать, откуда какие страницы.
    /// </summary>
    public string RangesText
    {
        get
        {
            if (Ranges.Count == 0) return "—";
            bool multiFile = Ranges.Select(r => r.SourceFile).Distinct().Count() > 1;
            return multiFile
                ? string.Join(", ", Ranges.Select(r => $"{r.FileName}:{r}"))
                : string.Join(", ", Ranges);
        }
    }

    public override string ToString() => $"{Label} ({TotalPages} стр.)";
}
