using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfGrouping.Core;
using PdfGrouping.Core.Models;

namespace PdfGrouping.Desktop.ViewModels;

/// <summary>Диапазоны страниц: ввод, добавление (обычное и постраничное), удаление.</summary>
public partial class MainViewModel
{
    /// <summary>Выбранные диапазоны (до объединения в группу).</summary>
    public ObservableCollection<PageRange> Ranges { get; } = new();

    /// <summary>
    /// Высота списка диапазонов ФИКСИРОВАНА: 3 строки по умолчанию, максимум 5 — дальше внутренняя
    /// прокрутка. Список НЕ растит окно; окно авто-увеличивается только под зону сообщений.
    /// </summary>
    public double RangesListHeight => Math.Clamp(Ranges.Count, 3, 5) * 43 + 4;

    // Числовые поля со стрелками; nullable — допускают временно пустое значение.
    [ObservableProperty]
    private decimal? _rangeStart = 1;

    [ObservableProperty]
    private decimal? _rangeEnd = 1;

    /// <summary>
    /// Общая для обеих кнопок добавления валидация ввода: файл открыт, номера заполнены и в
    /// пределах документа, начало не больше конца. При ошибке показывает сообщение и возвращает false.
    /// </summary>
    private bool TryGetRangeInput(out int start, out int end)
    {
        start = end = 0;
        if (GuardPendingDecision()) return false;
        if (TotalPages <= 0)
        {
            SetError(L["Err_OpenPdfFirst"]);
            return false;
        }
        if (RangeStart is null || RangeEnd is null)
        {
            SetError(L["Err_EnterPageNumbers"]);
            return false;
        }

        start = (int)RangeStart.Value;
        end = (int)RangeEnd.Value;

        if (start < 1 || end < 1 || start > TotalPages || end > TotalPages)
        {
            SetError(L.Format("Err_PageRange", TotalPages));
            return false;
        }
        if (start > end)
        {
            SetError(L["Err_StartGtEnd"]);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Пересечения нового диапазона с текущими диапазонами и со страницами созданных групп,
    /// плюс объединённый список дублируемых интервалов.
    /// </summary>
    private (List<PageRange> Cur, List<(string Label, PageRange Range)> Prev, List<(int, int)> Dup)
        FindOverlaps(int start, int end)
    {
        var cur = Ranges
            .Where(r => start <= r.EndPage && end >= r.StartPage)
            .ToList();
        var prev = Groups
            .SelectMany(g => g.Ranges.Select(r => (g.Label, Range: r)))
            .Where(x => start <= x.Range.EndPage && end >= x.Range.StartPage)
            .ToList();
        var dup = cur.Select(r => (Math.Max(start, r.StartPage), Math.Min(end, r.EndPage)))
            .Concat(prev.Select(x => (Math.Max(start, x.Range.StartPage), Math.Min(end, x.Range.EndPage))))
            .ToList();
        return (cur, prev, dup);
    }

    [RelayCommand]
    private void AddRange()
    {
        if (!TryGetRangeInput(out int start, out int end)) return;

        var (cur, prev, dup) = FindOverlaps(start, end);
        HasBlockMessage = false;

        // Режим «Без пересечений»: диапазон с пересекающимися страницами не добавляется.
        if (BlockOverlaps && dup.Count > 0)
        {
            ShowBlockMessage(L.Format("Block_ForbiddenAddOne", WithUnit(PageRangeUtils.MergeToString(dup))));
            return;
        }

        var range = new PageRange { StartPage = start, EndPage = end };

        if (dup.Count > 0)
        {
            // Пересечение: НЕ добавляем сразу — ждём решения пользователя (кнопки в баннере).
            StartPendingDecision(start, end, cur, prev, dup, new[] { range });
            return;
        }

        Ranges.Add(range);
        AdvanceRangeInput(end);
        SetInfo(L.Format("Msg_RangeAdded", range));
    }

    /// <summary>«+ Добавить диапазон постранично» — раскидать выбранные страницы по 1-страничным диапазонам.</summary>
    [RelayCommand]
    private void AddRangePaginated()
    {
        if (!TryGetRangeInput(out int start, out int end)) return;

        var (cur, prev, dup) = FindOverlaps(start, end);
        HasBlockMessage = false;

        if (BlockOverlaps && dup.Count > 0)
        {
            ShowBlockMessage(L.Format("Block_ForbiddenAddMany", WithUnit(PageRangeUtils.MergeToString(dup))));
            return;
        }

        // Готовим 1-страничные диапазоны.
        var pages = new List<PageRange>();
        for (int p = start; p <= end; p++)
            pages.Add(new PageRange { StartPage = p, EndPage = p });

        if (dup.Count > 0)
        {
            StartPendingDecision(start, end, cur, prev, dup, pages);
            return;
        }

        foreach (var pr in pages)
            Ranges.Add(pr);
        AdvanceRangeInput(end);
        SetInfo(L.Format("Msg_AddedPaginated", end - start + 1));
    }

    /// <summary>После добавления — подставить следующий свободный интервал в поля ввода.</summary>
    private void AdvanceRangeInput(int end)
    {
        RangeStart = end + 1 <= TotalPages ? end + 1 : TotalPages;
        RangeEnd = TotalPages;
    }

    [RelayCommand]
    private void RemoveRange(PageRange? range)
    {
        if (range == null) return;
        if (_overlapBatch.Remove(range) && _overlapBatch.Count == 0)
        {
            HasOverlapWarning = false;
            Overlaps.Clear();
            OverlapsMoreText = string.Empty;
        }
        Ranges.Remove(range);
        SetInfo(L.Format("Msg_RangeRemoved", range));
    }

    [RelayCommand]
    private void ClearRanges()
    {
        Ranges.Clear();
        ClearOverlapState();
        SetInfo(L["Msg_RangesCleared"]);
    }
}
