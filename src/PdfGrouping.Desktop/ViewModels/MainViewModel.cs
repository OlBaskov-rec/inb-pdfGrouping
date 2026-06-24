using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfGrouping.Core;
using PdfGrouping.Core.Models;
using PdfGrouping.Core.Services;
using PdfGrouping.Desktop.Localization;
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

    /// <summary>Доступ к локализатору.</summary>
    private static Localizer L => Localizer.Instance;

    public MainViewModel(IFilePickerService filePicker, UpdateService updateService)
    {
        _filePicker = filePicker;
        _updateService = updateService;

        // Высота списка диапазонов зависит от числа строк (3..5), дальше — прокрутка.
        Ranges.CollectionChanged += (_, _) => OnPropertyChanged(nameof(RangesListHeight));

        // Обновление зависящих от языка строк при переключении языка «на лету».
        L.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(LanguageShort));
            OnPropertyChanged(nameof(AppVersionText));
            OnPropertyChanged(nameof(PagesOfText));
            OnPropertyChanged(nameof(UpdateFoundButtonText));
            UpdateRangesDisplay();
            // Перерисовать динамические строки активных сообщений на новом языке.
            RebuildOverlapTexts();
            RebuildResolvedRanges();
            if (HasMergePrompt && _mergeTarget != null)
                MergePromptText = L.Format("Merge_Prompt", _mergeTarget.Label);
        };
    }

    // --- Язык интерфейса ---

    /// <summary>Список языков для меню выбора.</summary>
    public System.Collections.Generic.IReadOnlyList<Localizer.LanguageOption> Languages => Localizer.Languages;

    /// <summary>Краткая подпись текущего языка для кнопки.</summary>
    public string LanguageShort => L.CurrentShort;

    [RelayCommand]
    private void SetLanguage(string code) => L.SetLanguage(code);

    /// <summary>Высота списка диапазонов: 3 строки по умолчанию, до 5 — растёт, дальше прокрутка.</summary>
    public double RangesListHeight => Math.Clamp(Ranges.Count, 3, 5) * 48 + 20;

    // --- Исходный PDF ---
    [ObservableProperty]
    private string _sourceFilePath = string.Empty;

    [ObservableProperty]
    private int _totalPages;

    /// <summary>Максимум для полей ввода страниц (минимум 1, чтобы NumericUpDown был корректен).</summary>
    public decimal MaxPage => Math.Max(1, TotalPages);

    /// <summary>Локализованная подпись «Страницы (из N):» (число в середине — через шаблон).</summary>
    public string PagesOfText => L.Format("Ranges_PagesOf", TotalPages);

    partial void OnTotalPagesChanged(int value)
    {
        OnPropertyChanged(nameof(MaxPage));
        OnPropertyChanged(nameof(PagesOfText));
    }

    [ObservableProperty]
    private string _statusText = Localizer.Instance.Get("Status_Ready");

    [ObservableProperty]
    private bool _statusIsError;

    // --- Диапазоны (до группировки) ---
    public ObservableCollection<PageRange> Ranges { get; } = new();

    [ObservableProperty]
    private string _rangesDisplayText = Localizer.Instance.Get("List_Empty");

    // --- Группы ---
    public ObservableCollection<PdfGroup> Groups { get; } = new();

    // --- Ввод диапазона (числовые поля со стрелками; nullable — допускают временно пустое значение) ---
    [ObservableProperty]
    private decimal? _rangeStart = 1;

    [ObservableProperty]
    private decimal? _rangeEnd = 1;

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

    /// <summary>Обновление найдено (мигаем значком «ℹ», в меню — кнопка «Обнаружено обновление»).</summary>
    [ObservableProperty]
    private bool _isUpdateAvailable;

    /// <summary>Обновление готово к установке (показан баннер внизу с кнопками).</summary>
    [ObservableProperty]
    private bool _isUpdateReady;

    [ObservableProperty]
    private string _updateText = string.Empty;

    private bool _updateDownloaded;

    /// <summary>Показывать кнопку ручной проверки (когда обновление ещё не найдено).</summary>
    public bool ShowCheckButton => !IsUpdateAvailable;

    /// <summary>Показывать зелёную кнопку «Обнаружено обновление» (найдено, но баннер ещё не показан).</summary>
    public bool ShowRevealButton => IsUpdateAvailable && !IsUpdateReady;

    /// <summary>Текст зелёной кнопки в меню, напр. «Обнаружено обновление 0.1.30».</summary>
    public string UpdateFoundButtonText => L.Format("Btn_UpdateFound", _availableVersion);

    partial void OnIsUpdateAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCheckButton));
        OnPropertyChanged(nameof(ShowRevealButton));
        OnPropertyChanged(nameof(UpdateFoundButtonText));
    }

    partial void OnIsUpdateReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCheckButton));
        OnPropertyChanged(nameof(ShowRevealButton));
    }

    // -------------------------------------------------------------------
    // КОМАНДЫ
    // -------------------------------------------------------------------

    [RelayCommand]
    private async Task BrowseSourceFileAsync()
    {
        var path = await _filePicker.PickPdfAsync();
        if (!string.IsNullOrEmpty(path))
            LoadFromPath(path);
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
        if (GuardPendingDecision()) return;
        if (TotalPages <= 0)
        {
            SetError(L["Err_OpenPdfFirst"]);
            return;
        }

        if (RangeStart is null || RangeEnd is null)
        {
            SetError(L["Err_EnterPageNumbers"]);
            return;
        }
        int start = (int)RangeStart.Value;
        int end = (int)RangeEnd.Value;

        if (start < 1 || end < 1 || start > TotalPages || end > TotalPages)
        {
            SetError(L.Format("Err_PageRange", TotalPages));
            return;
        }

        if (start > end)
        {
            SetError(L["Err_StartGtEnd"]);
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

        HasBlockMessage = false;

        // Режим «Без пересечений»: диапазон с пересекающимися страницами не добавляется.
        if (BlockOverlaps && dupIntervals.Count > 0)
        {
            ShowBlockMessage(L.Format("Block_ForbiddenAddOne", WithUnit(PageRangeUtils.MergeToString(dupIntervals))));
            return;
        }

        var range = new PageRange { StartPage = start, EndPage = end };

        if (dupIntervals.Count > 0)
        {
            // Пересечение: НЕ добавляем сразу — ждём решения пользователя (кнопки в баннере).
            BuildOverlapWarning(start, end, curOverlaps, prevOverlaps, dupIntervals);
            _pendingRanges.Clear();
            _pendingRanges.Add(range);
            ComputePendingTrim();
            HasPendingDecision = true;
            HasOverlapWarning = true;
            SetInfo(string.Empty);
            return;
        }

        // Без пересечений — добавляем сразу.
        Ranges.Add(range);
        UpdateRangesDisplay();
        AdvanceRangeInput(end);
        SetInfo(L.Format("Msg_RangeAdded", range));
    }

    /// <summary>«+ Добавить диапазон постранично» — раскидать выбранные страницы по 1-страничным диапазонам.</summary>
    [RelayCommand]
    private void AddRangePaginated()
    {
        if (GuardPendingDecision()) return;
        if (TotalPages <= 0) { SetError(L["Err_OpenPdfFirst"]); return; }
        if (RangeStart is null || RangeEnd is null)
        {
            SetError(L["Err_EnterPageNumbers"]);
            return;
        }
        int start = (int)RangeStart.Value;
        int end = (int)RangeEnd.Value;
        if (start < 1 || end < 1 || start > TotalPages || end > TotalPages)
        { SetError(L.Format("Err_PageRange", TotalPages)); return; }
        if (start > end) { SetError(L["Err_StartGtEnd"]); return; }

        var curOverlaps = Ranges.Where(r => start <= r.EndPage && end >= r.StartPage).ToList();
        var prevOverlaps = Groups
            .SelectMany(g => g.Ranges.Select(r => (g.Label, Range: r)))
            .Where(x => start <= x.Range.EndPage && end >= x.Range.StartPage)
            .ToList();
        var dupIntervals = curOverlaps.Select(r => (Math.Max(start, r.StartPage), Math.Min(end, r.EndPage)))
            .Concat(prevOverlaps.Select(x => (Math.Max(start, x.Range.StartPage), Math.Min(end, x.Range.EndPage))))
            .ToList();

        HasBlockMessage = false;
        if (BlockOverlaps && dupIntervals.Count > 0)
        {
            ShowBlockMessage(L.Format("Block_ForbiddenAddMany", WithUnit(PageRangeUtils.MergeToString(dupIntervals))));
            return;
        }

        // Готовим 1-страничные диапазоны.
        var pages = new List<PageRange>();
        for (int p = start; p <= end; p++)
            pages.Add(new PageRange { StartPage = p, EndPage = p });

        if (dupIntervals.Count > 0)
        {
            // Пересечение: НЕ добавляем сразу — ждём решения.
            BuildOverlapWarning(start, end, curOverlaps, prevOverlaps, dupIntervals);
            _pendingRanges.Clear();
            _pendingRanges.AddRange(pages);
            ComputePendingTrim();
            HasPendingDecision = true;
            HasOverlapWarning = true;
            SetInfo(string.Empty);
            return;
        }

        foreach (var pr in pages)
            Ranges.Add(pr);
        UpdateRangesDisplay();
        AdvanceRangeInput(end);
        SetInfo(L.Format("Msg_AddedPaginated", end - start + 1));
    }

    // Сырые данные текущего предупреждения о пересечении (для перерисовки при смене языка).
    private readonly List<(int NewStart, int NewEnd, int ExStart, int ExEnd, string? GroupLabel)> _overlapRaw = new();
    private List<(int, int)> _overlapDupRaw = new();

    /// <summary>Строит баннер пересечения и запоминает сырые данные для перерисовки при смене языка.</summary>
    private void BuildOverlapWarning(int start, int end,
        List<PageRange> curOverlaps, List<(string Label, PageRange Range)> prevOverlaps,
        List<(int, int)> dupIntervals)
    {
        _overlapRaw.Clear();
        foreach (var r in curOverlaps)
            _overlapRaw.Add((start, end, r.StartPage, r.EndPage, null));
        foreach (var (label, r) in prevOverlaps)
            _overlapRaw.Add((start, end, r.StartPage, r.EndPage, label));
        _overlapDupRaw = dupIntervals;
        RebuildOverlapTexts();
    }

    /// <summary>Пересобирает локализованные строки баннера пересечения из сырых данных.</summary>
    private void RebuildOverlapTexts()
    {
        if (!HasOverlapWarning && _overlapRaw.Count == 0) { Overlaps.Clear(); return; }
        Overlaps.Clear();
        foreach (var o in _overlapRaw)
        {
            string source = o.GroupLabel is null ? L["Src_CurrentRanges"] : L.Format("Src_Group", o.GroupLabel);
            Overlaps.Add(MakeOverlap(o.NewStart, o.NewEnd,
                new PageRange { StartPage = o.ExStart, EndPage = o.ExEnd }, source));
        }
        DuplicatedPagesText = WithUnit(PageRangeUtils.MergeToString(_overlapDupRaw));
        if (HasPendingDecision)
            ComputePendingTrim();
    }

    /// <summary>Пересобирает список предлагаемых непересекающихся диапазонов (для смены языка).</summary>
    private void RebuildResolvedRanges()
    {
        if (!HasConflictPrompt) return;
        var intervals = Ranges.Select(r => (r.StartPage, r.EndPage)).ToList();
        var resolved = PageRangeUtils.ResolveOverlaps(intervals);
        ResolvedRanges.Clear();
        foreach (var (s, e) in resolved)
            ResolvedRanges.Add(s == e ? L.Format("Resolved_Page", s) : L.Format("Resolved_PageRange", s, e));
    }

    private static OverlapInfo MakeOverlap(int start, int end, PageRange existing, string source)
    {
        int ds = Math.Max(start, existing.StartPage);
        int de = Math.Min(end, existing.EndPage);
        string dup = L.Format("Overlap_RepeatFull", WithUnit(ds == de ? $"{ds}" : $"{ds}–{de}"));
        return new OverlapInfo($"{start}–{end}", $"{existing.StartPage}–{existing.EndPage}", source, dup);
    }

    /// <summary>Добавляет единицу измерения: «страница 10» / «страницы 70–90».</summary>
    private static string WithUnit(string pages)
    {
        if (string.IsNullOrEmpty(pages)) return pages;
        bool plural = pages.Contains('–') || pages.Contains(',');
        return plural ? L.Format("Unit_PageMany", pages) : L.Format("Unit_PageOne", pages);
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

    // Последняя добавленная «партия» диапазонов (для кнопки «Убрать»).
    private readonly List<PageRange> _overlapBatch = new();

    // Диапазоны, ожидающие решения пользователя (ещё НЕ добавлены в список).
    private readonly List<PageRange> _pendingRanges = new();

    // Те же диапазоны, обрезанные до свободных страниц (для «Добавить без пересечения»).
    private readonly List<PageRange> _pendingTrimmed = new();

    /// <summary>Есть ли нерешённое пересечение (нужно нажать «Добавить ещё раз» / «без пересечения» / «Убрать»).</summary>
    [ObservableProperty]
    private bool _hasPendingDecision;

    /// <summary>Можно ли менять номера страниц: нельзя, пока висит вопрос (пересечение или конфликт).</summary>
    public bool IsRangeInputEnabled => !HasPendingDecision && !HasConflictPrompt;

    partial void OnHasPendingDecisionChanged(bool value) => OnPropertyChanged(nameof(IsRangeInputEnabled));

    /// <summary>Что будет добавлено в режиме «без пересечения», напр. «страницы 46–52».</summary>
    [ObservableProperty]
    private string _pendingResolveText = string.Empty;

    private void ComputePendingTrim()
    {
        var covered = Ranges.Select(r => (r.StartPage, r.EndPage))
            .Concat(Groups.SelectMany(g => g.Ranges).Select(r => (r.StartPage, r.EndPage)));
        var trimmed = PageRangeUtils.Subtract(_pendingRanges.Select(r => (r.StartPage, r.EndPage)), covered);

        _pendingTrimmed.Clear();
        foreach (var (s, e) in trimmed)
            _pendingTrimmed.Add(new PageRange { StartPage = s, EndPage = e });

        PendingResolveText = trimmed.Count == 0
            ? L["Resolve_NoFree"]
            : WithUnit(PageRangeUtils.MergeToString(trimmed));
    }

    /// <summary>«Добавить без пересечения» — добавить ожидающий диапазон, обрезанный до свободных страниц.</summary>
    [RelayCommand]
    private void KeepWithoutOverlap()
    {
        HasBlockMessage = false;
        if (_pendingTrimmed.Count == 0)
        {
            _pendingRanges.Clear();
            HasPendingDecision = false;
            HasOverlapWarning = false;
            Overlaps.Clear();
            SetInfo(L["Msg_AllSelected"]);
            return;
        }

        int lastEnd = _pendingTrimmed.Max(r => r.EndPage);
        foreach (var r in _pendingTrimmed)
            Ranges.Add(r);

        _overlapBatch.Clear();
        _overlapBatch.AddRange(_pendingTrimmed);
        _pendingTrimmed.Clear();
        _pendingRanges.Clear();
        HasPendingDecision = false;
        HasOverlapWarning = false; // конфликт разрешён — пересечений нет
        Overlaps.Clear();

        UpdateRangesDisplay();
        AdvanceRangeInput(lastEnd);
        SetInfo(L["Msg_AddedWithout"]);
    }

    private void AdvanceRangeInput(int end)
    {
        RangeStart = end + 1 <= TotalPages ? end + 1 : TotalPages;
        RangeEnd = TotalPages;
    }

    /// <summary>Если есть нерешённое пересечение/конфликт — нельзя продолжать, пока не решат.</summary>
    private bool GuardPendingDecision()
    {
        if (HasPendingDecision)
        {
            ShowBlockMessage(L["Block_DecideFirst"]);
            return true;
        }
        if (HasConflictPrompt)
        {
            ShowBlockMessage(L["Block_ResolveFirst"]);
            return true;
        }
        return false;
    }

    // --- Сообщение о запрете пересечений (в области уведомлений) ---

    [ObservableProperty]
    private bool _hasBlockMessage;

    [ObservableProperty]
    private string _blockMessage = string.Empty;

    /// <summary>Тип блок-сообщения: спец-подсказка про «Без пересечений» при активном вопросе (цветной текст).</summary>
    [ObservableProperty]
    private bool _blockMessageIsOverlapHint;

    /// <summary>Видно ли обычное (однотонное) блок-сообщение.</summary>
    public bool ShowPlainBlock => HasBlockMessage && !BlockMessageIsOverlapHint;

    /// <summary>Видна ли цветная подсказка про «Без пересечений».</summary>
    public bool ShowOverlapHint => HasBlockMessage && BlockMessageIsOverlapHint;

    partial void OnBlockMessageIsOverlapHintChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPlainBlock));
        OnPropertyChanged(nameof(ShowOverlapHint));
    }

    // --- Разрешение конфликта при включении «Без пересечений» ---

    [ObservableProperty]
    private bool _hasConflictPrompt;

    /// <summary>Кнопка «Оставить имеющиеся пересечения» — появляется с небольшой задержкой.</summary>
    [ObservableProperty]
    private bool _showKeepOverlapsButton;

    /// <summary>Предлагаемые непересекающиеся диапазоны (для отображения), напр. «Стр. 10–110».</summary>
    public ObservableCollection<string> ResolvedRanges { get; } = new();

    /// <summary>Видна ли область сообщений (предупреждение / конфликт / запрет).</summary>
    public bool IsMessageVisible => HasOverlapWarning || HasConflictPrompt || HasBlockMessage;

    partial void OnHasBlockMessageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsMessageVisible));
        OnPropertyChanged(nameof(ShowPlainBlock));
        OnPropertyChanged(nameof(ShowOverlapHint));
    }

    private void ShowBlockMessage(string text)
    {
        StatusText = string.Empty;
        StatusIsError = false;
        BlockMessageIsOverlapHint = false;
        BlockMessage = text;
        HasBlockMessage = true;
    }

    partial void OnHasOverlapWarningChanged(bool value) => OnPropertyChanged(nameof(IsMessageVisible));

    partial void OnHasConflictPromptChanged(bool value)
    {
        OnPropertyChanged(nameof(IsMessageVisible));
        OnPropertyChanged(nameof(IsRangeInputEnabled));
        ShowKeepOverlapsButton = false;
        if (value)
            _ = RevealKeepOverlapsButtonAsync();
    }

    /// <summary>Показать «Оставить имеющиеся пересечения» спустя короткую видимую паузу.</summary>
    private async Task RevealKeepOverlapsButtonAsync()
    {
        await Task.Delay(1200);
        if (HasConflictPrompt)
            ShowKeepOverlapsButton = true;
    }

    /// <summary>«Оставить имеющиеся пересечения» — диапазоны не меняем, режим «Без пересечений» выключаем.</summary>
    [RelayCommand]
    private void KeepExistingOverlaps()
    {
        HasConflictPrompt = false;
        ResolvedRanges.Clear();
        ShowKeepOverlapsButton = false;
        Dispatcher.UIThread.Post(() => BlockOverlaps = false);
        SetInfo(L["Msg_OverlapsKept"]);
    }

    partial void OnBlockOverlapsChanged(bool value)
    {
        if (value)
        {
            // Одновременно — только один вопрос. Если ждём решения по добавлению диапазона,
            // второй вопрос не показываем: цветная подсказка в области уведомлений + откат тумблера.
            if (HasPendingDecision)
            {
                StatusText = string.Empty;
                StatusIsError = false;
                BlockMessageIsOverlapHint = true;
                HasBlockMessage = true;
                // Откатываем тумблер отложенно: иначе ToggleButton, обрабатывающий свой клик,
                // не сбросит визуальное состояние и останется «нажатым», хотя режим выключен.
                Dispatcher.UIThread.Post(() => BlockOverlaps = false);
                return;
            }

            var intervals = Ranges.Select(r => (r.StartPage, r.EndPage)).ToList();
            if (HasInternalOverlaps(intervals))
            {
                var resolved = PageRangeUtils.ResolveOverlaps(intervals);
                ResolvedRanges.Clear();
                foreach (var (s, e) in resolved)
                    ResolvedRanges.Add(s == e ? L.Format("Resolved_Page", s) : L.Format("Resolved_PageRange", s, e));
                HasConflictPrompt = true;
            }
        }
        else
        {
            HasConflictPrompt = false;
            ResolvedRanges.Clear();
        }
    }

    private static bool HasInternalOverlaps(List<(int Start, int End)> ranges)
    {
        for (int i = 0; i < ranges.Count; i++)
            for (int j = i + 1; j < ranges.Count; j++)
                if (ranges[i].Start <= ranges[j].End && ranges[i].End >= ranges[j].Start)
                    return true;
        return false;
    }

    /// <summary>«Подтвердить» — применить непересекающиеся диапазоны (обрезка/разбиение).</summary>
    [RelayCommand]
    private void ConfirmResolve()
    {
        var intervals = Ranges.Select(r => (r.StartPage, r.EndPage)).ToList();
        var resolved = PageRangeUtils.ResolveOverlaps(intervals);

        Ranges.Clear();
        foreach (var (s, e) in resolved)
            Ranges.Add(new PageRange { StartPage = s, EndPage = e });

        ClearOverlapState();
        UpdateRangesDisplay();
        SetInfo(L["Msg_OverlapsTrimmed"]);
    }

    /// <summary>«Убрать пересекающиеся» — оставить только первые занявшие страницы диапазоны.</summary>
    [RelayCommand]
    private void RemoveOverlappingRanges()
    {
        var kept = new List<PageRange>();
        foreach (var r in Ranges)
        {
            bool overlapsKept = kept.Any(k => r.StartPage <= k.EndPage && r.EndPage >= k.StartPage);
            if (!overlapsKept)
                kept.Add(r);
        }

        Ranges.Clear();
        foreach (var r in kept)
            Ranges.Add(r);

        ClearOverlapState();
        UpdateRangesDisplay();
        SetInfo(L["Msg_OverlappingRemoved"]);
    }

    private void ClearOverlapState()
    {
        HasConflictPrompt = false;
        ResolvedRanges.Clear();
        HasOverlapWarning = false;
        Overlaps.Clear();
        _overlapRaw.Clear();
        _overlapBatch.Clear();
        _pendingRanges.Clear();
        _pendingTrimmed.Clear();
        HasPendingDecision = false;
        HasBlockMessage = false;
    }

    /// <summary>«Добавить ещё раз» — добавить ожидающий решения диапазон несмотря на пересечение.</summary>
    [RelayCommand]
    private void KeepOverlapRange()
    {
        HasBlockMessage = false;
        if (_pendingRanges.Count == 0) return;

        int lastEnd = _pendingRanges.Max(r => r.EndPage);
        foreach (var r in _pendingRanges)
            Ranges.Add(r);

        _overlapBatch.Clear();
        _overlapBatch.AddRange(_pendingRanges);
        _pendingRanges.Clear();
        _pendingTrimmed.Clear();
        HasPendingDecision = false;

        UpdateRangesDisplay();
        AdvanceRangeInput(lastEnd);
        // Область предупреждения НЕ убираем — остаётся как информация.
        SetInfo(L["Msg_RangeAddedDup"]);
    }

    /// <summary>«Убрать» — отклонить ожидающий диапазон или удалить только что добавленную партию.</summary>
    [RelayCommand]
    private void RemoveOverlapRange()
    {
        HasBlockMessage = false;
        if (_pendingRanges.Count > 0)
        {
            _pendingRanges.Clear();
            _pendingTrimmed.Clear();
            HasPendingDecision = false;
            SetInfo(L["Msg_OverlapNotAdded"]);
        }
        else
        {
            foreach (var r in _overlapBatch)
                Ranges.Remove(r);
            UpdateRangesDisplay();
            SetInfo(L["Msg_OverlappingRemoved"]);
        }
        _overlapBatch.Clear();
        HasOverlapWarning = false;
        Overlaps.Clear();
    }

    [RelayCommand]
    private void RemoveRange(PageRange? range)
    {
        if (range == null) return;
        if (_overlapBatch.Remove(range) && _overlapBatch.Count == 0)
        {
            HasOverlapWarning = false;
            Overlaps.Clear();
        }
        Ranges.Remove(range);
        UpdateRangesDisplay();
        SetInfo(L.Format("Msg_RangeRemoved", range));
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

    /// <summary>Угол поворота страницы в увеличенном просмотре (0/90/180/270°).</summary>
    [ObservableProperty]
    private double _zoomRotation;

    [RelayCommand]
    private void RotateZoomLeft() => ZoomRotation = ((ZoomRotation - 90) % 360 + 360) % 360;

    [RelayCommand]
    private void RotateZoomRight() => ZoomRotation = (ZoomRotation + 90) % 360;

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
            : L.Format("Preview_RangeInfo", value.PageCount, value.StartPage, value.EndPage);

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
            ZoomRotation = 0; // каждый просмотр открываем без поворота
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
        ClearOverlapState();
        UpdateRangesDisplay();
        SetInfo(L["Msg_RangesCleared"]);
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
        if (GuardPendingDecision()) return;
        string label = (GroupLabelText ?? string.Empty).Trim();

        var labelError = FileNameValidator.Validate(label);
        if (labelError != null)
        {
            SetError(labelError);
            return;
        }

        if (Ranges.Count == 0)
        {
            SetError(L["Err_AddRangesFirst"]);
            return;
        }

        var existing = Groups.FirstOrDefault(g => string.Equals(g.Label, label, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            // Спрашиваем: добавить выбранные диапазоны в уже существующую группу?
            _mergeTarget = existing;
            MergePromptText = L.Format("Merge_Prompt", existing.Label);
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
        SetError(L["Err_ChooseOtherName"]);
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
                ShowBlockMessage(L.Format("Block_ForbiddenGroup", WithUnit(PageRangeUtils.MergeToString(conflicts))));
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
            SetInfo(L.Format("Msg_RangesAddedToGroup", merged.Label, merged.TotalPages));
        }
        else
        {
            var group = new PdfGroup { Label = label };
            foreach (var r in Ranges)
                group.Ranges.Add(new PageRange { StartPage = r.StartPage, EndPage = r.EndPage });
            Groups.Add(group);
            SetInfo(L.Format("Msg_GroupAdded", label, group.TotalPages));
        }

        // Готовимся к следующей группе
        Ranges.Clear();
        ClearOverlapState();
        UpdateRangesDisplay();
        GroupLabelText = string.Empty;
    }

    /// <summary>«Создать группу на каждый диапазон» — каждый текущий диапазон → отдельная группа.</summary>
    [RelayCommand]
    private void AddGroupPerRange()
    {
        if (GuardPendingDecision()) return;
        if (Ranges.Count == 0)
        {
            SetError(L["Err_AddRangesFirst"]);
            return;
        }

        string prefix = (GroupLabelText ?? string.Empty).Trim();
        if (prefix.Length > 0)
        {
            var err = FileNameValidator.Validate(prefix);
            if (err != null) { SetError(err); return; }
        }

        HasBlockMessage = false;
        // Режим «Без пересечений»: запрещаем, если диапазоны пересекаются между собой или с группами.
        if (BlockOverlaps)
        {
            var rangeList = Ranges.Select(r => (r.StartPage, r.EndPage)).ToList();
            var conflicts = new List<(int, int)>();
            for (int i = 0; i < rangeList.Count; i++)
                for (int j = i + 1; j < rangeList.Count; j++)
                    if (rangeList[i].StartPage <= rangeList[j].EndPage && rangeList[i].EndPage >= rangeList[j].StartPage)
                        conflicts.Add((Math.Max(rangeList[i].StartPage, rangeList[j].StartPage),
                                       Math.Min(rangeList[i].EndPage, rangeList[j].EndPage)));
            foreach (var gr in Groups.SelectMany(g => g.Ranges))
                foreach (var cur in Ranges)
                    if (cur.StartPage <= gr.EndPage && cur.EndPage >= gr.StartPage)
                        conflicts.Add((Math.Max(cur.StartPage, gr.StartPage), Math.Min(cur.EndPage, gr.EndPage)));

            if (conflicts.Count > 0)
            {
                ShowBlockMessage(L.Format("Block_ForbiddenPerRange", WithUnit(PageRangeUtils.MergeToString(conflicts))));
                return;
            }
        }

        int created = 0;
        foreach (var r in Ranges.ToList())
        {
            string rangeStr = r.StartPage == r.EndPage ? $"{r.StartPage}" : $"{r.StartPage}-{r.EndPage}";
            string label = prefix.Length == 0 ? rangeStr : $"{prefix} {rangeStr}";
            label = UniqueGroupLabel(label);

            var g = new PdfGroup { Label = label };
            g.Ranges.Add(new PageRange { StartPage = r.StartPage, EndPage = r.EndPage });
            Groups.Add(g);
            created++;
        }

        Ranges.Clear();
        ClearOverlapState();
        UpdateRangesDisplay();
        GroupLabelText = string.Empty;
        SetInfo(L.Format("Msg_GroupsCreated", created));
    }

    private string UniqueGroupLabel(string baseLabel)
    {
        string label = baseLabel;
        int n = 1;
        while (Groups.Any(g => string.Equals(g.Label, label, StringComparison.OrdinalIgnoreCase)))
            label = $"{baseLabel}_{++n}";
        return label;
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
            SetError(L["Err_NoGroups"]);
            return;
        }

        if (string.IsNullOrEmpty(OutputDirectory))
        {
            SetError(L["Err_ChooseOutput"]);
            return;
        }

        IsProcessing = true;
        HasResults = false;
        OutputFiles.Clear();
        SetInfo(L["Status_Processing"]);

        try
        {
            var groupsList = Groups.ToList();
            var source = SourceFilePath;
            var outDir = OutputDirectory;

            var produced = await Task.Run(() => _pdfService.SplitAndGroup(source, groupsList, outDir));

            foreach (var f in produced)
                OutputFiles.Add(f);

            HasResults = produced.Count > 0;
            SetInfo(L.Format("Msg_Done", produced.Count));
        }
        catch (Exception ex)
        {
            SetError(L.Format("Err_Processing", ex.Message));
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

    /// <summary>Локализованная строка «Версия X» (обновляется при смене языка).</summary>
    public string AppVersionText => L.Format("About_Version", AppVersion);

    [ObservableProperty]
    private string _updateCheckStatus = string.Empty;

    /// <summary>Версия скачанного/найденного обновления (для сообщений).</summary>
    private string _availableVersion = string.Empty;

    /// <summary>Разворачивает цепочку вложенных исключений в одну строку (для диагностики).</summary>
    private static string DescribeError(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (sb.Length > 0) sb.Append(" → ");
            sb.Append(e.Message);
        }
        return sb.ToString();
    }

    private static string GetAppVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
            return info.Split('+')[0];
        var v = asm.GetName().Version;
        return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>Ручная проверка обновления из окна «О программе» (когда автоматически не найдено).</summary>
    [RelayCommand]
    private async Task CheckUpdatesManualAsync()
    {
        if (!_updateService.IsSupported)
        {
            UpdateCheckStatus = L["Upd_OnlyInstalled"];
            return;
        }

        UpdateCheckStatus = L["Upd_Checking"];
        try
        {
            var version = await Task.Run(() => _updateService.CheckAsync());
            if (version is null)
            {
                UpdateCheckStatus = L["Upd_Latest"];
                return;
            }

            _availableVersion = version;
            IsUpdateAvailable = true;
            UpdateCheckStatus = L.Format("Upd_Found", version);
            await Task.Run(() => _updateService.DownloadAsync());
            _updateDownloaded = true;

            // Ручная проверка — это явное действие пользователя, сразу показываем баннер.
            UpdateText = L.Format("Upd_ReadyText", version);
            UpdateCheckStatus = L.Format("Upd_Downloaded", version);
            IsUpdateReady = true;
        }
        catch (Exception ex)
        {
            UpdateCheckStatus = IsUpdateReady
                ? L.Format("Upd_Downloaded", _availableVersion)
                : L.Format("Upd_Failed", DescribeError(ex));
        }
    }

    /// <summary>«Обнаружено обновление» в меню: докачивает (если нужно) и показывает баннер внизу.</summary>
    [RelayCommand]
    private async Task RevealUpdateAsync()
    {
        if (!IsUpdateAvailable) return;
        if (!_updateDownloaded)
        {
            UpdateCheckStatus = L.Format("Upd_Found", _availableVersion);
            try
            {
                await Task.Run(() => _updateService.DownloadAsync());
                _updateDownloaded = true;
            }
            catch (Exception ex)
            {
                UpdateCheckStatus = L.Format("Upd_Failed", DescribeError(ex));
                return;
            }
        }

        UpdateText = L.Format("Upd_ReadyText", _availableVersion);
        UpdateCheckStatus = L.Format("Upd_Downloaded", _availableVersion);
        IsUpdateReady = true;
    }

    /// <summary>«Отложить»: скрыть баннер внизу. Значок остаётся зелёным — обновиться можно позже.</summary>
    [RelayCommand]
    private void PostponeUpdate() => IsUpdateReady = false;

    /// <summary>
    /// Фоновая проверка обновлений при старте. В dev-запуске — безопасный no-op.
    /// Находит и (для удобства) скачивает обновление в фоне, но НЕ показывает баннер сразу —
    /// лишь подсвечивает значок «ℹ». Баннер появится после «Обнаружено обновление» в меню.
    /// Ошибки сети не мешают работе приложения.
    /// </summary>
    public async Task CheckForUpdatesAsync()
    {
        try
        {
            // ВСЯ работа Velopack (включая синхронные участки и сеть) — на пуле потоков, НИКОГДА
            // не на UI-потоке, плюс таймаут: запуск приложения не должен подвисать из-за проверки.
            var checkTask = Task.Run(() => _updateService.CheckAsync());
            if (await Task.WhenAny(checkTask, Task.Delay(TimeSpan.FromSeconds(20))) != checkTask)
                return; // сеть не ответила вовремя — тихо выходим, приложение работает

            var version = await checkTask;
            if (version is null)
                return;

            _availableVersion = version;
            IsUpdateAvailable = true; // мигание значка «ℹ» (продолжение — на UI-потоке)

            // Фоновое скачивание — чтобы по «Обнаружено обновление» применилось мгновенно.
            try { await Task.Run(() => _updateService.DownloadAsync()); _updateDownloaded = true; }
            catch { /* докачаем по требованию */ }
        }
        catch
        {
            // Обновление не критично для основной работы — тихо игнорируем.
        }
    }

    /// <summary>Сбрасывает все рабочие данные (диапазоны, группы, предупреждения, предпросмотр).</summary>
    private void ResetWorkspace()
    {
        Ranges.Clear();
        Groups.Clear();
        OutputFiles.Clear();
        HasResults = false;
        ClearOverlapState();
        _mergeTarget = null;
        HasMergePrompt = false;
        SelectedRange = null;
        StartThumb = null;
        EndThumb = null;
        HasPreview = false;
        CloseZoom();
        GroupLabelText = string.Empty;
        UpdateRangesDisplay();
    }

    [RelayCommand]
    private void ClearAll()
    {
        ResetWorkspace();
        SourceFilePath = string.Empty;
        TotalPages = 0;
        OutputDirectory = string.Empty;
        RangeStart = 1;
        RangeEnd = 1;
        SetInfo(L["Status_Ready"]);
    }

    // -------------------------------------------------------------------
    // ВСПОМОГАТЕЛЬНОЕ
    // -------------------------------------------------------------------

    /// <summary>Загрузка PDF по пути (кнопка/drag&amp;drop). Новый файл = новая сессия: всё обнуляется.</summary>
    public void LoadFromPath(string path)
    {
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            SetError(L["Err_SelectPdf"]);
            return;
        }

        ResetWorkspace();
        SourceFilePath = path;
        OutputDirectory = Path.GetDirectoryName(path) ?? string.Empty;
        LoadPdfInfo();
    }

    private void LoadPdfInfo()
    {
        try
        {
            TotalPages = _pdfService.GetPageCount(SourceFilePath);
            RangeStart = 1;
            RangeEnd = TotalPages;
            // Если групп ещё нет — подставим первую метку «A» (можно изменить).
            if (Groups.Count == 0 && string.IsNullOrEmpty(GroupLabelText))
                GroupLabelText = "A";
            SetInfo(L.Format("Msg_Loaded", Path.GetFileName(SourceFilePath), TotalPages));
        }
        catch (Exception ex)
        {
            TotalPages = 0;
            SetError(L.Format("Err_ReadPdf", ex.Message));
        }
    }

    private void UpdateRangesDisplay()
    {
        // По одному диапазону на строке для лучшей читаемости.
        RangesDisplayText = Ranges.Count == 0
            ? L["List_Empty"]
            : string.Join("\n", Ranges.Select(r => r.ToString()));
    }

    // Рутинные подтверждения у кнопок больше не выводятся: любое инфо-сообщение
    // лишь скрывает баннер ошибки. Ошибки выводятся в общую область сообщений (баннер).
    private void SetInfo(string text)
    {
        StatusIsError = false;
        StatusText = text;
        HasBlockMessage = false;
    }

    private void SetError(string text)
    {
        StatusIsError = true;
        StatusText = text;
        ShowBlockMessage(text);
    }
}
