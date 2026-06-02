namespace PdfGrouping.Desktop.Services;

/// <summary>
/// Абстракция выбора файла/папки, чтобы ViewModel не зависела от Avalonia напрямую.
/// </summary>
public interface IFilePickerService
{
    Task<string?> PickPdfAsync();
    Task<string?> PickFolderAsync();
}
