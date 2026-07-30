namespace PdfGrouping.Desktop.Services;

/// <summary>
/// Абстракция выбора файла/папки, чтобы ViewModel не зависела от Avalonia напрямую.
/// </summary>
public interface IFilePickerService
{
    /// <summary>Выбор одного или нескольких PDF-файлов (для добавления в список источников).</summary>
    Task<IReadOnlyList<string>> PickPdfsAsync();

    Task<string?> PickFolderAsync();
}
