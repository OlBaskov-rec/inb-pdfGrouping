using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfGrouping.Core;
using PdfGrouping.Core.Models;
using PdfGrouping.Core.Services;
using PdfGrouping.Desktop.Services;

namespace PdfGrouping.Desktop.ViewModels;

/// <summary>Описание одного пересечения: добавленный диапазон vs уже выбранный диапазон.</summary>
/// <param name="Source">Источник пересечения, напр. «группа 12» или «текущие диапазоны».</param>
public record OverlapInfo(string NewPages, string ExistingPages, string Source, string Dup);

public partial class MainViewModel : ObservableObject
{
    private readonly PdfDocumentService _pdfService = new();
    private readonly PdfRenderService _renderService = new();
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

        // Пересечения: внутри текущей группы и со страницами уже созданных групп.
        var curOverlaps = Ranges
            .Where(r => start <= r.EndPage && end >= r.StartPage)
            .ToList();
        var prevOverlaps = Groups
            .SelectMany(g => g.Ranges.Select(r => (g.Label, Range: r)))
            .Where(x => start <= x.Range.EndPage && end >= x.Range.StartPage)
            .ToList();

        // Объединённый список дублируемых страниц (пересечения нового диапазона с уже выбранными).
        var dupIntervals = curOverlaps.Select(r => (Math.Max(start, r.StartPage), Math.Min(end, r.EndPage)))
            .Concat(prevOverlaps.Select(x => (Math.Max(start, x.Range.StartPage), Math.Min(end, x.Range.EndPage))))
            .ToList();

        // Режим «Без пересечений»: диапазон с пересекающимися страницами не добавляется.
        if (BlockOverlaps && dupIntervals.Count > 0)
        {
            string conflict = PageRangeUtils.MergeToString(dupIntervals);
            SetError($"Пересечения запрещены: страницы {conflict} уже выбраны. Диапазон не добавлен.");
            return;
        }

        var range = new PageRange { StartPage = start, EndPage = end };
        Ranges.Add(range);
        UpdateRangesDisplay();

        // Подготовить следующий ввод
        RangeStartText = (end + 1 <= TotalPages ? end + 1 : TotalPages).ToString();
        RangeEndText = TotalPages.ToString();

