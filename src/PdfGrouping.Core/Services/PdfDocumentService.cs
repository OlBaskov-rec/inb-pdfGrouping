using PdfGrouping.Core.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfGrouping.Core.Services;

/// <summary>
/// Разбивает и объединяет страницы из одного или НЕСКОЛЬКИХ исходных PDF в отдельные выходные
/// файлы (по одному на группу). Каждый диапазон сам указывает свой исходный файл
/// (<see cref="PageRange.SourceFile"/>), поэтому один выходной файл может собираться из страниц
/// разных исходников. Полностью на PdfSharp (MIT) — без внешних утилит и без временных файлов.
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
            // Import — самый лёгкий из РЕАЛИЗОВАННЫХ режимов: InformationOnly в PdfSharp 6.x
            // не реализован (помечен устаревшим с указанием использовать Import).
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
    /// Для каждой группы создаёт отдельный PDF, склеивая страницы указанных диапазонов —
    /// возможно, из НЕСКОЛЬКИХ разных исходных файлов (каждый диапазон несёт свой SourceFile).
    /// Возвращает список путей к созданным файлам (по одному на группу).
    /// </summary>
    public List<string> SplitAndGroup(List<PdfGroup> groups, string outputDirectory)
    {
        if (groups is null || groups.Count == 0)
            throw new ArgumentException("Список групп пуст.");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Не указана папка для сохранения результатов.");

        Directory.CreateDirectory(outputDirectory);

        // Открываем каждый уникальный исходный файл ОДИН раз (страницы могут переиспользоваться
        // в нескольких группах) — Import-режим обязателен, чтобы переносить страницы в новые документы.
        var sources = new Dictionary<string, PdfDocument>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var group in groups)
                foreach (var range in group.Ranges)
                    OpenSourceIfNeeded(sources, range.SourceFile);

            ValidateGroups(groups, sources);

            var outputFiles = new List<string>();
            // Защита от совпадений: и между группами одного запуска, и с уже существующими на диске.
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                string baseName = SanitizeFileName(group.Label);

                using var outDoc = new PdfDocument();
                foreach (var range in group.Ranges)
                {
                    var source = sources[range.SourceFile];
                    for (int p = range.StartPage; p <= range.EndPage; p++)
                        outDoc.AddPage(source.Pages[p - 1]); // AddPage импортирует страницу
                }

                // CreateNew резервирует имя атомарно: файл, появившийся между проверкой
                // ResolveUniquePath и записью, не будет молча перезаписан — возьмём следующее имя.
                while (true)
                {
                    string outputPath = ResolveUniquePath(outputDirectory, baseName, usedNames);
                    try
                    {
                        using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write);
                        outDoc.Save(stream);
                        outputFiles.Add(outputPath);
                        break;
                    }
                    catch (IOException) when (File.Exists(outputPath))
                    {
                        // имя заняли параллельно — ResolveUniquePath выдаст следующий индекс
                    }
                }
            }

            return outputFiles;
        }
        finally
        {
            foreach (var doc in sources.Values)
                doc.Dispose();
        }
    }

    private static void OpenSourceIfNeeded(Dictionary<string, PdfDocument> sources, string path)
    {
        if (sources.ContainsKey(path))
            return;
        if (!File.Exists(path))
            throw new FileNotFoundException("PDF-файл не найден.", path);
        try
        {
            sources[path] = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        }
        catch (PdfReaderException ex)
        {
            throw new InvalidOperationException(
                $"Не удалось открыть PDF «{Path.GetFileName(path)}»: {ex.Message}", ex);
        }
    }

    private static void ValidateGroups(List<PdfGroup> groups, Dictionary<string, PdfDocument> sources)
    {
        foreach (var group in groups)
        {
            if (group.Ranges.Count == 0)
                throw new ArgumentException($"Группа «{group.Label}» не содержит диапазонов страниц.");

            foreach (var range in group.Ranges)
            {
                // Пустой/несуществующий SourceFile уже отсеян в OpenSourceIfNeeded (FileNotFoundException)
                // до попадания сюда — sources гарантированно содержит запись для любого диапазона.
                int totalPages = sources[range.SourceFile].PageCount;
                if (range.StartPage < 1 || range.EndPage < 1)
                    throw new ArgumentException($"Номера страниц должны быть >= 1: {range}");
                if (range.StartPage > range.EndPage)
                    throw new ArgumentException($"Начальная страница больше конечной: {range}");
                if (range.StartPage > totalPages || range.EndPage > totalPages)
                    throw new ArgumentException(
                        $"Страница вне диапазона (в файле «{Path.GetFileName(range.SourceFile)}» всего {totalPages}): {range}");
            }
        }
    }

    /// <summary>
    /// Подбирает свободное имя файла: если «base.pdf» уже занято (в этом запуске или на диске),
    /// добавляет индекс по порядку — «base (1).pdf», «base (2).pdf» и т.д.
    /// </summary>
    private static string ResolveUniquePath(string dir, string baseName, HashSet<string> usedNames)
    {
        string name = baseName;
        int i = 1;
        while (usedNames.Contains(name) || File.Exists(Path.Combine(dir, name + ".pdf")))
            name = $"{baseName} ({i++})";

        usedNames.Add(name);
        return Path.Combine(dir, name + ".pdf");
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
