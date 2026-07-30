using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfGrouping.Core;
using PdfGrouping.Core.Models;

namespace PdfGrouping.Desktop.ViewModels;

/// <summary>Описание одного пересечения: добавленный диапазон vs уже выбранный диапазон.</summary>
/// <param name="Source">Источник пересечения, напр. «группа 12» или «текущие диапазоны».</param>
public record OverlapInfo(string NewPages, string ExistingPages, string Source, string Dup);

/// <summary>Пересечения страниц: баннер предупреждения, ожидание решения, режим «Без пересечений».</summary>
public partial class MainViewModel
{
    // --- Баннер пересечения ---

    [ObservableProperty]
    private bool _hasOverlapWarning;

    /// <summary>Сводный компактный список дублируемых страниц, напр. «10–23, 30».</summary>
    [ObservableProperty]
    private string _duplicatedPagesText = string.Empty;

    /// <summary>Режим запрета добавления пересекающихся диапазонов (тумблер у «Обработать»).</summary>
    [ObservableProperty]
    private bool _blockOverlaps;

    /// <summary>Список пересечений добавленного диапазона с уже выбранными страницами.</summary>
    public ObservableCollection<OverlapInfo> Overlaps { get; } = new();

    /// <summary>
    /// Максимум подробных строк в баннере пересечения. Больше рисовать бессмысленно и дорого:
    /// список не виртуализируется, сотни строк (постраничные диапазоны) заметно тормозят UI.
    /// Остаток сворачивается в строку «… и ещё N», сводка страниц всё равно полная.
    /// </summary>
    private const int MaxOverlapRows = 50;

    /// <summary>Строка «… и ещё N» под списком пересечений (пустая, если всё уместилось).</summary>
    [ObservableProperty]
    private string _overlapsMoreText = string.Empty;

    // Результат анализа пересечений (Core) для активного предупреждения: хранится «как данные»,
    // чтобы при смене языка пересобрать локализованные строки без повторного анализа.
    private OverlapAnalysis.Report _overlapReport = OverlapAnalysis.Report.Empty;
    private int _overlapNewStart, _overlapNewEnd;

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

    /// <summary>Показывает баннер пересечения и переводит добавление в режим ожидания решения.</summary>
    private void StartPendingDecision(int start, int end,
        OverlapAnalysis.Report report, IEnumerable<PageRange> pending)
    {
        _overlapNewStart = start;
        _overlapNewEnd = end;
        _overlapReport = report;
        RebuildOverlapTexts();
        _pendingRanges.Clear();
        _pendingRanges.AddRange(pending);
        ComputePendingTrim();
        HasPendingDecision = true;
        HasOverlapWarning = true;
        SetInfo(string.Empty);
    }

    /// <summary>Пересобирает локализованные строки баннера пересечения из данных анализа.</summary>
    private void RebuildOverlapTexts()
    {
        if (!HasOverlapWarning && !_overlapReport.HasOverlaps) { Overlaps.Clear(); OverlapsMoreText = string.Empty; return; }
        Overlaps.Clear();
        foreach (var hit in _overlapReport.Hits.Take(MaxOverlapRows))
        {
            string source = hit.GroupLabel is null ? L["Src_CurrentRanges"] : L.Format("Src_Group", hit.GroupLabel);
            Overlaps.Add(MakeOverlap(hit, source));
        }
        OverlapsMoreText = _overlapReport.Hits.Count > MaxOverlapRows
            ? L.Format("Overlap_MoreRows", _overlapReport.Hits.Count - MaxOverlapRows)
            : string.Empty;
        DuplicatedPagesText = WithUnit(PageRangeUtils.MergeToString(_overlapReport.DupIntervals));
        if (HasPendingDecision)
            ComputePendingTrim();
    }

    /// <summary>Превращает результат анализа (Core) в локализованную строку баннера.</summary>
    private OverlapInfo MakeOverlap(OverlapAnalysis.Hit hit, string source)
    {
        string dup = L.Format("Overlap_RepeatFull",
            WithUnit(hit.DupStart == hit.DupEnd ? $"{hit.DupStart}" : $"{hit.DupStart}–{hit.DupEnd}"));
        return new OverlapInfo($"{_overlapNewStart}–{_overlapNewEnd}",
            $"{hit.Existing.Start}–{hit.Existing.End}", source, dup);
    }

    /// <summary>Добавляет единицу измерения: «страница 10» / «страницы 70–90».</summary>
    private static string WithUnit(string pages)
    {
        if (string.IsNullOrEmpty(pages)) return pages;
        bool plural = pages.Contains('–') || pages.Contains(',');
        return plural ? L.Format("Unit_PageMany", pages) : L.Format("Unit_PageOne", pages);
    }

