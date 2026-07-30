using PdfGrouping.Core;
using Xunit;
using FR = PdfGrouping.Core.OverlapAnalysis.FileRange;

namespace PdfGrouping.Core.Tests;

/// <summary>
/// Тесты логики решений о пересечениях. Раньше эта логика жила в модели представления (UI)
/// и проверялась только вручную; теперь она в Core и зафиксирована здесь как контракт.
/// Диапазоны привязаны к файлу — «A»/«B» ниже это разные исходные файлы, «a» — один и тот же.
/// </summary>
public class OverlapAnalysisTests
{
    private const string FileA = "a.pdf";
    private const string FileB = "b.pdf";

    private static readonly (string, FR)[] NoGroups = System.Array.Empty<(string, FR)>();

    // --- Analyze: обнаружение пересечений нового диапазона ---

    [Fact]
    public void Analyze_NoOverlaps_ReturnsEmpty()
    {
        var r = OverlapAnalysis.Analyze(new FR(FileA, 30, 40),
            new[] { new FR(FileA, 1, 10), new FR(FileA, 50, 60) }, NoGroups);
        Assert.False(r.HasOverlaps);
        Assert.Empty(r.Hits);
        Assert.Empty(r.DupIntervals);
    }

    [Fact]
    public void Analyze_OverlapWithCurrentRange_ReportsHitWithoutLabel()
    {
        var r = OverlapAnalysis.Analyze(new FR(FileA, 5, 15), new[] { new FR(FileA, 10, 20) }, NoGroups);

        var hit = Assert.Single(r.Hits);
        Assert.Null(hit.GroupLabel);              // источник — текущие диапазоны
        Assert.Equal((10, 20), (hit.Existing.Start, hit.Existing.End));
        Assert.Equal((10, 15), (hit.DupStart, hit.DupEnd)); // дублируется только общая часть
    }

    [Fact]
    public void Analyze_OverlapWithGroup_ReportsLabel()
    {
        var r = OverlapAnalysis.Analyze(new FR(FileA, 5, 15), Array.Empty<FR>(),
            new[] { ("A", new FR(FileA, 1, 7)) });

        var hit = Assert.Single(r.Hits);
        Assert.Equal("A", hit.GroupLabel);
        Assert.Equal((5, 7), (hit.DupStart, hit.DupEnd));
    }

    [Fact]
    public void Analyze_CurrentRangesComeBeforeGroups()
    {
        var r = OverlapAnalysis.Analyze(new FR(FileA, 1, 100),
            new[] { new FR(FileA, 10, 20) }, new[] { ("A", new FR(FileA, 30, 40)) });

        Assert.Equal(2, r.Hits.Count);
        Assert.Null(r.Hits[0].GroupLabel);
        Assert.Equal("A", r.Hits[1].GroupLabel);
        Assert.Equal(new[] { (10, 20), (30, 40) }, r.DupIntervals);
    }

    [Fact]
    public void Analyze_TouchingButNotOverlapping_IsNotHit()
    {
        // Диапазоны 1–10 и 11–20 смежные, но НЕ пересекаются — дубликатов нет.
        var r = OverlapAnalysis.Analyze(new FR(FileA, 11, 20), new[] { new FR(FileA, 1, 10) }, NoGroups);
        Assert.False(r.HasOverlaps);
    }

    [Fact]
    public void Analyze_SinglePageOverlap_IsDetected()
    {
        var r = OverlapAnalysis.Analyze(new FR(FileA, 10, 10), new[] { new FR(FileA, 10, 10) }, NoGroups);
        var hit = Assert.Single(r.Hits);
        Assert.Equal((10, 10), (hit.DupStart, hit.DupEnd));
    }

    [Fact]
    public void Analyze_NewRangeInsideExisting_DupIsWholeNewRange()
    {
        var r = OverlapAnalysis.Analyze(new FR(FileA, 12, 14), new[] { new FR(FileA, 10, 20) }, NoGroups);
        Assert.Equal((12, 14), r.DupIntervals[0]);
    }

    [Fact]
    public void Analyze_ExistingInsideNewRange_DupIsWholeExisting()
    {
        var r = OverlapAnalysis.Analyze(new FR(FileA, 1, 100), new[] { new FR(FileA, 40, 45) }, NoGroups);
        Assert.Equal((40, 45), r.DupIntervals[0]);
    }

    [Fact]
    public void Analyze_SamePagesDifferentFile_IsNotHit()
    {
        // Страница 5–15 файла B не пересекается со страницей 5–15 файла A — это разные страницы.
        var r = OverlapAnalysis.Analyze(new FR(FileB, 5, 15), new[] { new FR(FileA, 5, 15) }, NoGroups);
        Assert.False(r.HasOverlaps);
    }

    [Fact]
    public void Analyze_MixedFiles_OnlySameFileHitsReported()
    {
        var r = OverlapAnalysis.Analyze(new FR(FileA, 1, 100),
            new[] { new FR(FileA, 10, 20), new FR(FileB, 10, 20) }, NoGroups);

        var hit = Assert.Single(r.Hits);
        Assert.Equal(FileA, hit.Existing.File);
    }

    // --- HasInternalOverlaps / InternalIntersections ---

    [Fact]
    public void HasInternalOverlaps_Disjoint_False()
        => Assert.False(OverlapAnalysis.HasInternalOverlaps(
            new[] { new FR(FileA, 1, 10), new FR(FileA, 11, 20), new FR(FileA, 30, 40) }));