        Overlaps.Clear();
        if (dupIntervals.Count > 0)
        {
            // Единое место вывода: все пересечения (и текущие, и с прежними группами) — в баннере.
            foreach (var r in curOverlaps)
                Overlaps.Add(MakeOverlap(start, end, r, "текущие диапазоны"));
            foreach (var (label, r) in prevOverlaps)
                Overlaps.Add(MakeOverlap(start, end, r, $"группа {label}"));

            DuplicatedPagesText = PageRangeUtils.MergeToString(dupIntervals);
            _overlapRange = range;
            HasOverlapWarning = true;
            SetInfo($"Добавлен диапазон {range} — есть пересечения (см. предупреждение).");
        }
        else
        {
            SetInfo($"Добавлен диапазон: {range}");
        }
    }

    private static OverlapInfo MakeOverlap(int start, int end, PageRange existing, string source)
    {
        int ds = Math.Max(start, existing.StartPage);
        int de = Math.Min(end, existing.EndPage);
        string dup = ds == de ? $"{ds}" : $"{ds}–{de}";
        return new OverlapInfo($"{start}–{end}", $"{existing.StartPage}–{existing.EndPage}", source, dup);
    }

    // Баннер пересечения с предыдущими группами
    [ObservableProperty]
    private bool _hasOverlapWarning;

    /// <summary>Сводный компактный список дублируемых страниц, напр. «10–23, 30».</summary>
    [ObservableProperty]
    private string _duplicatedPagesText = string.Empty;

    /// <summary>Режим запрета добавления пересекающихся диапазонов (тумблер у «Обработать»).</summary>
    [ObservableProperty]
    private bool _blockOverlaps;

    /// <summary>Список пересечений добавленного диапазона с диапазонами прежних групп.</summary>
    public ObservableCollection<OverlapInfo> Overlaps { get; } = new();

    private PageRange? _overlapRange;

    /// <summary>«Добавить ещё раз» — оставить диапазон несмотря на пересечение.</summary>
    [RelayCommand]
    private void KeepOverlapRange()
    {
        HasOverlapWarning = false;
        Overlaps.Clear();
        _overlapRange = null;
        SetInfo("Диапазон оставлен (страницы продублируются).");
    }

    /// <summary>«Убрать» — удалить только что добавленный пересекающийся диапазон.</summary>
    [RelayCommand]
    private void RemoveOverlapRange()
    {
        if (_overlapRange != null)
        {
            Ranges.Remove(_overlapRange);
            UpdateRangesDisplay();
        }
        _overlapRange = null;
        HasOverlapWarning = false;
        Overlaps.Clear();
        SetInfo("Пересекающийся диапазон убран.");
    }

    [RelayCommand]
    private void RemoveRange(PageRange? range)
    {
        if (range == null) return;
        if (ReferenceEquals(range, _overlapRange)) { _overlapRange = null; HasOverlapWarning = false; Overlaps.Clear(); }
        Ranges.Remove(range);
        UpdateRangesDisplay();
        SetInfo($"Диапазон {range} убран");
    }

    // -------------------------------------------------------------------
    // ПРЕДПРОСМОТР СТРАНИЦ (включаемая панель + увеличение)
    // -------------------------------------------------------------------

    [ObservableProperty]
    private bool _isPreviewEnabled;

    [ObservableProperty]
    private PageRange? _selectedRange;

    [ObservableProperty]
    private Bitmap? _startThumb;

    [ObservableProperty]
    private Bitmap? _endThumb;

    [ObservableProperty]
    private bool _isZoomOpen;

    [ObservableProperty]
    private Bitmap? _zoomImage;

    /// <summary>Строка-разделитель между миниатюрами, напр. «↕ 233 стр.  (112 → 344)».</summary>
    [ObservableProperty]
    private string _previewRangeText = string.Empty;

    /// <summary>Есть ли загруженные миниатюры (для подсказки в панели).</summary>
    [ObservableProperty]
    private bool _hasPreview;

    partial void OnIsPreviewEnabledChanged(bool value)
    {
        if (value)
            _ = RefreshPreviewAsync();
        else
        {
            StartThumb = null;
            EndThumb = null;
            HasPreview = false;
        }
    }

    partial void OnSelectedRangeChanged(PageRange? value)
    {
        PreviewRangeText = value is null
            ? string.Empty
            : $"↕ {value.PageCount} стр.  ({value.StartPage} → {value.EndPage})";

        if (IsPreviewEnabled)
            _ = RefreshPreviewAsync();
    }

    private async Task RefreshPreviewAsync()
    {
        var range = SelectedRange;
        var path = SourceFilePath;

        if (range is null || string.IsNullOrEmpty(path) || TotalPages <= 0)
        {
            StartThumb = null;
            EndThumb = null;
            HasPreview = false;
            return;
        }

        int startPage = range.StartPage;
        int endPage = range.EndPage;

        try
        {
            var (s, e) = await Task.Run(() =>
            {
                var sp = ImageHelper.ToBitmap(_renderService.RenderPage(path, startPage, 360, 1000));
                var ep = ImageHelper.ToBitmap(_renderService.RenderPage(path, endPage, 360, 1000));
                return (sp, ep);
            });

            StartThumb = s;
            EndThumb = e;
            HasPreview = true;
        }
        catch
        {
            StartThumb = null;
            EndThumb = null;
            HasPreview = false;
        }
    }

    [RelayCommand]
    private Task ZoomStartAsync() => OpenZoomAsync(SelectedRange?.StartPage);

    [RelayCommand]
    private Task ZoomEndAsync() => OpenZoomAsync(SelectedRange?.EndPage);

    private async Task OpenZoomAsync(int? page)
    {
        var path = SourceFilePath;
        if (page is null || string.IsNullOrEmpty(path)) return;

        int p = page.Value;
        try
        {
            var big = await Task.Run(() =>
                ImageHelper.ToBitmap(_renderService.RenderPage(path, p, 1600, 2200)));
            ZoomImage = big;
            IsZoomOpen = true;
        }
        catch
        {
            // не критично
        }
    }

    /// <summary>Свернуть увеличенный просмотр (по клику в любом месте).</summary>
    public void CloseZoom()
    {
        IsZoomOpen = false;
        ZoomImage = null;
    }

    [RelayCommand]
    private void ClearRanges()
    {
        Ranges.Clear();
        _overlapRange = null;
        HasOverlapWarning = false;
        Overlaps.Clear();
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
        _overlapRange = null;
        HasOverlapWarning = false;
        Overlaps.Clear();
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

    // --- «О программе» / ручная проверка обновлений ---

    /// <summary>Версия приложения (из сборки) для окна «О программе».</summary>
    public string AppVersion { get; } = GetAppVersion();

    [ObservableProperty]
    private string _updateCheckStatus = string.Empty;

    private static string GetAppVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
            return info.Split('+')[0];
        var v = asm.GetName().Version;
        return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>Ручная проверка обновления из окна «О программе».</summary>
    [RelayCommand]
    private async Task CheckUpdatesManualAsync()
    {
        if (!_updateService.IsSupported)
        {
            UpdateCheckStatus = "Проверка доступна только в установленной или portable-версии (не в режиме разработки).";
            return;
        }

        UpdateCheckStatus = "Проверка…";
        try
        {
            var version = await _updateService.CheckAsync();
            if (version is null)
            {
                UpdateCheckStatus = "У вас последняя версия.";
                return;
            }

            UpdateCheckStatus = $"Найдено обновление {version}. Скачивание…";
            await _updateService.DownloadAsync();

            UpdateText = $"Доступно обновление {version} — готово к установке.";
            IsUpdateReady = true;
            UpdateCheckStatus = $"Обновление {version} скачано. Нажмите «Обновить и перезапустить».";
        }
        catch (Exception ex)
        {
            UpdateCheckStatus = "Не удалось проверить обновления: " + ex.Message;
        }
    }

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
        _overlapRange = null;
        HasOverlapWarning = false;
        Overlaps.Clear();
        SelectedRange = null;
        StartThumb = null;
        EndThumb = null;
        HasPreview = false;
        CloseZoom();
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
}
