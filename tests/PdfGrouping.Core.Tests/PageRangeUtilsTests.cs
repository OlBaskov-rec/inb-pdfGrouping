using PdfGrouping.Core;
using Xunit;

namespace PdfGrouping.Core.Tests;

public class PageRangeUtilsTests
{
    [Fact]
    public void Merge_Empty_ReturnsEmpty()
        => Assert.Equal("", PageRangeUtils.MergeToString(new (int, int)[0]));

    [Fact]
    public void Merge_SinglePage_NoDash()
        => Assert.Equal("5", PageRangeUtils.MergeToString(new[] { (5, 5) }));

    [Fact]
    public void Merge_OverlappingAndAdjacent_AreCombined()
    {
        var s = PageRangeUtils.MergeToString(new[] { (10, 20), (15, 23), (24, 25) });
        Assert.Equal("10–25", s);
    }

    [Fact]
    public void Merge_DisjointSorted()
    {
        var s = PageRangeUtils.MergeToString(new[] { (40, 42), (10, 12), (30, 30) });
        Assert.Equal("10–12, 30, 40–42", s);
    }

    [Fact]
    public void Merge_IgnoresInvalid()
        => Assert.Equal("3–4", PageRangeUtils.MergeToString(new[] { (5, 1), (3, 4) }));
}
