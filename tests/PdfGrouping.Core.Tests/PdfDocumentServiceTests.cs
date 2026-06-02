using PdfGrouping.Core.Models;
using PdfGrouping.Core.Services;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfGrouping.Core.Tests;

public class PdfDocumentServiceTests : IDisposable
{
    private readonly string _workDir;
    private readonly PdfDocumentService _service = new();

    public PdfDocumentServiceTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "pdfgrouping_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>Создаёт PDF с заданным числом пустых страниц и возвращает путь к нему.</summary>
    private string CreateSamplePdf(int pageCount, string name = "sample.pdf")
    {
        string path = Path.Combine(_workDir, name);
        using var doc = new PdfDocument();
        for (int i = 0; i < pageCount; i++)
            doc.AddPage();
        doc.Save(path);
        return path;
    }

    private static int PageCountOf(string path)
    {
        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }

    private static PdfGroup Group(string label, params (int start, int end)[] ranges)
    {
        var g = new PdfGroup { Label = label };
        foreach (var (s, e) in ranges)
            g.Ranges.Add(new PageRange { StartPage = s, EndPage = e });
        return g;
    }

    [Fact]
    public void GetPageCount_ReturnsCorrectCount()
    {
        string pdf = CreateSamplePdf(10);
        Assert.Equal(10, _service.GetPageCount(pdf));
    }

    [Fact]
    public void SplitAndGroup_ProducesOneFilePerGroup_WithExpectedPageCounts()
    {
        string pdf = CreateSamplePdf(50);
        var groups = new List<PdfGroup>
        {
            Group("A", (1, 10), (25, 30)), // 16 страниц
            Group("B", (11, 24)),          // 14 страниц
            Group("C", (31, 50)),          // 20 страниц
        };

        var outputs = _service.SplitAndGroup(pdf, groups, _workDir);

        Assert.Equal(3, outputs.Count);
        Assert.All(outputs, f => Assert.True(File.Exists(f)));
        Assert.Equal(16, PageCountOf(outputs[0]));
        Assert.Equal(14, PageCountOf(outputs[1]));
        Assert.Equal(20, PageCountOf(outputs[2]));
        Assert.EndsWith("A.pdf", outputs[0]);
        Assert.EndsWith("B.pdf", outputs[1]);
        Assert.EndsWith("C.pdf", outputs[2]);
    }

    [Fact]
    public void SplitAndGroup_DuplicateLabels_GetUniqueFileNames()
    {
        string pdf = CreateSamplePdf(5);
        var groups = new List<PdfGroup>
        {
            Group("X", (1, 2)),
            Group("X", (3, 5)),
        };

        var outputs = _service.SplitAndGroup(pdf, groups, _workDir);

        Assert.Equal(2, outputs.Count);
        Assert.NotEqual(outputs[0], outputs[1]);
        Assert.All(outputs, f => Assert.True(File.Exists(f)));
    }

    [Fact]
    public void SplitAndGroup_PageOutOfRange_Throws()
    {
        string pdf = CreateSamplePdf(5);
        var groups = new List<PdfGroup> { Group("A", (1, 99)) };

        var ex = Assert.Throws<ArgumentException>(() => _service.SplitAndGroup(pdf, groups, _workDir));
        Assert.Contains("вне диапазона", ex.Message);
    }

    [Fact]
    public void SplitAndGroup_StartGreaterThanEnd_Throws()
    {
        string pdf = CreateSamplePdf(5);
        var groups = new List<PdfGroup> { Group("A", (4, 2)) };

        Assert.Throws<ArgumentException>(() => _service.SplitAndGroup(pdf, groups, _workDir));
    }

    [Fact]
    public void SplitAndGroup_EmptyGroups_Throws()
    {
        string pdf = CreateSamplePdf(5);
        Assert.Throws<ArgumentException>(() => _service.SplitAndGroup(pdf, new List<PdfGroup>(), _workDir));
    }

    [Fact]
    public void SplitAndGroup_MissingInputFile_Throws()
    {
        var groups = new List<PdfGroup> { Group("A", (1, 1)) };
        Assert.Throws<FileNotFoundException>(
            () => _service.SplitAndGroup(Path.Combine(_workDir, "nope.pdf"), groups, _workDir));
    }
}
