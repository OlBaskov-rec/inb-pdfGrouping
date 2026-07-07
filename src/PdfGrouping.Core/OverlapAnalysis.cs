namespace PdfGrouping.Core;

/// <summary>
/// Чистая логика решений о пересечениях диапазонов страниц: что с чем пересекается,
/// какие страницы дублируются, что оставить при разрешении конфликтов.
///
/// Здесь НЕТ ни UI, ни локализации, ни состояния — только функции «данные на входе →
/// результат на выходе». Благодаря этому логика проверяется юнит-тестами напрямую
/// (см. OverlapAnalysisTests), а слой интерфейса лишь превращает результат в тексты и кнопки.
/// Интервалы всюду 1-based и включительные: (10, 12) = страницы 10, 11, 12.
/// </summary>
public static class OverlapAnalysis
{
    /// <summary>
    /// Одно пересечение нового диапазона с уже занятым интервалом.
    /// </summary>
    /// <param name="ExistingStart">Начало занятого интервала.</param>
    /// <param name="ExistingEnd">Конец занятого интервала.</param>
    /// <param name="GroupLabel">Метка группы-источника или null, если источник — текущие диапазоны.</param>
    /// <param name="DupStart">Начало дублируемого куска (пересечение нового с занятым).</param>
    /// <param name="DupEnd">Конец дублируемого куска.</param>
    public sealed record Hit(int ExistingStart, int ExistingEnd, string? GroupLabel, int DupStart, int DupEnd);

    /// <summary>Итог анализа нового диапазона против всего уже выбранного.</summary>
    public sealed record Report(IReadOnlyList<Hit> Hits, IReadOnlyList<(int Start, int End)> DupIntervals)
    {
        /// <summary>Есть ли хоть одно пересечение.</summary>
        public bool HasOverlaps => Hits.Count > 0;

        /// <summary>Пустой отчёт (пересечений нет).</summary>
        public static readonly Report Empty = new(Array.Empty<Hit>(), Array.Empty<(int, int)>());
    }

    /// <summary>
    /// Анализирует новый диапазон [newStart..newEnd] против текущих диапазонов и страниц групп.
    /// Возвращает список пересечений (в порядке: сначала текущие диапазоны, затем группы —
    /// в порядке перечисления) и сырые дублируемые интервалы (по одному на пересечение, без слияния).
    /// </summary>
    public static Report Analyze(int newStart, int newEnd,
        IEnumerable<(int Start, int End)> currentRanges,
        IEnumerable<(string Label, int Start, int End)> groupRanges)
    {
        var hits = new List<Hit>();

        foreach (var (s, e) in currentRanges)
            if (newStart <= e && newEnd >= s)
                hits.Add(new Hit(s, e, null, Math.Max(newStart, s), Math.Min(newEnd, e)));

        foreach (var (label, s, e) in groupRanges)
            if (newStart <= e && newEnd >= s)
                hits.Add(new Hit(s, e, label, Math.Max(newStart, s), Math.Min(newEnd, e)));

        if (hits.Count == 0)
            return Report.Empty;

        var dups = hits.Select(h => (h.DupStart, h.DupEnd)).ToList();
        return new Report(hits, dups);
    }

    /// <summary>Есть ли пересечения между диапазонами внутри одного набора.</summary>
    public static bool HasInternalOverlaps(IReadOnlyList<(int Start, int End)> ranges)
    {
        for (int i = 0; i < ranges.Count; i++)
            for (int j = i + 1; j < ranges.Count; j++)
                if (ranges[i].Start <= ranges[j].End && ranges[i].End >= ranges[j].Start)
                    return true;
        return false;
    }

    /// <summary>Все попарные пересечения внутри одного набора (сырые куски, без слияния).</summary>
    public static List<(int Start, int End)> InternalIntersections(IReadOnlyList<(int Start, int End)> ranges)
    {
        var result = new List<(int, int)>();
        for (int i = 0; i < ranges.Count; i++)
            for (int j = i + 1; j < ranges.Count; j++)
                if (ranges[i].Start <= ranges[j].End && ranges[i].End >= ranges[j].Start)
                    result.Add((Math.Max(ranges[i].Start, ranges[j].Start),
                                Math.Min(ranges[i].End, ranges[j].End)));
        return result;
    }

    /// <summary>Все попарные пересечения диапазонов первого набора со вторым (сырые куски).</summary>
    public static List<(int Start, int End)> Intersections(
        IEnumerable<(int Start, int End)> first,
        IEnumerable<(int Start, int End)> second)
    {
        var secondList = second as IReadOnlyList<(int Start, int End)> ?? second.ToList();
        var result = new List<(int, int)>();
        foreach (var a in first)
            foreach (var b in secondList)
                if (a.Start <= b.End && a.End >= b.Start)
                    result.Add((Math.Max(a.Start, b.Start), Math.Min(a.End, b.End)));
        return result;
    }

    /// <summary>
    /// Разрешение «Убрать пересекающиеся»: оставить только диапазоны, не пересекающиеся с уже
    /// оставленными ранее (приоритет — у более ранних в списке). Порядок сохраняется.
    /// </summary>
    public static List<(int Start, int End)> KeepFirstOccupiers(IEnumerable<(int Start, int End)> ranges)
    {
        var kept = new List<(int Start, int End)>();
        foreach (var r in ranges)
        {
            bool overlapsKept = kept.Any(k => r.Start <= k.End && r.End >= k.Start);
            if (!overlapsKept)
                kept.Add(r);
        }
        return kept;
    }
}
