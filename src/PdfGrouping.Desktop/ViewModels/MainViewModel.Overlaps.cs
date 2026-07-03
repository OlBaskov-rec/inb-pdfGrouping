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

    // Сырые данные текущего предупреждения о пересечении (для перерисовки при смене языка).
    private readonly List<(int NewStart, int NewEnd, int ExStart, int ExEnd, string? GroupLabel)> _overlapRaw = new();
    private List<(int, int)> _overlapDupRaw = new();

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
        List<PageRange> curOverlaps, List<(string Label, PageRange Range)> prevOverlaps,
        List<(int, int)> dupIntervals, IEnumerable<PageRange> pending)
    {
        BuildOverlapWarning(start, end, curOverlaps, prevOverlaps, dupIntervals);
        _pendingRanges.Clear();
        _pendingRanges.AddRange(pending);
        ComputePendingTrim();
        HasPendingDecision = true;
        HasOverlapWarning = true;
        SetInfo(string.Empty);
    }

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
        if (!HasOverlapWarning && _overlapRaw.Count == 0) { Overlaps.Clear(); OverlapsMoreText = string.Empty; return; }
        Overlaps.Clear();
        foreach (var o in _overlapRaw.Take(MaxOverlapRows))
        {
            string source = o.GroupLabel is null ? L["Src_CurrentRanges"] : L.Format("Src_Group", o.GroupLabel);
            Overlaps.Add(MakeOverlap(o.NewStart, o.NewEnd,
                new PageRange { StartPage = o.ExStart, EndPage = o.ExEnd }, source));
        }
        OverlapsMoreText = _overlapRaw.Count > MaxOverlapRows
            ? L.Format("Overlap_MoreRows", _overlapRaw.Count - MaxOverlapRows)
            : string.Empty;
        DuplicatedPagesText = WithUnit(PageRangeUtils.MergeToString(_overlapDupRaw));
        if (HasPendingDecision)
            ComputePendingTrim();
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
        SetInfo(L["Msg_OverlappingRemoved"]);
    }

    private void ClearOverlapState()
    {
        HasConflictPrompt = false;
        ResolvedRanges.Clear();
        HasOverlapWarning = false;
        Overlaps.Clear();
        OverlapsMoreText = string.Empty;
        _overlapRaw.Clear();
        _overlapBatch.Clear();
        _pendingRanges.Clear();
        _pendingTrimmed.Clear();
        HasPendingDecision = false;
        HasBlockMessage = false;
    }
}
