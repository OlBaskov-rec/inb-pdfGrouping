using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfGrouping.Core;
using PdfGrouping.Core.Models;
using PdfGrouping.Desktop.Services;

namespace PdfGrouping.Desktop.ViewModels;

/// <summary>Группы (будущие выходные PDF): создание, объединение, обработка, результаты.</summary>
public partial class MainViewModel
{
    /// <summary>Сформированные группы.</summary>
    public ObservableCollection<PdfGroup> Groups { get; } = new();

    [ObservableProperty]
    private string _groupLabelText = string.Empty;

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private bool _isProcessing;

    /// <summary>Файлы, созданные последней обработкой.</summary>
    public ObservableCollection<string> OutputFiles { get; } = new();

    [ObservableProperty]
    private bool _hasResults;

    // Запрос на объединение с существующей группой
    [ObservableProperty]
    private bool _hasMergePrompt;

    [ObservableProperty]
    private string _mergePromptText = string.Empty;

    private PdfGroup? _mergeTarget;

    [RelayCommand]
    private async Task BrowseOutputDirectoryAsync()
    {
        var path = await _filePicker.PickFolderAsync();
        if (!string.IsNullOrEmpty(path))
            OutputDirectory = path;
    }

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

    /// <summary>Пересечения текущих диапазонов со страницами уже созданных групп (логика — в Core).</summary>
    private List<OverlapAnalysis.FileRange> FindConflictsWithGroups() =>
        OverlapAnalysis.Intersections(
            Groups.SelectMany(g => g.Ranges).Select(r => new OverlapAnalysis.FileRange(r.SourceFile, r.StartPage, r.EndPage)),
            Ranges.Select(r => new OverlapAnalysis.FileRange(r.SourceFile, r.StartPage, r.EndPage)));

    /// <summary>Пересечения текущих диапазонов между собой (логика — в Core).</summary>
    private List<OverlapAnalysis.FileRange> FindInternalConflicts() =>
        OverlapAnalysis.InternalIntersections(
            Ranges.Select(r => new OverlapAnalysis.FileRange(r.SourceFile, r.StartPage, r.EndPage)).ToList());

    private void CreateOrMergeGroup(string label, PdfGroup? target)
    {
        // Режим «Без пересечений»: запрещаем создавать/объединять при пересечении страниц.
        // Сравниваем со ВСЕМИ группами: при объединении дубль внутри целевой группы тоже дубль.
        if (BlockOverlaps)
        {
            var conflicts = FindConflictsWithGroups();
            if (conflicts.Count > 0)
            {
                ShowBlockMessage(L.Format("Block_ForbiddenGroup",
                    WithUnit(PageRangeUtils.MergeToString(conflicts.Select(c => (c.Start, c.End))))));
                return;
            }
        }

        if (target != null)
        {
            // Объединяем: заменяем элемент в коллекции, чтобы обновился вывод (PdfGroup — не INPC).
            int idx = Groups.IndexOf(target);
            var merged = new PdfGroup { Label = target.Label };
            foreach (var r in target.Ranges)
                merged.Ranges.Add(new PageRange { StartPage = r.StartPage, EndPage = r.EndPage, SourceFile = r.SourceFile, FileNumber = r.FileNumber });
            foreach (var r in Ranges)
                merged.Ranges.Add(new PageRange { StartPage = r.StartPage, EndPage = r.EndPage, SourceFile = r.SourceFile, FileNumber = r.FileNumber });
            Groups[idx] = merged;
            SetInfo(L.Format("Msg_RangesAddedToGroup", merged.Label, merged.TotalPages));
        }
        else
        {
            var group = new PdfGroup { Label = label };
            foreach (var r in Ranges)
                group.Ranges.Add(new PageRange { StartPage = r.StartPage, EndPage = r.EndPage, SourceFile = r.SourceFile, FileNumber = r.FileNumber });
            Groups.Add(group);
            SetInfo(L.Format("Msg_GroupAdded", label, group.TotalPages));
        }

        // Готовимся к следующей группе
        Ranges.Clear();
        ClearOverlapState();
        GroupLabelText = string.Empty;
    }

    /// <summary>«Создать группы для вывода каждого диапазона» — каждый диапазон → отдельная группа.</summary>
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
            var conflicts = FindInternalConflicts();
            conflicts.AddRange(FindConflictsWithGroups());
            if (conflicts.Count > 0)
            {
                ShowBlockMessage(L.Format("Block_ForbiddenPerRange",
                    WithUnit(PageRangeUtils.MergeToString(conflicts.Select(c => (c.Start, c.End))))));
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
            g.Ranges.Add(new PageRange { StartPage = r.StartPage, EndPage = r.EndPage, SourceFile = r.SourceFile, FileNumber = r.FileNumber });
            Groups.Add(g);
            created++;
        }

        Ranges.Clear();
        ClearOverlapState();
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
            var outDir = OutputDirectory;

            // Каждый диапазон несёт свой SourceFile — движок сам откроет нужные исходники
            // (один или несколько) и соберёт из них выходные файлы.
            var produced = await Task.Run(() => _pdfService.SplitAndGroup(groupsList, outDir));

            foreach (var f in produced)
                OutputFiles.Add(f);

            HasResults = produced.Count > 0;
            SetInfo(L.Format("Msg_Done", produced.Count));
        }
        catch (Exception ex)
        {
            AppLog.Error("Ошибка обработки PDF", ex);
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
}
