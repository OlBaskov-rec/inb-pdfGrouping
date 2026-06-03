using PdfGrouping.Core.Services;
using PdfSharp.Fonts;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace PdfGrouping.Core.Tests;

public class PdfRenderServiceTests : IDisposable
{
    private readonly string _workDir;
    private readonly PdfRenderService _service = new();

    public PdfRenderServiceTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "pdfgrouping_render_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch { }
    }

    private string CreateSamplePdf(int pages)
    {
        string path = Path.Combine(_workDir, "sample.pdf");
        using var doc = new PdfDocument();
        for (int i = 0; i < pages; i++)
            doc.AddPage(); // пустые страницы — для рендера достаточно
        doc.Save(path);
        return path;
    }

    [Fact]
    public void RenderPage_ReturnsBgraOfExpectedSize()
    {
        string pdf = CreateSamplePdf(3);

        var page = _service.RenderPage(pdf, 1, 300, 800);

        Assert.True(page.Width > 0 && page.Width <= 300);
        Assert.True(page.Height > 0 && page.Height <= 800);
        Assert.Equal(page.Width * page.Height * 4, page.Bgra.Length);
        // фон сделан непрозрачным
        Assert.Equal(255, page.Bgra[3]);
    }

    [Fact]
    public void RenderPage_InvalidPath_Throws()
    {
        Assert.Throws<FileNotFoundException>(
            () => _service.RenderPage(Path.Combine(_workDir, "nope.pdf"), 1, 100, 100));
    }
}
