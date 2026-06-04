namespace PdfGrouping.Core;

/// <summary>Утилиты для компактного представления наборов страниц.</summary>
public static class PageRangeUtils
{
    /// <summary>
    /// Сливает интервалы (пересекающиеся и смежные) и форматирует компактно,
    /// напр. «10–23, 30, 40–42». Пустые/некорректные интервалы пропускаются.
    /// </summary>
    public static string MergeToString(IEnumerable<(int Start, int End)> intervals)
    {
        var list = intervals
            .Where(i => i.End >= i.Start)
            .OrderBy(i => i.Start)
            .ThenBy(i => i.End)
            .ToList();

        if (list.Count == 0)
            return string.Empty;

        var merged = new List<(int s, int e)>();
        var (cs, ce) = list[0];

        foreach (var (s, e) in list.Skip(1))
        {
            if (s <= ce + 1)               // пересекается или смежный
                ce = Math.Max(ce, e);
            else
            {
                merged.Add((cs, ce));
                cs = s; ce = e;
            }
        }
        merged.Add((cs, ce));

        return string.Join(", ", merged.Select(m => m.s == m.e ? $"{m.s}" : $"{m.s}–{m.e}"));
    }

    /// <summary>
    /// Разворачивает интервалы в явный перечень номеров страниц: «8, 9, 10, 30, 31».
    /// При количестве больше <paramref name="maxCount"/> остаток сворачивается в «… (всего N)».
    /// </summary>
    public static string ExpandToString(IEnumerable<(int Start, int End)> intervals, int maxCount = 60)
    {
        var pages = new SortedSet<int>();
        foreach (var (s, e) in intervals)
            for (int p = Math.Max(1, s); p <= e; p++)
                pages.Add(p);

        if (pages.Count == 0)
            return string.Empty;

        if (pages.Count <= maxCount)
            return string.Join(", ", pages);

        return string.Join(", ", pages.Take(maxCount)) + $", … (всего {pages.Count})";
    }
}
