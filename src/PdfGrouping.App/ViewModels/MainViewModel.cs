using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PdfGrouping.App.Models;
using PdfGrouping.App.Services;

namespace PdfGrouping.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly PdfService _pdfService = new();

    // --- Source PDF ---
    [ObservableProperty]
    private string _sourceFilePath = string.Empty;

    [ObservableProperty]
    private int _totalPages;

    [ObservableProperty]
    private string _statusText = "Готов к работе";

    // --- Ranges (before grouping) ---
    public ObservableCollection<PageRange> Ranges { get; set; } = new();

    // Ranges display for UI (formatted string)
    [ObservableProperty]
    private string _rangesDisplayText = string.Empty;

    // --- Groups ---
    public ObservableCollection<PdfGroup> Groups { get; set; } = new();

    // --- Add range inputs ---
    [ObservableProperty]
    private string _rangeStartText = "1";

    [ObservableProperty]
    private string _rangeEndText = "1";

    // --- Add group label input ---
    [ObservableProperty]
    private string _groupLabelText = string.Empty;

    // --- Output directory ---
    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    // --- Is processing ---
    [ObservableProperty]
    private bool _isProcessing;

    // -------------------------------------------------------------------
    // COMMANDS
    // -------------------------------------------------------------------

    [RelayCommand]
    private void BrowseSourceFile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "PDF файлы (*.pdf)|*.pdf|Все файлы (*.*)|*.*",
            Title = "Выберите PDF файл"
        };

        if (dlg.ShowDialog() == true)
        {
            SourceFilePath = dlg.FileName;
            OutputDirectory = Path.GetDirectoryName(dlg.FileName) ?? string.Empty;
            LoadPdfInfo();
        }
    }

    [RelayCommand]
    private void BrowseOutputDirectory()
    {
        var dlg = new global::System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Выберите папку для сохранения результатов"
        };

        if (dlg.ShowDialog() == global::System.Windows.Forms.DialogResult.OK)
        {
            OutputDirectory = dlg.SelectedPath;
        }
    }

    [RelayCommand]
    private void AddRange()
    {
        if (!int.TryParse(RangeStartText, out int start) || !int.TryParse(RangeEndText, out int end))
        {
            MessageBox.Show("Введите корректные номера страниц (целые числа).", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (start < 1 || end < 1 || start > TotalPages || end > TotalPages)
        {
            MessageBox.Show($"Номера страниц должны быть от 1 до {TotalPages}.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (start > end)
        {
            MessageBox.Show("Начальная страница не может быть больше конечной.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var range = new PageRange { StartPage = start, EndPage = end };
        Ranges.Add(range);
        UpdateRangesDisplay();

        // Clear inputs
        RangeStartText = (end + 1).ToString();
        RangeEndText = (TotalPages).ToString();

        StatusText = $"Добавлен диапазон: {range}";
    }

    [RelayCommand]
    private void RemoveSelectedRange()
    {
        // This is handled in code-behind via a separate mechanism.
        // For simplicity, we expose the ranges collection and let UI handle selection.
    }

    [RelayCommand]
    private void AddGroup()
    {
        string label = GroupLabelText?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(label))
        {
            MessageBox.Show("Введите метку группы (букву или номер).", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Ranges.Count == 0)
        {
            MessageBox.Show("Сначала добавьте диапазоны страниц.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Create group with current ranges
        var group = new PdfGroup { Label = label };
        foreach (var r in Ranges)
        {
            group.Ranges.Add(new PageRange { StartPage = r.StartPage, EndPage = r.EndPage });
        }

        Groups.Add(group);
        StatusText = $"Группа '{label}' добавлена ({group.TotalPages} стр.)";

        // Clear ranges for next group
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
    private void ClearRanges()
    {
        Ranges.Clear();
        UpdateRangesDisplay();
        StatusText = "Диапазоны очищены";
    }

    [RelayCommand]
    private async Task ProcessAsync()
    {
        if (Groups.Count == 0)
        {
            MessageBox.Show("Нет групп для обработки. Добавьте хотя бы одну группу.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(OutputDirectory))
        {
            MessageBox.Show("Выберите папку для сохранения результатов.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsProcessing = true;
        StatusText = "Обработка...";

        try
        {
            var groupsList = Groups.ToList();
            var outputFiles = await Task.Run(() =>
                _pdfService.SplitAndGroup(SourceFilePath, groupsList, OutputDirectory)
            );

            StatusText = $"Готово! Создано файлов: {outputFiles.Count}";

            string fileList = string.Join("\n", outputFiles.Select(f => $"• {f}"));
            MessageBox.Show($"Обработка завершена успешно!\n\nСозданные файлы:\n{fileList}",
                "Готово", MessageBoxButton.OK, MessageBoxImage.Information);

            // Ask to open output folder
            var result = MessageBox.Show("Открыть папку с результатами?", "Открыть папку",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start("explorer.exe", OutputDirectory);
            }
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка!";
            MessageBox.Show($"Ошибка при обработке:\n{ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private void ClearAll()
    {
        SourceFilePath = string.Empty;
        TotalPages = 0;
        Ranges.Clear();
        Groups.Clear();
        UpdateRangesDisplay();
        GroupLabelText = string.Empty;
        OutputDirectory = string.Empty;
        StatusText = "Готов к работе";
    }

    // -------------------------------------------------------------------
    // HELPERS
    // -------------------------------------------------------------------

    private void LoadPdfInfo()
    {
        try
        {
            TotalPages = _pdfService.GetPageCount(SourceFilePath);
            StatusText = $"Загружен: {Path.GetFileName(SourceFilePath)} ({TotalPages} стр.)";
            RangeStartText = "1";
            RangeEndText = TotalPages.ToString();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось прочитать PDF:\n{ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
            TotalPages = 0;
        }
    }

    private void UpdateRangesDisplay()
    {
        RangesDisplayText = Ranges.Count == 0
            ? "(пусто)"
            : string.Join(", ", Ranges.Select(r => r.ToString()));
    }

    // Event handler for drag-and-drop
    public void OnFileDropped(string filePath)
    {
        if (filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            SourceFilePath = filePath;
            OutputDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
            LoadPdfInfo();
        }
        else
        {
            MessageBox.Show("Пожалуйста, перетащите PDF-файл.", "Неверный формат",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}