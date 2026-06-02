namespace PdfGrouping.Core.Models;

/// <summary>
/// Непрерывный диапазон страниц [StartPage..EndPage] (1-based, включительно).
/// </summary>
public class PageRange
{
    public int StartPage { get; set; } = 1;
    public int EndPage { get; set; } = 1;

    public int PageCount => EndPage >= StartPage ? EndPage - StartPage + 1 : 0;

    public override string ToString() => $"{StartPage}–{EndPage}";
}
