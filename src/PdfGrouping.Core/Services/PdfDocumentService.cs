using PdfGrouping.Core.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfGrouping.Core.Services;

/// <summary>
/// Разбивает исходный PDF на группы страниц и склеивает каждую группу в отдельный файл.
/// Полностью на PdfSharp (MIT) — без внешних утилит и без временных файлов.
/// </summary>
public class PdfDocumentService
{
    /// <summary>
    /// Возвращает число страниц в PDF (быстро, без полной загрузки содержимого).
    /// </summary>
    public int GetPageCount(string pdfPath)
    {
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException("PDF-файл не найден.", pdfPath);

        try
        {
            using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
            return doc.PageCount;
        }
        catch (PdfReaderException ex)
        {
            throw new InvalidOperationException(
                $"Не удалось прочитать PDF «{Path.GetFileName(pdfPath)}»: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Для каждой группы создаёт отдельный PDF, склеивая указанные диапазоны страниц.
    /// Возвращает список путей к созданным файлам (по одному на группу).
    /// </summary>
    public List<string> SplitAndGroup(string inputPdfPath, List<PdfGroup> groups, string outputDirectory)
    {
        if (!File.Exists(inputPdfPath))
            throw new FileNotFoundException("PDF-файл не найден.", inputPdfPath);
        if (groups is null || groups.Count == 0)
            throw new ArgumentException("Список групп пуст.");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Не указана папка для сохранения результатов.");

        Directory.CreateDirectory(outputDirectory);

        PdfDocument source;
        try
        {
            // Import-режим обязателен, чтобы переносить страницы в новые документы.
            source = PdfReader.Open(inputPdfPath, PdfDocumentOpenMode.Import);
        }
        catch (PdfReaderException ex)
        {
            throw new InvalidOperationException(
                $"Не удалось открыть PDF «{Path.GetFileName(inputPdfPath)}»: {ex.Message}", ex);
        }

        using (source)
        {
            int totalPages = source.PageCount;
            ValidateGroups(groups, totalPages);

            var outputFiles = new List<string>();
            // Защита от совпадающих имён файлов у разных групп.
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                string baseName = SanitizeFileName(group.Label);
                string fileName = baseName;
                int dup = 1;
                while (!usedNames.Add(fileName))
                    fileName = $"{baseName}_{++dup}";

                string outputPath = Path.Combine(outputDirectory, fileName + ".pdf");

                using var outDoc = new PdfDocument();
                foreach (var range in group.Ranges)
                    for (int p = range.StartPage; p <= range.EndPage; p++)
                        outDoc.AddPage(source.Pages[p - 1]); // AddPage импортирует страницу

                outDoc.Save(outputPath);
                outputFiles.Add(outputPath);
            }

            return outputFiles;
        }
    }

    private static void ValidateGroups(List<PdfGroup> groups, int totalPages)
    {
        foreach (var group in groups)
        {
            if (group.Ranges.Count == 0)
                throw new ArgumentException($"Группа «{group.Label}» не содержит диапазонов страниц.");

            foreach (var range in group.Ranges)
            {
                if (range.StartPage < 1 || range.EndPage < 1)
                    throw new ArgumentException($"Номера страниц должны быть >= 1: {range}");
                if (range.StartPage > range.EndPage)
                    throw new ArgumentException($"Начальная страница больше конечной: {range}");
                if (range.StartPage > totalPages || range.EndPage > totalPages)
                    throw new ArgumentException($"Страница вне диапазона (всего {totalPages}): {range}");
            }
        }
    }

    /// <summary>
    /// Убирает из имени файла недопустимые символы.
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "group";

        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        name = name.Trim();
        return string.IsNullOrEmpty(name) ? "group" : name;
    }
}
