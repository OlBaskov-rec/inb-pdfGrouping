using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PdfGrouping.Core.Services;

namespace PdfGrouping.Desktop.Services;

/// <summary>Преобразование растеризованной страницы (BGRA) в Avalonia-битмап.</summary>
public static class ImageHelper
{
    public static WriteableBitmap ToBitmap(RenderedPage page)
    {
        var bmp = new WriteableBitmap(
            new PixelSize(page.Width, page.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var fb = bmp.Lock();
        int srcStride = page.Width * 4;
        int dstStride = fb.RowBytes;

        if (srcStride == dstStride)
        {
            Marshal.Copy(page.Bgra, 0, fb.Address, page.Bgra.Length);
        }
        else
        {
            // Учитываем возможное выравнивание строк в кадровом буфере.
            // Min — страховка от записи за границу строки, если dstStride вдруг меньше srcStride.
            int rowBytes = Math.Min(srcStride, dstStride);
            for (int y = 0; y < page.Height; y++)
                Marshal.Copy(page.Bgra, y * srcStride, IntPtr.Add(fb.Address, y * dstStride), rowBytes);
        }

        return bmp;
    }
}
