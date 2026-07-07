using PdfGrouping.Core;
using Xunit;

namespace PdfGrouping.Core.Tests;

/// <summary>
/// Тесты логики решений о пересечениях. Раньше эта логика жила в модели представления (UI)
/// и проверялась только вручную; теперь она в Core и зафиксирована здесь как контракт.
/// </summary>
public class OverlapAnalysisTests
{
    private static readonly (string, int, int)[] NoGroups = System.Array.Empty<(string, int, int)>();

    // --- Analyze: обнаружение пересечений нового диапазона ---

    [Fact]
    public void Analyze_NoOverlaps_ReturnsEmpty()
    {
        var r = OverlapAnalysis.Analyze(30, 40, new[] { (1, 10), (50, 60) }, NoGroups);
        Assert.False(r.HasOverlaps);
        Assert.Empty(r.Hits);
        Assert.Empty(r.DupIntervals);
    }

    [Fact]
    public void Analyze_OverlapWithCurrentRange_ReportsHitWithoutLabel()
    {
        var r = OverlapAnalysis.Analyze(5, 15, new[] { (10, 20) }, NoGroups);

        var hit = Assert.Single(r.Hits);
        Assert.Null(hit.GroupLabel);              // источник — текущие диапазоны
        Assert.Equal((10, 20), (hit.ExistingStart, hit.ExistingEnd));
        Assert.Equal((10, 15), (hit.DupStart, hit.DupEnd)); // дублируется только общая часть
    }

    [Fact]
    public void Analyze_OverlapWithGroup_ReportsLabel()
    {
        var r = OverlapAnalysis.Analyze(5, 15, new (int, int)[0], new[] { ("A", 1, 7) });

        var hit = Assert.Single(r.Hits);
        Assert.Equal("A", hit.GroupLabel);
        Assert.Equal((5, 7), (hit.DupStart, hit.DupEnd));
    }

    [Fact]
    public void Analyze_CurrentRangesComeBeforeGroups()
    {
        var r = OverlapAnalysis.Analyze(1, 100, new[] { (10, 20) }, new[] { ("A", 30, 40) });

        Assert.Equal(2, r.Hits.Count);
        Assert.Null(r.Hits[0].GroupLabel);
        Assert.Equal("A", r.Hits[1].GroupLabel);
        Assert.Equal(new[] { (10, 20), (30, 40) }, r.DupIntervals);
    }

    [Fact]
    public void Analyze_TouchingButNotOverlapping_IsNotHit()
    {
        // Диапазоны 1–10 и 11–20 смежные, но НЕ пересекаются — дубликатов нет.
        var r = OverlapAnalysis.Analyze(11, 20, new[] { (1, 10) }, NoGroups);
        Assert.False(r.HasOverlaps);
    }

    [Fact]
    public void Analyze_SinglePageOverlap_IsDetected()
    {
        var r = OverlapAnalysis.Analyze(10, 10, new[] { (10, 10) }, NoGroups);
        var hit = Assert.Single(r.Hits);
        Assert.Equal((10, 10), (hit.DupStart, hit.DupEnd));
    }

    [Fact]
    public void Analyze_NewRangeInsideExisting_DupIsWholeNewRange()
    {
        var r = OverlapAnalysis.Analyze(12, 14, new[] { (10, 20) }, NoGroups);
        Assert.Equal((12, 14), r.DupIntervals[0]);
    }

    [Fact]
    public void Analyze_ExistingInsideNewRange_DupIsWholeExisting()
    {
        var r = OverlapAnalysis.Analyze(1, 100, new[] { (40, 45) }, NoGroups);
        Assert.Equal((40, 45), r.DupIntervals[0]);
    }

    // --- HasInternalOverlaps / InternalIntersections ---

    [Fact]
    public void HasInternalOverlaps_Disjoint_False()
        => Assert.False(OverlapAnalysis.HasInternalOverlaps(new[] { (1, 10), (11, 20), (30, 40) }));

    [Fact]
    public void HasInternalOverlaps_Overlapping_True()
        => Assert.True(OverlapAnalysis.HasInternalOverlaps(new[] { (1, 10), (5, 20) }));

    [Fact]
    public void InternalIntersections_ReturnsPairwisePieces()
    {
        var pieces = OverlapAnalysis.InternalIntersections(new[] { (1, 10), (5, 20), (8, 9) });
        // (1,10)×(5,20)=(5,10); (1,10)×(8,9)=(8,9); (5,20)×(8,9)=(8,9)
        Assert.Equal(new[] { (5, 10), (8, 9), (8, 9) }, pieces);
    }

    // --- Intersections двух наборов ---

    [Fact]
    public void Intersections_FindsCrossSetPieces()
    {
        var pieces = OverlapAnalysis.Intersections(
            first: new[] { (1, 10), (20, 30) },
            second: new[] { (8, 22) });
        Assert.Equal(new[] { (8, 10), (20, 22) }, pieces);
    }

    [Fact]
    public void Intersections_NoOverlap_Empty()
        => Assert.Empty(OverlapAnalysis.Intersections(new[] { (1, 5) }, new[] { (6, 9) }));

    // --- KeepFirstOccupiers («Убрать пересекающиеся») ---

    [Fact]
    public void KeepFirstOccupiers_EarlierRangeWins()
    {
        var kept = OverlapAnalysis.KeepFirstOccupiers(new[] { (1, 10), (5, 20), (30, 40) });
        Assert.Equal(new[] { (1, 10), (30, 40) }, kept);
    }

    [Fact]
    public void KeepFirstOccupiers_ChainOfOverlaps_KeepsOnlyNonConflicting()
    {
        // (5,20) выброшен из-за (1,10); (15,25) НЕ пересекается с оставленным (1,10) — остаётся.
        var kept = OverlapAnalysis.KeepFirstOccupiers(new[] { (1, 10), (5, 20), (15, 25) });
        Assert.Equal(new[] { (1, 10), (15, 25) }, kept);
    }

    [Fact]
    public void KeepFirstOccupiers_PreservesOrder()
    {
        var kept = OverlapAnalysis.KeepFirstOccupiers(new[] { (30, 40), (1, 10), (35, 45) });
        Assert.Equal(new[] { (30, 40), (1, 10) }, kept);
    }

    [Fact]
    public void KeepFirstOccupiers_Empty_ReturnsEmpty()
        => Assert.Empty(OverlapAnalysis.KeepFirstOccupiers(System.Array.Empty<(int, int)>()));
}
