using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfGrouping.Desktop.Services;

namespace PdfGrouping.Desktop.ViewModels;

/// <summary>
/// Один загруженный исходный файл в списке слева. Наблюдаемый объект — чтобы колонка «№»
/// (порядковый номер) в UI обновлялась «на лету», если файл удалили из середины списка и
/// последующие сдвинулись.
/// </summary>
public sealed partial class SourceFileEntry : ObservableObject
{
    public required string FilePath { get; init; }
    public required int PageCount { get; init; }

    /// <summary>Порядковый номер в списке (1-based, не редактируется пользователем).</summary>
    [ObservableProperty]
    private int _number;

    /// <summary>Короткое имя для отображения в списке.</summary>
    public string FileName => System.IO.Path.GetFileName(FilePath);
}

/// <summary>
/// Список исходных PDF для обработки (панель слева). Пользователь добавляет один или несколько
/// файлов; клик по строке делает файл «активным» — диапазоны страниц (справа) берутся из него,
/// как и раньше для единственного файла. Диапазоны из РАЗНЫХ файлов можно объединять в одну
/// группу — один выходной PDF будет собран из страниц нескольких исходников.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Загруженные исходные файлы.</summary>
    public ObservableCollection<SourceFileEntry> SourceFiles { get; } = new();

    /// <summary>Активный файл: диапазоны страниц добавляются из него.</summary>
    [ObservableProperty]
    private SourceFileEntry? _selectedSourceFile;

    /// <summary>
    /// Показывать ли у каждого диапазона метку файла-источника: только когда файлов больше
    /// одного — иначе это лишняя информация в самом частом (один файл) сценарии.
    /// </summary>
    public bool ShowFileTags => SourceFiles.Count > 1;

    [RelayCommand]
    private async Task AddSourceFilesAsync()
    {
        var paths = await _filePicker.PickPdfsAsync();
        if (paths.Count > 0)
            AddSourceFiles(paths);
    }

    /// <summary>
    /// Добавляет файлы в список (пропуская не-PDF); уже загруженный файл не дублируется —
    /// становится активным. Последний добавленный/выбранный файл становится активным.
    /// </summary>
    public void AddSourceFiles(IEnumerable<string> paths)
    {
        SourceFileEntry? lastTouched = null;

        foreach (var path in paths)
        {
            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                SetError(L["Err_SelectPdf"]);
                continue;
            }

            var already = SourceFiles.FirstOrDefault(
                f => string.Equals(f.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (already != null)
            {
                lastTouched = already;
                continue;
            }

            try
            {
                int pageCount = _pdfService.GetPageCount(path);
                var entry = new SourceFileEntry { FilePath = path, PageCount = pageCount };
                SourceFiles.Add(entry);
                lastTouched = entry;

                if (string.IsNullOrEmpty(OutputDirectory))
                    OutputDirectory = Path.GetDirectoryName(path) ?? string.Empty;
                // Если групп ещё нет — подставим первую метку «A» (можно изменить).
                if (Groups.Count == 0 && string.IsNullOrEmpty(GroupLabelText))
                    GroupLabelText = "A";
            }
            catch (Exception ex)
            {
                AppLog.Error($"Не удалось прочитать PDF «{path}»", ex);
                SetError(L.Format("Err_ReadPdf", ex.Message));
            }
        }

        if (lastTouched != null)
            SelectedSourceFile = lastTouched;
    }

    /// <summary>
    /// При смене активного файла — сброс полей ввода диапазона на «весь файл» (1..N), как и при
    /// загрузке единственного файла раньше: если пользователь ничего не поменяет и сразу нажмёт
    /// «Добавить диапазон», в диапазон войдёт файл целиком.
    /// </summary>
    partial void OnSelectedSourceFileChanged(SourceFileEntry? value)
    {
        SourceFilePath = value?.FilePath ?? string.Empty;
        TotalPages = value?.PageCount ?? 0;
        RangeStart = 1;
        RangeEnd = TotalPages > 0 ? TotalPages : 1;
        if (value != null)
            SetInfo(L.Format("Msg_Loaded", value.FileName, value.PageCount));
    }

    [RelayCommand]
    private void RemoveSourceFile(SourceFileEntry? entry)
    {
        if (entry is null) return;

        SourceFiles.Remove(entry);
        if (ReferenceEquals(SelectedSourceFile, entry))
            SelectedSourceFile = SourceFiles.FirstOrDefault();

        // Диапазоны/группы, уже созданные по этому файлу, НЕ удаляются: SourceFile хранится
        // в самом диапазоне и не зависит от того, остался ли файл в списке слева.
    }

    /// <summary>Пересчитывает «№» всех файлов по текущему порядку в списке (после Add/Remove).</summary>
    private void RenumberSourceFiles()
    {
        for (int i = 0; i < SourceFiles.Count; i++)
            SourceFiles[i].Number = i + 1;
    }

    /// <summary>
    /// Номер файла-источника (для метки «[N]» у диапазона) на МОМЕНТ вызова. Если файл потом
    /// уберут из списка/список перенумеруется — уже добавленные диапазоны сохраняют старый номер
    /// как снимок на момент добавления (как и сам SourceFile — не «живая» ссылка).
    /// </summary>
    private int FileNumberFor(string sourceFile) =>
        SourceFiles.FirstOrDefault(f => f.FilePath == sourceFile)?.Number ?? 0;
}
