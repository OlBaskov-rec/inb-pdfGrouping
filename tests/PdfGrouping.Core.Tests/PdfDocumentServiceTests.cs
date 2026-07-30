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

    /// <summary>Группа, все диапазоны которой берутся из ОДНОГО файла (типичный сценарий).</summary>
    private static PdfGroup Group(string label, string sourceFile, params (int start, int end)[] ranges)
    {
        var g = new PdfGroup { Label = label };
        foreach (var (s, e) in ranges)
            g.Ranges.Add(new PageRange { StartPage = s, EndPage = e, SourceFile = sourceFile });
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
            Group("A", pdf, (1, 10), (25, 30)), // 16 страниц
            Group("B", pdf, (11, 24)),          // 14 страниц
            Group("C", pdf, (31, 50)),          // 20 страниц
        };

        var outputs = _service.SplitAndGroup(groups, _workDir);

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
            Group("X", pdf, (1, 2)),
            Group("X", pdf, (3, 5)),
        };

        var outputs = _service.SplitAndGroup(groups, _workDir);

        Assert.Equal(2, outputs.Count);
        Assert.NotEqual(outputs[0], outputs[1]);
        Assert.All(outputs, f => Assert.True(File.Exists(f)));
    }

    [Fact]
    public void SplitAndGroup_ExistingFileOnDisk_GetsIndexedName()
    {
        string pdf = CreateSamplePdf(5);
        // Заранее кладём файл «A.pdf» в выходную папку.
        File.WriteAllText(Path.Combine(_workDir, "A.pdf"), "existing");
        var groups = new List<PdfGroup> { Group("A", pdf, (1, 3)) };

        var outputs = _service.SplitAndGroup(groups, _workDir);

        Assert.Single(outputs);
        Assert.EndsWith("A (1).pdf", outputs[0]);     // авто-индекс
        Assert.Equal(3, PageCountOf(outputs[0]));
        // Существующий файл не перезаписан.
        Assert.Equal("existing", File.ReadAllText(Path.Combine(_workDir, "A.pdf")));
    }

    [Fact]
    public void SplitAndGroup_PageOutOfRange_Throws()
    {
        string pdf = CreateSamplePdf(5);
        var groups = new List<PdfGroup> { Group("A", pdf, (1, 99)) };

        var ex = Assert.Throws<ArgumentException>(() => _service.SplitAndGroup(groups, _workDir));
        Assert.Contains("вне диапазона", ex.Message);
    }

    [Fact]
    public void SplitAndGroup_StartGreaterThanEnd_Throws()
    {
        string pdf = CreateSamplePdf(5);
        var groups = new List<PdfGroup> { Group("A", pdf, (4, 2)) };

        Assert.Throws<ArgumentException>(() => _service.SplitAndGroup(groups, _workDir));
    }

    [Fact]
    public void SplitAndGroup_EmptyGroups_Throws()
    {
        Assert.Throws<ArgumentException>(() => _service.SplitAndGroup(new List<PdfGroup>(), _workDir));
    }

    [Fact]
    public void SplitAndGroup_MissingInputFile_Throws()
    {
        var groups = new List<PdfGroup> { Group("A", Path.Combine(_workDir, "nope.pdf"), (1, 1)) };
        Assert.Throws<FileNotFoundException>(() => _service.SplitAndGroup(groups, _workDir));
    }

    [Fact]
    public void SplitAndGroup_EmptySourceFile_OnRange_ThrowsFileNotFound()
    {
        var group = new PdfGroup { Label = "A" };
        group.Ranges.Add(new PageRange { StartPage = 1, EndPage = 2, SourceFile = string.Empty });
        Assert.Throws<FileNotFoundException>(() => _service.SplitAndGroup(new List<PdfGroup> { group }, _workDir));
    }

    // --- Объединение НЕСКОЛЬКИХ разных файлов в один выходной (ключевой сценарий фичи merge) ---

    [Fact]
    public void SplitAndGroup_RangesFromTwoDifferentFiles_MergeIntoOneOutput()
    {
        string pdfA = CreateSamplePdf(10, "book1.pdf");
        string pdfB = CreateSamplePdf(20, "book2.pdf");

        var group = new PdfGroup { Label = "Merged" };
        group.Ranges.Add(new PageRange { StartPage = 1, EndPage = 3, SourceFile = pdfA });  // 3 стр. из A
        group.Ranges.Add(new PageRange { StartPage = 5, EndPage = 9, SourceFile = pdfB });  // 5 стр. из B

        var outputs = _service.SplitAndGroup(new List<PdfGroup> { group }, _workDir);

        Assert.Single(outputs);
        Assert.Equal(8, PageCountOf(outputs[0])); // 3 + 5 = 8 страниц в одном файле
    }

    [Fact]
    public void SplitAndGroup_SamePageNumberDifferentFiles_BothValid_NoConflict()
    {
        // Страница 1–5 файла A и страница 1–5 файла B — разные страницы, оба диапазона валидны
        // одновременно (движок не путает страницы разных исходников).
        string pdfA = CreateSamplePdf(5, "x.pdf");
        string pdfB = CreateSamplePdf(5, "y.pdf");

        var group = new PdfGroup { Label = "Both" };
        group.Ranges.Add(new PageRange { StartPage = 1, EndPage = 5, SourceFile = pdfA });
        group.Ranges.Add(new PageRange { StartPage = 1, EndPage = 5, SourceFile = pdfB });

        var outputs = _service.SplitAndGroup(new List<PdfGroup> { group }, _workDir);

        Assert.Single(outputs);
        Assert.Equal(10, PageCountOf(outputs[0]));
    }

    [Fact]
    public void SplitAndGroup_SharedSourceOpenedOnce_UsedAcrossMultipleGroups()
    {
        // Один и тот же исходник используется в двух разных группах — должен открыться один раз
        // и корректно обслужить обе (регрессия на кэш открытых источников).
        string pdf = CreateSamplePdf(30);
        var groups = new List<PdfGroup>
        {
            Group("First", pdf, (1, 10)),
            Group("Second", pdf, (11, 30)),
        };

        var outputs = _service.SplitAndGroup(groups, _workDir);

        Assert.Equal(2, outputs.Count);
        Assert.Equal(10, PageCountOf(outputs[0]));
        Assert.Equal(20, PageCountOf(outputs[1]));
    }
}
