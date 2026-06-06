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

        // Высота списка диапазонов зависит от числа строк (3..5), дальше — прокрутка.
        Ranges.CollectionChanged += (_, _) => OnPropertyChanged(nameof(RangesListHeight));
    }

    /// <summary>Высота списка диапазонов: 3 строки по умолчанию, до 5 — растёт, дальше прокрутка.</summary>
    public double RangesListHeight => Math.Clamp(Ranges.Count, 3, 5) * 48 + 20;

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

            DuplicatedPagesText = WithUnit(PageRangeUtils.MergeToString(dupIntervals));
            _overlapRange = range;
            HasOverlapWarning = true;
            // Сообщение у кнопок не дублируем — предупреждение показано в баннере.
            SetInfo(string.Empty);
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
        string dup = WithUnit(ds == de ? $"{ds}" : $"{ds}–{de}");
        return new OverlapInfo($"{start}–{end}", $"{existing.StartPage}–{existing.EndPage}", source, dup);
    }

    /// <summary>Добавляет единицу измерения: «страница 10» / «страницы 70–90».</summary>
    private static string WithUnit(string pages)
    {
        if (string.IsNullOrEmpty(pages)) return pages;
        bool plural = pages.Contains('–') || pages.Contains(',');
        return plural ? $"страницы {pages}" : $"страница {pages}";
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
        // Область предупреждения НЕ убираем — она остаётся как информация о пересечении.
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

    // Запрос на объединение с существующей группой
    [ObservableProperty]
    private bool _hasMergePrompt;

    [ObservableProperty]
    private string _mergePromptText = string.Empty;

    private PdfGroup? _mergeTarget;

    /// <summary>Быстрый выбор метки группы кнопкой (A, B, C …).</summary>
    [RelayCommand]
    private void PickLabel(string? letter)
    {
        if (!string.IsNullOrEmpty(letter))
            GroupLabelText = letter;
    }

    [RelayCommand]
    private void AddGroup()
    {
        string label = (GroupLabelText ?? string.Empty).Trim();

        var labelError = FileNameValidator.Validate(label);
        if (labelError != null)
        {
            SetError(labelError);
            return;
        }

        if (Ranges.Count == 0)
        {
            SetError("Сначала добавьте диапазоны страниц.");
            return;
        }

        var existing = Groups.FirstOrDefault(g => string.Equals(g.Label, label, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            // Спрашиваем: добавить выбранные диапазоны в уже существующую группу?
            _mergeTarget = existing;
            MergePromptText = $"Группа «{existing.Label}» уже существует. Добавить выбранные диапазоны в неё?";
            HasMergePrompt = true;
            return;
        }

        CreateOrMergeGroup(label, null);
    }

    /// <summary>«Добавить в группу» — подтверждение объединения.</summary>
    [RelayCommand]
    private void ConfirmMerge()
    {
        HasMergePrompt = false;
        var target = _mergeTarget;
        _mergeTarget = null;
        if (target != null)
            CreateOrMergeGroup(target.Label, target);
    }

    /// <summary>«Другое название» — отмена объединения.</summary>
    [RelayCommand]
    private void CancelMerge()
    {
        HasMergePrompt = false;
        _mergeTarget = null;
        SetError("Выберите другое название группы.");
    }

    private void CreateOrMergeGroup(string label, PdfGroup? target)
    {
        // Режим «Без пересечений»: запрещаем создавать/объединять при пересечении страниц.
        if (BlockOverlaps)
        {
            var others = Groups.Where(g => g != target).SelectMany(g => g.Ranges)
                .Concat(target?.Ranges ?? Enumerable.Empty<PageRange>());
            var conflicts = others
                .SelectMany(r => Ranges
                    .Where(cur => cur.StartPage <= r.EndPage && cur.EndPage >= r.StartPage)
                    .Select(cur => (Math.Max(cur.StartPage, r.StartPage), Math.Min(cur.EndPage, r.EndPage))))
                .ToList();

            if (conflicts.Count > 0)
            {
                SetError($"Страницы {WithUnit(PageRangeUtils.MergeToString(conflicts))} пересекаются с уже выбранными — " +
                         "в режиме «Без пересечений» действие недопустимо.");
                return;
            }
        }

        if (target != null)
        {
            // Объединяем: заменяем элемент в коллекции, чтобы обновился вывод (PdfGroup — не INPC).
            int idx = Groups.IndexOf(target);
            var merged = new PdfGroup { Label = target.Label };
            foreach (var r in target.Ranges)
                merged.Ranges.Add(new PageRange { StartPage = r.StartPage, EndPage = r.EndPage });
            foreach (var r in Ranges)
                merged.Ranges.Add(new PageRange { StartPage = r.StartPage, EndPage = r.EndPage });
            Groups[idx] = merged;
            SetInfo($"Диапазоны добавлены в группу «{merged.Label}» ({merged.TotalPages} стр.)");
        }
        else
        {
            var group = new PdfGroup { Label = label };
            foreach (var r in Ranges)
                group.Ranges.Add(new PageRange { StartPage = r.StartPage, EndPage = r.EndPage });
            Groups.Add(group);
            SetInfo($"Группа «{label}» добавлена ({group.TotalPages} стр.)");
        }

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
        _mergeTarget = null;
        HasMergePrompt = false;
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
        // По одному диапазону на строке для лучшей читаемости.
        RangesDisplayText = Ranges.Count == 0
            ? "(пусто)"
            : string.Join("\n", Ranges.Select(r => r.ToString()));
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