    [Fact]
    public void HasInternalOverlaps_Overlapping_True()
        => Assert.True(OverlapAnalysis.HasInternalOverlaps(
            new[] { new FR(FileA, 1, 10), new FR(FileA, 5, 20) }));

    [Fact]
    public void HasInternalOverlaps_SamePagesDifferentFiles_False()
        => Assert.False(OverlapAnalysis.HasInternalOverlaps(
            new[] { new FR(FileA, 1, 10), new FR(FileB, 1, 10) }));

    [Fact]
    public void InternalIntersections_ReturnsPairwisePieces()
    {
        var pieces = OverlapAnalysis.InternalIntersections(
            new[] { new FR(FileA, 1, 10), new FR(FileA, 5, 20), new FR(FileA, 8, 9) });
        // (1,10)×(5,20)=(5,10); (1,10)×(8,9)=(8,9); (5,20)×(8,9)=(8,9)
        Assert.Equal(new[] { (5, 10), (8, 9), (8, 9) }, pieces.Select(p => (p.Start, p.End)));
    }

    [Fact]
    public void InternalIntersections_IgnoresCrossFilePairs()
    {
        var pieces = OverlapAnalysis.InternalIntersections(
            new[] { new FR(FileA, 1, 10), new FR(FileB, 1, 10) });
        Assert.Empty(pieces);
    }

    // --- Intersections двух наборов ---

    [Fact]
    public void Intersections_FindsCrossSetPieces()
    {
        var pieces = OverlapAnalysis.Intersections(
            first: new[] { new FR(FileA, 1, 10), new FR(FileA, 20, 30) },
            second: new[] { new FR(FileA, 8, 22) });
        Assert.Equal(new[] { (8, 10), (20, 22) }, pieces.Select(p => (p.Start, p.End)));
    }

    [Fact]
    public void Intersections_NoOverlap_Empty()
        => Assert.Empty(OverlapAnalysis.Intersections(
            new[] { new FR(FileA, 1, 5) }, new[] { new FR(FileA, 6, 9) }));

    [Fact]
    public void Intersections_DifferentFiles_Empty()
        => Assert.Empty(OverlapAnalysis.Intersections(
            new[] { new FR(FileA, 1, 10) }, new[] { new FR(FileB, 1, 10) }));

    // --- KeepFirstOccupiers («Убрать пересекающиеся») ---

    [Fact]
    public void KeepFirstOccupiers_EarlierRangeWins()
    {
        var kept = OverlapAnalysis.KeepFirstOccupiers(
            new[] { new FR(FileA, 1, 10), new FR(FileA, 5, 20), new FR(FileA, 30, 40) });
        Assert.Equal(new[] { (1, 10), (30, 40) }, kept.Select(k => (k.Start, k.End)));
    }

    [Fact]
    public void KeepFirstOccupiers_ChainOfOverlaps_KeepsOnlyNonConflicting()
    {
        // (5,20) выброшен из-за (1,10); (15,25) НЕ пересекается с оставленным (1,10) — остаётся.
        var kept = OverlapAnalysis.KeepFirstOccupiers(
            new[] { new FR(FileA, 1, 10), new FR(FileA, 5, 20), new FR(FileA, 15, 25) });
        Assert.Equal(new[] { (1, 10), (15, 25) }, kept.Select(k => (k.Start, k.End)));
    }

    [Fact]
    public void KeepFirstOccupiers_PreservesOrder()
    {
        var kept = OverlapAnalysis.KeepFirstOccupiers(
            new[] { new FR(FileA, 30, 40), new FR(FileA, 1, 10), new FR(FileA, 35, 45) });
        Assert.Equal(new[] { (30, 40), (1, 10) }, kept.Select(k => (k.Start, k.End)));
    }

    [Fact]
    public void KeepFirstOccupiers_Empty_ReturnsEmpty()
        => Assert.Empty(OverlapAnalysis.KeepFirstOccupiers(System.Array.Empty<FR>()));

    [Fact]
    public void KeepFirstOccupiers_SamePagesDifferentFiles_BothKept()
    {
        // Файл A стр. 1–10 и файл B стр. 1–10 — не конфликт, оба остаются.
        var kept = OverlapAnalysis.KeepFirstOccupiers(new[] { new FR(FileA, 1, 10), new FR(FileB, 1, 10) });
        Assert.Equal(2, kept.Count);
    }

    // --- ResolveOverlapsPerFile («Подтвердить») ---

    [Fact]
    public void ResolveOverlapsPerFile_TrimsWithinEachFileIndependently()
    {
        var resolved = OverlapAnalysis.ResolveOverlapsPerFile(
            new[] { new FR(FileA, 1, 10), new FR(FileA, 5, 15), new FR(FileB, 1, 10) });

        var a = resolved.Where(r => r.File == FileA).Select(r => (r.Start, r.End)).ToList();
        var b = resolved.Where(r => r.File == FileB).Select(r => (r.Start, r.End)).ToList();
        Assert.Equal(new[] { (1, 10), (11, 15) }, a); // второй диапазон файла A обрезан
        Assert.Equal(new[] { (1, 10) }, b);           // файл B не тронут пересечением из файла A
    }

    [Fact]
    public void ResolveOverlapsPerFile_Empty_ReturnsEmpty()
        => Assert.Empty(OverlapAnalysis.ResolveOverlapsPerFile(System.Array.Empty<FR>()));
}