    private void ComputePendingTrim()
    {
        if (_pendingRanges.Count == 0)
        {
            _pendingTrimmed.Clear();
            PendingResolveText = string.Empty;
            return;
        }

        // Все _pendingRanges — из ОДНОГО (активного на момент добавления) файла; «занятые»
        // страницы других файлов не имеют значения — фильтруем covered по этому же файлу.
        string file = _pendingRanges[0].SourceFile;
        var covered = Ranges.Where(r => r.SourceFile == file).Select(r => (r.StartPage, r.EndPage))
            .Concat(Groups.SelectMany(g => g.Ranges).Where(r => r.SourceFile == file).Select(r => (r.StartPage, r.EndPage)));
        var trimmed = PageRangeUtils.Subtract(_pendingRanges.Select(r => (r.StartPage, r.EndPage)), covered);

        _pendingTrimmed.Clear();
        foreach (var (s, e) in trimmed)
            _pendingTrimmed.Add(new PageRange { StartPage = s, EndPage = e, SourceFile = file });

        PendingResolveText = trimmed.Count == 0
            ? L["Resolve_NoFree"]
            : WithUnit(PageRangeUtils.MergeToString(trimmed));
    }

    // --- Кнопки решения по ожидающему диапазону ---

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
            OverlapsMoreText = string.Empty;
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
        OverlapsMoreText = string.Empty;

        AdvanceRangeInput(lastEnd);
        SetInfo(L["Msg_AddedWithout"]);
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
            SetInfo(L["Msg_OverlappingRemoved"]);
        }
        _overlapBatch.Clear();
        HasOverlapWarning = false;
        Overlaps.Clear();
        OverlapsMoreText = string.Empty;
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

    private void ShowBlockMessage(string text)
    {
        StatusText = string.Empty;
        StatusIsError = false;
        BlockMessageIsOverlapHint = false;
        BlockMessage = text;
        HasBlockMessage = true;
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

            var intervals = Ranges.Select(r => new OverlapAnalysis.FileRange(r.SourceFile, r.StartPage, r.EndPage)).ToList();
            if (OverlapAnalysis.HasInternalOverlaps(intervals))
            {
                FillResolvedRanges();
                HasConflictPrompt = true;
            }
        }
        else
        {
            HasConflictPrompt = false;
            ResolvedRanges.Clear();
        }
    }

    /// <summary>Пересобирает список предлагаемых непересекающихся диапазонов (для смены языка).</summary>
    private void RebuildResolvedRanges()
    {
        if (!HasConflictPrompt) return;
        FillResolvedRanges();
    }

    /// <summary>
    /// Считает предлагаемое разрешение конфликта (обрезка по занятым страницам, отдельно для
    /// каждого файла) и заполняет <see cref="ResolvedRanges"/>. Если задействовано больше одного
    /// файла — каждая строка помечается именем файла, чтобы не запутаться, откуда какой кусок.
    /// </summary>
    private void FillResolvedRanges()
    {
        var intervals = Ranges.Select(r => new OverlapAnalysis.FileRange(r.SourceFile, r.StartPage, r.EndPage));
        var resolved = OverlapAnalysis.ResolveOverlapsPerFile(intervals);
        bool multiFile = SourceFiles.Count > 1;

        ResolvedRanges.Clear();
        foreach (var r in resolved)
        {
            string text = r.Start == r.End ? L.Format("Resolved_Page", r.Start) : L.Format("Resolved_PageRange", r.Start, r.End);
            if (multiFile) text = $"[{Path.GetFileName(r.File)}] {text}";
            ResolvedRanges.Add(text);
        }
    }

    /// <summary>«Подтвердить» — применить непересекающиеся диапазоны (обрезка/разбиение).</summary>
    [RelayCommand]
    private void ConfirmResolve()
    {
        var intervals = Ranges.Select(r => new OverlapAnalysis.FileRange(r.SourceFile, r.StartPage, r.EndPage));
        var resolved = OverlapAnalysis.ResolveOverlapsPerFile(intervals);

        Ranges.Clear();
        foreach (var r in resolved)
            Ranges.Add(new PageRange { StartPage = r.Start, EndPage = r.End, SourceFile = r.File });

        ClearOverlapState();
        SetInfo(L["Msg_OverlapsTrimmed"]);
    }

    /// <summary>«Убрать пересекающиеся» — оставить только первые занявшие страницы диапазоны (в пределах своего файла).</summary>
    [RelayCommand]
    private void RemoveOverlappingRanges()
    {
        var kept = OverlapAnalysis.KeepFirstOccupiers(
            Ranges.Select(r => new OverlapAnalysis.FileRange(r.SourceFile, r.StartPage, r.EndPage)));

        Ranges.Clear();
        foreach (var r in kept)
            Ranges.Add(new PageRange { StartPage = r.Start, EndPage = r.End, SourceFile = r.File });

        ClearOverlapState();
        SetInfo(L["Msg_OverlappingRemoved"]);
    }

    private void ClearOverlapState()
    {
        HasConflictPrompt = false;
        ResolvedRanges.Clear();
        HasOverlapWarning = false;
        Overlaps.Clear();
        OverlapsMoreText = string.Empty;
        _overlapReport = OverlapAnalysis.Report.Empty;
        _overlapBatch.Clear();
        _pendingRanges.Clear();
        _pendingTrimmed.Clear();
        HasPendingDecision = false;
        HasBlockMessage = false;
    }
}
