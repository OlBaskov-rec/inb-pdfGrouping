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

    public async Task<IReadOnlyList<string>> PickPdfsAsync()
    {
        var top = _topLevel();
        if (top is null) return Array.Empty<string>();

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите PDF-файлы",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PDF документы") { Patterns = new[] { "*.pdf" } },
                FilePickerFileTypes.All,
            },
        });

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
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
