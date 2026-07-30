namespace PdfGrouping.Core;

/// <summary>
/// Чистая логика решений о пересечениях диапазонов страниц: что с чем пересекается,
/// какие страницы дублируются, что оставить при разрешении конфликтов.
///
/// Все диапазоны привязаны к исходному файлу (<see cref="FileRange"/>): страница 5 файла A и
/// страница 5 файла B — РАЗНЫЕ страницы, поэтому пересечение проверяется только МЕЖДУ
/// диапазонами ОДНОГО И ТОГО ЖЕ файла; диапазоны разных файлов никогда не конфликтуют.
///
/// Здесь НЕТ ни UI, ни локализации, ни состояния — только функции «данные на входе →
/// результат на выходе». Благодаря этому логика проверяется юнит-тестами напрямую
/// (см. OverlapAnalysisTests), а слой интерфейса лишь превращает результат в тексты и кнопки.
/// Интервалы всюду 1-based и включительные: (10, 12) = страницы 10, 11, 12.
/// </summary>
public static class OverlapAnalysis
{
    /// <summary>Диапазон страниц конкретного исходного файла (File — полный путь).</summary>
    public readonly record struct FileRange(string File, int Start, int End);

    /// <summary>
    /// Одно пересечение нового диапазона с уже занятым интервалом (того же файла).
    /// </summary>
    /// <param name="Existing">Уже занятый интервал — того же файла, что и новый диапазон.</param>
    /// <param name="GroupLabel">Метка группы-источника или null, если источник — текущие диапазоны.</param>
    /// <param name="DupStart">Начало дублируемого куска (пересечение нового с занятым).</param>
    /// <param name="DupEnd">Конец дублируемого куска.</param>
    public sealed record Hit(FileRange Existing, string? GroupLabel, int DupStart, int DupEnd);

    /// <summary>Итог анализа нового диапазона против всего уже выбранного.</summary>
    public sealed record Report(IReadOnlyList<Hit> Hits, IReadOnlyList<(int Start, int End)> DupIntervals)
    {
        /// <summary>Есть ли хоть одно пересечение.</summary>
        public bool HasOverlaps => Hits.Count > 0;

        /// <summary>Пустой отчёт (пересечений нет).</summary>
        public static readonly Report Empty = new(Array.Empty<Hit>(), Array.Empty<(int, int)>());
    }

    /// <summary>
    /// Анализирует новый диапазон против текущих диапазонов и страниц групп. Сравнение — только
    /// с диапазонами ТОГО ЖЕ файла, что и newRange; диапазоны других файлов не учитываются.
    /// Возвращает список пересечений (в порядке: сначала текущие диапазоны, затем группы —
    /// в порядке перечисления) и сырые дублируемые интервалы (по одному на пересечение, без слияния).
    /// </summary>
    public static Report Analyze(FileRange newRange,
        IEnumerable<FileRange> currentRanges,
        IEnumerable<(string Label, FileRange Range)> groupRanges)
    {
        var hits = new List<Hit>();

        foreach (var r in currentRanges)
            if (r.File == newRange.File && newRange.Start <= r.End && newRange.End >= r.Start)
                hits.Add(new Hit(r, null, Math.Max(newRange.Start, r.Start), Math.Min(newRange.End, r.End)));

        foreach (var (label, r) in groupRanges)
            if (r.File == newRange.File && newRange.Start <= r.End && newRange.End >= r.Start)
                hits.Add(new Hit(r, label, Math.Max(newRange.Start, r.Start), Math.Min(newRange.End, r.End)));

        if (hits.Count == 0)
            return Report.Empty;

        var dups = hits.Select(h => (h.DupStart, h.DupEnd)).ToList();
        return new Report(hits, dups);
    }

    /// <summary>Есть ли пересечения между диапазонами ОДНОГО файла внутри набора.</summary>
    public static bool HasInternalOverlaps(IReadOnlyList<FileRange> ranges)
    {
        for (int i = 0; i < ranges.Count; i++)
            for (int j = i + 1; j < ranges.Count; j++)
                if (ranges[i].File == ranges[j].File &&
                    ranges[i].Start <= ranges[j].End && ranges[i].End >= ranges[j].Start)
                    return true;
        return false;
    }

    /// <summary>Все попарные пересечения внутри набора (только между диапазонами ОДНОГО файла).</summary>
    public static List<FileRange> InternalIntersections(IReadOnlyList<FileRange> ranges)
    {
        var result = new List<FileRange>();
        for (int i = 0; i < ranges.Count; i++)
            for (int j = i + 1; j < ranges.Count; j++)
                if (ranges[i].File == ranges[j].File &&
                    ranges[i].Start <= ranges[j].End && ranges[i].End >= ranges[j].Start)
                    result.Add(new FileRange(ranges[i].File,
                        Math.Max(ranges[i].Start, ranges[j].Start),
                        Math.Min(ranges[i].End, ranges[j].End)));
        return result;
    }

    /// <summary>Все попарные пересечения первого набора со вторым (только между ОДНИМ и тем же файлом).</summary>
    public static List<FileRange> Intersections(IEnumerable<FileRange> first, IEnumerable<FileRange> second)
    {
        var secondList = second as IReadOnlyList<FileRange> ?? second.ToList();
        var result = new List<FileRange>();
        foreach (var a in first)
            foreach (var b in secondList)
                if (a.File == b.File && a.Start <= b.End && a.End >= b.Start)
                    result.Add(new FileRange(a.File, Math.Max(a.Start, b.Start), Math.Min(a.End, b.End)));
        return result;
    }

    /// <summary>
    /// Разрешение «Убрать пересекающиеся»: оставить только диапазоны, не пересекающиеся с уже
    /// оставленными ранее диапазонами ТОГО ЖЕ файла (приоритет — у более ранних в списке).
    /// Диапазоны разных файлов никогда друг друга не вытесняют. Порядок сохраняется.
    /// </summary>
    public static List<FileRange> KeepFirstOccupiers(IEnumerable<FileRange> ranges)
    {
        var kept = new List<FileRange>();
        foreach (var r in ranges)
        {
            bool overlapsKept = kept.Any(k => k.File == r.File && r.Start <= k.End && r.End >= k.Start);
            if (!overlapsKept)
                kept.Add(r);
        }
        return kept;
    }

    /// <summary>
    /// Разрешение «Подтвердить» (обрезка/разбиение по занятым страницам): для КАЖДОГО файла
    /// отдельно вызывает <see cref="PageRangeUtils.ResolveOverlaps"/> — диапазоны разных файлов
    /// друг на друга не влияют. Результат сгруппирован по файлам (в порядке первого появления).
    /// </summary>
    public static List<FileRange> ResolveOverlapsPerFile(IEnumerable<FileRange> ranges)
    {
        var result = new List<FileRange>();
        foreach (var group in ranges.GroupBy(r => r.File))
        {
            var resolved = PageRangeUtils.ResolveOverlaps(group.Select(r => (r.Start, r.End)));
            foreach (var (s, e) in resolved)
                result.Add(new FileRange(group.Key, s, e));
        }
        return result;
    }
}
