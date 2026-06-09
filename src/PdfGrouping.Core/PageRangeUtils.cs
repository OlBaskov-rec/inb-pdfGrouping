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

    /// <summary>
    /// Делает набор диапазонов непересекающимся: каждый следующий диапазон обрезается по уже
    /// занятым страницам (приоритет — у того, кто занял страницы раньше). Из одного диапазона
    /// при этом может получиться несколько «заполняющих» кусков. Порядок сохраняется.
    /// </summary>
    public static List<(int Start, int End)> ResolveOverlaps(IEnumerable<(int Start, int End)> ranges)
    {
        var result = new List<(int, int)>();
        var covered = new List<(int s, int e)>(); // отсортированный, слитый список занятых страниц

        foreach (var (s, e) in ranges)
        {
            if (e < s) continue;
            foreach (var piece in SubtractCovered(s, e, covered))
                result.Add(piece);
            covered = AddAndMerge(covered, s, e);
        }

        return result;
    }

    /// <summary>
    /// Вычитает из диапазонов <paramref name="ranges"/> уже занятые страницы
    /// <paramref name="covered"/>. Возвращает непересекающиеся «свободные» куски.
    /// </summary>
    public static List<(int Start, int End)> Subtract(
        IEnumerable<(int Start, int End)> ranges,
        IEnumerable<(int Start, int End)> covered)
    {
        var cov = Merge(covered);
        var result = new List<(int, int)>();
        foreach (var (s, e) in ranges)
        {
            if (e < s) continue;
            result.AddRange(SubtractCovered(s, e, cov));
        }
        return result;
    }

    private static List<(int s, int e)> Merge(IEnumerable<(int Start, int End)> intervals)
    {
        var list = intervals.Where(i => i.End >= i.Start).OrderBy(i => i.Start).ToList();
        var merged = new List<(int, int)>();
        if (list.Count == 0) return merged;

        var (cs, ce) = (list[0].Start, list[0].End);
        foreach (var (s, e) in list.Skip(1))
        {
            if (s <= ce + 1) ce = Math.Max(ce, e);
            else { merged.Add((cs, ce)); cs = s; ce = e; }
        }
        merged.Add((cs, ce));
        return merged;
    }

    private static IEnumerable<(int, int)> SubtractCovered(int s, int e, List<(int s, int e)> covered)
    {
        int cur = s;
        foreach (var (cs, ce) in covered)
        {
            if (ce < cur) continue;
            if (cs > e) break;
            if (cs > cur) yield return (cur, Math.Min(cs - 1, e));
            cur = Math.Max(cur, ce + 1);
            if (cur > e) yield break;
        }
        if (cur <= e) yield return (cur, e);
    }

    private static List<(int, int)> AddAndMerge(List<(int s, int e)> covered, int s, int e)
    {
        var all = new List<(int s, int e)>(covered) { (s, e) };
        all.Sort((a, b) => a.s.CompareTo(b.s));

        var merged = new List<(int, int)>();
        var (cs, ce) = all[0];
        foreach (var (ns, ne) in all.Skip(1))
        {
            if (ns <= ce + 1) ce = Math.Max(ce, ne);
            else { merged.Add((cs, ce)); cs = ns; ce = ne; }
        }
        merged.Add((cs, ce));
        return merged;
    }
}
