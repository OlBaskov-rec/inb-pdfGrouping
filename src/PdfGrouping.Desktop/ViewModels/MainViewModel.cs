using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfGrouping.Core.Models;
using PdfGrouping.Core.Services;
using PdfGrouping.Desktop.Services;

namespace PdfGrouping.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly PdfDocumentService _pdfService = new();
    private readonly IFilePickerService _filePicker;
    private readonly UpdateService _updateService;

    public MainViewModel(IFilePickerService filePicker, UpdateService updateService)
    {
        _filePicker = filePicker;
        _updateService = updateService;
    }

    // --- Исходный PDF ---
    [ObservableProperty]
    private string _sourceFilePath = string.Empty;

    [ObservableProperty]
    private int _totalPages;

    [ObservableProperty]
    private string _statusText = "Готов к работе";

    [ObservableProperty]
    private bool _statusIsError;

    // --- Диапазоны (до группировки) ---
    public ObservableCollection<PageRange> Ranges { get; } = new();

    [ObservableProperty]
    private string _rangesDisplayText = "(пусто)";

    // --- Группы ---
    public ObservableCollection<PdfGroup> Groups { get; } = new();

    // --- Ввод диапазона ---
    [ObservableProperty]
    private string _rangeStartText = "1";

    [ObservableProperty]
    private string _rangeEndText = "1";

    // --- Метка группы ---
    [ObservableProperty]
    private string _groupLabelText = string.Empty;

    // --- Папка вывода ---
    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    // --- Состояние обработки ---
    [ObservableProperty]
    private bool _isProcessing;

    // --- Результаты последней обработки ---
    public ObservableCollection<string> OutputFiles { get; } = new();

    [ObservableProperty]
    private bool _hasResults;

    // --- Обновления (Velopack) ---
    [ObservableProperty]
    private bool _isUpdateReady;

    [ObservableProperty]
    private string _updateText = string.Empty;

    // -------------------------------------------------------------------
    // КОМАНДЫ
    // -------------------------------------------------------------------

    [RelayCommand]
    private async Task BrowseSourceFileAsync()
    {
        var path = await _filePicker.PickPdfAsync();
        if (!string.IsNullOrEmpty(path))
        {
            SourceFilePath = path;
            OutputDirectory = Path.GetDirectoryName(path) ?? string.Empty;
            LoadPdfInfo();
        }
    }

    [RelayCommand]
    private async Task BrowseOutputDirectoryAsync()
    {
        var path = await _filePicker.PickFolderAsync();
        if (!string.IsNullOrEmpty(path))
            OutputDirectory = path;
    }

    [RelayCommand]
    private void AddRange()
    {
        if (TotalPages <= 0)
        {
            SetError("Сначала откройте PDF-файл.");
            return;
        }

        if (!int.TryParse(RangeStartText, out int start) || !int.TryParse(RangeEndText, out int end))
        {
            SetError("Введите корректные номера страниц (целые числа).");
            return;
        }

        if (start < 1 || end < 1 || start > TotalPages || end > TotalPages)
        {
            SetError($"Номера страниц должны быть от 1 до {TotalPages}.");
            return;
        }

        if (start > end)
        {
            SetError("Начальная страница не может быть больше конечной.");
            return;
        }

        // Предупреждение о пересечении с уже добавленными диапазонами (не блокируем).
        var overlapping = Ranges.FirstOrDefault(r => start <= r.EndPage && end >= r.StartPage);

        var range = new PageRange { StartPage = start, EndPage = end };
        Ranges.Add(range);
        UpdateRangesDisplay();

        // Подготовить следующий ввод
        RangeStartText = (end + 1 <= TotalPages ? end + 1 : TotalPages).ToString();
        RangeEndText = TotalPages.ToString();

        if (overlapping != null)
            SetWarning($"Диапазон {range} пересекается с {overlapping} — страницы продублируются.");
        else
            SetInfo($"Добавлен диапазон: {range}");
    }

    [RelayCommand]
    private void RemoveRange(PageRange? range)
    {
        if (range == null) return;
        Ranges.Remove(range);
        UpdateRangesDisplay();
        SetInfo($"Диапазон {range} убран");
    }

    [RelayCommand]
    private void ClearRanges()
    {
        Ranges.Clear();
        UpdateRangesDisplay();
        SetInfo("Диапазоны очищены");
    }

    [RelayCommand]
    private void AddGroup()
    {
        string label = GroupLabelText?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(label))
        {
            SetError("Введите метку группы (букву или номер).");
            return;
        }

        if (Ranges.Count == 0)
        {
            SetError("Сначала добавьте диапазоны страниц.");
            return;
        }

        if (Groups.Any(g => string.Equals(g.Label, label, StringComparison.OrdinalIgnoreCase)))
        {
            SetError($"Группа с меткой «{label}» уже существует. Выберите другую метку.");
            return;
        }

        var group = new PdfGroup { Label = label };
        foreach (var r in Ranges)
            group.Ranges.Add(new PageRange { StartPage = r.StartPage, EndPage = r.EndPage });

        Groups.Add(group);
        SetInfo($"Группа «{label}» добавлена ({group.TotalPages} стр.)");

        // Готовимся к следующей группе
        Ranges.Clear();
        UpdateRangesDisplay();
        GroupLabelText = string.Empty;
    }

    [RelayCommand]
    private void RemoveGroup(PdfGroup? group)
    {
        if (group != null)
            Groups.Remove(group);
    }

    [RelayCommand]
    private async Task ProcessAsync()
    {
        if (Groups.Count == 0)
        {
            SetError("Нет групп для обработки. Добавьте хотя бы одну группу.");
            return;
        }

        if (string.IsNullOrEmpty(OutputDirectory))
        {
            SetError("Выберите папку для сохранения результатов.");
            return;
        }

        IsProcessing = true;
        HasResults = false;
        OutputFiles.Clear();
        SetInfo("Обработка…");

        try
        {
            var groupsList = Groups.ToList();
            var source = SourceFilePath;
            var outDir = OutputDirectory;

            var produced = await Task.Run(() => _pdfService.SplitAndGroup(source, groupsList, outDir));

            foreach (var f in produced)
                OutputFiles.Add(f);

            HasResults = produced.Count > 0;
            SetInfo($"Готово! Создано файлов: {produced.Count}");
        }
        catch (Exception ex)
        {
            SetError($"Ошибка при обработке: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (!string.IsNullOrEmpty(OutputDirectory))
            PlatformHelper.OpenFolder(OutputDirectory);
    }

    [RelayCommand]
    private void ApplyUpdate() => _updateService.ApplyAndRestart();

    /// <summary>
    /// Фоновая проверка обновлений при старте. В dev-запуске — безопасный no-op.
    /// Ошибки сети не мешают работе приложения.
    /// </summary>
    public async Task CheckForUpdatesAsync()
    {
        try
        {
            if (!_updateService.IsSupported)
                return;

            var version = await _updateService.CheckAsync();
            if (version is null)
                return;

            await _updateService.DownloadAsync();
            UpdateText = $"Доступно обновление {version} — готово к установке.";
            IsUpdateReady = true;
        }
        catch
        {
            // Обновление не критично для основной работы — тихо игнорируем.
        }
    }

    [RelayCommand]
    private void ClearAll()
    {
        SourceFilePath = string.Empty;
        TotalPages = 0;
        Ranges.Clear();
        Groups.Clear();
        OutputFiles.Clear();
        HasResults = false;
        UpdateRangesDisplay();
        GroupLabelText = string.Empty;
        OutputDirectory = string.Empty;
        RangeStartText = "1";
        RangeEndText = "1";
        SetInfo("Готов к работе");
    }

    // -------------------------------------------------------------------
    // ВСПОМОГАТЕЛЬНОЕ
    // -------------------------------------------------------------------

    /// <summary>Загрузка PDF по пути (используется кнопкой и drag&drop).</summary>
    public void LoadFromPath(string path)
    {
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            SetError("Пожалуйста, выберите PDF-файл.");
            return;
        }

        SourceFilePath = path;
        OutputDirectory = Path.GetDirectoryName(path) ?? string.Empty;
        LoadPdfInfo();
    }

    private void LoadPdfInfo()
    {
        try
        {
            TotalPages = _pdfService.GetPageCount(SourceFilePath);
            RangeStartText = "1";
            RangeEndText = TotalPages.ToString();
            SetInfo($"Загружен: {Path.GetFileName(SourceFilePath)} ({TotalPages} стр.)");
        }
        catch (Exception ex)
        {
            TotalPages = 0;
            SetError($"Не удалось прочитать PDF: {ex.Message}");
        }
    }

    private void UpdateRangesDisplay()
    {
        RangesDisplayText = Ranges.Count == 0
            ? "(пусто)"
            : string.Join(", ", Ranges.Select(r => r.ToString()));
    }

    private void SetInfo(string text)
    {
        StatusIsError = false;
        StatusText = text;
    }

    private void SetError(string text)
    {
        StatusIsError = true;
        StatusText = text;
    }

    private void SetWarning(string text)
    {
        // Действие выполнено, но требует внимания — помечаем значком, не красным.
        StatusIsError = false;
        StatusText = "⚠ " + text;
    }
}
