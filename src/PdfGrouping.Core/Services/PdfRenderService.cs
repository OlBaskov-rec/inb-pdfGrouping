using Docnet.Core;
using Docnet.Core.Models;

namespace PdfGrouping.Core.Services;

/// <summary>Растеризованная страница: пиксели BGRA8888 + размеры.</summary>
public sealed record RenderedPage(byte[] Bgra, int Width, int Height);

/// <summary>
/// Рендер страниц PDF в растровое изображение через Docnet.Core (PDFium).
/// Кросс-платформенно (нативные библиотеки PDFium для win/linux/osx в пакете).
/// </summary>
public class PdfRenderService
{
    // DocLib.Instance — синглтон; доступ к рендеру сериализуем.
    private static readonly object Gate = new();

    /// <summary>
    /// Рендерит страницу (1-based) в BGRA, вписывая в рамку maxWidth×maxHeight с сохранением пропорций.
    /// Прозрачный фон заменяется на белый.
    /// </summary>
    public RenderedPage RenderPage(string pdfPath, int pageNumber, int maxWidth, int maxHeight)
    {
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException("PDF-файл не найден.", pdfPath);
        if (pageNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        lock (Gate)
        {
            using var docReader = DocLib.Instance.GetDocReader(
                pdfPath, new PageDimensions(maxWidth, maxHeight));

            using var pageReader = docReader.GetPageReader(pageNumber - 1);
            int w = pageReader.GetPageWidth();
            int h = pageReader.GetPageHeight();
            byte[] bgra = pageReader.GetImage();

            // PDFium отдаёт прозрачный фон там, где в документе нет заливки —
            // делаем непрозрачно-белым, чтобы миниатюра не была чёрной/прозрачной.
            for (int i = 0; i + 3 < bgra.Length; i += 4)
            {
                byte a = bgra[i + 3];
                if (a == 0)
                {
                    bgra[i] = 255; bgra[i + 1] = 255; bgra[i + 2] = 255; bgra[i + 3] = 255;
                }
                else if (a < 255)
                {
                    // премультипликация к белому фону
                    bgra[i]     = (byte)(bgra[i]     + (255 - a));
                    bgra[i + 1] = (byte)(bgra[i + 1] + (255 - a));
                    bgra[i + 2] = (byte)(bgra[i + 2] + (255 - a));
                    bgra[i + 3] = 255;
                }
            }

            return new RenderedPage(bgra, w, h);
        }
    }
}
