using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PdfGrouping.Desktop.Services;

/// <summary>
/// Реализация выбора файла/папки на кросс-платформенном Avalonia StorageProvider.
/// </summary>
public class StorageProviderFilePicker : IFilePickerService
{
    private readonly Func<TopLevel?> _topLevel;

    public StorageProviderFilePicker(Func<TopLevel?> topLevel) => _topLevel = topLevel;

    public async Task<string?> PickPdfAsync()
    {
        var top = _topLevel();
        if (top is null) return null;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите PDF файл",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PDF документы") { Patterns = new[] { "*.pdf" } },
                FilePickerFileTypes.All,
            },
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync()
    {
        var top = _topLevel();
        if (top is null) return null;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите папку для сохранения результатов",
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
