namespace PdfGrouping.Core.Models;

/// <summary>
/// Непрерывный диапазон страниц [StartPage..EndPage] (1-based, включительно) в конкретном
/// исходном файле <see cref="SourceFile"/>. Диапазоны из РАЗНЫХ файлов можно свободно объединять
/// в одну группу (один выходной PDF) — см. <see cref="PdfGroup"/>; страница 5 файла A и страница 5
/// файла B не считаются «одной и той же» страницей нигде в логике пересечений.
/// </summary>
public class PageRange
{
    public int StartPage { get; set; } = 1;
    public int EndPage { get; set; } = 1;

    /// <summary>Полный путь к исходному PDF, из которого взят этот диапазон.</summary>
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>Короткое имя исходного файла — для отображения в UI (напр. «отчёт.pdf»).</summary>
    public string FileName => System.IO.Path.GetFileName(SourceFile);

    public int PageCount => EndPage >= StartPage ? EndPage - StartPage + 1 : 0;

    public override string ToString() => $"{StartPage}–{EndPage}";
}
