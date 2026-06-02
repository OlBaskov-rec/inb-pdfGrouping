using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using iText.Kernel.Pdf;
using iText.Kernel.Utils;
using PdfGrouping.App.Models;

namespace PdfGrouping.App.Services;

public class PdfService
{
    /// <summary>
    /// Path to qpdf.exe. By default, it looks in the application directory.
    /// </summary>
    public string QpdfPath { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qpdf", "qpdf.exe");

    /// <summary>
    /// Splits a PDF into ranges using qpdf, then merges groups using iText7.
    /// Returns the list of output file paths (one per group).
    /// </summary>
    public List<string> SplitAndGroup(string inputPdfPath, List<PdfGroup> groups, string outputDirectory)
    {
        // Validate input
        if (!File.Exists(inputPdfPath))
            throw new FileNotFoundException("PDF-файл не найден.", inputPdfPath);

        if (groups == null || groups.Count == 0)
            throw new ArgumentException("Список групп пуст.");

        // Get total page count using iText7 (fast, no external process)
        int totalPages = GetPageCount(inputPdfPath);

        // Validate ranges
        foreach (var group in groups)
        {
            foreach (var range in group.Ranges)
            {
                if (range.StartPage < 1 || range.EndPage < 1)
                    throw new ArgumentException($"Номера страниц должны быть >= 1: {range}");
                if (range.StartPage > totalPages || range.EndPage > totalPages)
                    throw new ArgumentException($"Страница вне диапазона (всего {totalPages}): {range}");
            }
        }

        // Create temp directory for intermediate split files
        string tempDir = Path.Combine(outputDirectory, "_temp_splits");
        Directory.CreateDirectory(tempDir);

        List<string> outputFiles = new List<string>();

        try
        {
            // STEP 1: Split source PDF into individual pages using qpdf (one PDF per page)
            var splitPageFiles = SplitToPages(inputPdfPath, tempDir, totalPages);

            // STEP 2: For each group, merge the selected pages using iText7
            foreach (var group in groups)
            {
                string outputPath = Path.Combine(outputDirectory, $"{SanitizeFileName(group.Label)}.pdf");
                MergePages(splitPageFiles, group, outputPath);
                outputFiles.Add(outputPath);
            }
        }
        finally
        {
            // Cleanup temp files
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }

        return outputFiles;
    }

    /// <summary>
    /// Gets total page count using iText7.
    /// </summary>
    public int GetPageCount(string pdfPath)
    {
        using var reader = new PdfReader(pdfPath);
        using var doc = new PdfDocument(reader);
        return doc.GetNumberOfPages();
    }

    /// <summary>
    /// Splits a PDF into individual page files using qpdf.exe: one file per page.
    /// Returns dictionary: pageNumber (1-based) -> file path.
    /// </summary>
    private Dictionary<int, string> SplitToPages(string inputPdfPath, string tempDir, int totalPages)
    {
        var result = new Dictionary<int, string>();

        // Use qpdf to split: qpdf --split-pages=n input.pdf temp_%d.pdf
        // Each output file contains up to n pages. We use n=1 for per-page split.
        string prefix = Path.Combine(tempDir, "page_");

        RunQpdf(
            $"--split-pages=1",
            $"\"{inputPdfPath}\"",
            $"\"{prefix}%d.pdf\""
        );

        // qpdf names output files as prefix1.pdf, prefix2.pdf, ...
        // Account for possible offsets: qpdf may start numbering from 1
        for (int i = 1; i <= totalPages; i++)
        {
            string expectedPath = $"{prefix}{i}.pdf";
            if (File.Exists(expectedPath))
            {
                result[i] = expectedPath;
            }
            else
            {
                // Try to find by listing files in temp dir
                throw new FileNotFoundException($"Не удалось найти страницу {i} в файлах разделения. Ожидался: {expectedPath}");
            }
        }

        return result;
    }

    /// <summary>
    /// Merges selected pages into a single PDF using iText7 PdfMerger.
    /// </summary>
    private void MergePages(Dictionary<int, string> pageFiles, PdfGroup group, string outputPath)
    {
        using var writer = new PdfWriter(outputPath);
        using var outputDoc = new PdfDocument(writer);
        var merger = new PdfMerger(outputDoc);

        foreach (var range in group.Ranges)
        {
            for (int page = range.StartPage; page <= range.EndPage; page++)
            {
                if (!pageFiles.TryGetValue(page, out var pageFile))
                    throw new FileNotFoundException($"Файл страницы {page} не найден для группы '{group.Label}'.");

                using var pageReader = new PdfReader(pageFile);
                using var pageDoc = new PdfDocument(pageReader);
                merger.Merge(pageDoc, 1, 1); // merge single page
            }
        }

        outputDoc.Close();
    }

    /// <summary>
    /// Runs qpdf.exe with the given arguments.
    /// </summary>
    private void RunQpdf(params string[] args)
    {
        if (!File.Exists(QpdfPath))
        {
            throw new FileNotFoundException(
                $"qpdf.exe не найден по пути: {QpdfPath}\n" +
                "Поместите qpdf.exe в папку 'qpdf' рядом с программой.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = QpdfPath,
            Arguments = string.Join(" ", args),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi);
        process?.WaitForExit(60_000); // 60 seconds timeout

        if (process?.ExitCode != 0)
        {
            string error = process?.StandardError.ReadToEnd() ?? "";
            throw new InvalidOperationException($"Ошибка qpdf (код {process?.ExitCode}):\n{error}\nКоманда: qpdf {string.Join(" ", args)}");
        }
    }

    /// <summary>
    /// Removes invalid characters from file names.
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "group" : name;
    }
}