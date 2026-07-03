using System;
using System.IO;

namespace PdfGrouping.Desktop.Services;

/// <summary>
/// Простейший файловый лог в %AppData%/PdfGrouping/log.txt — для диагностики проблем у
/// пользователей (падения, ошибки сети/PDF, кривые переводы). Ошибки самого лога глотаются:
/// логирование никогда не должно ломать приложение.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private const long MaxBytes = 1024 * 1024; // ~1 МБ, потом ротация в log.old.txt

    private static string LogPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "PdfGrouping", "log.txt");

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} | {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                string path = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxBytes)
                    File.Move(path, Path.Combine(fi.DirectoryName!, "log.old.txt"), overwrite: true);

                File.AppendAllText(path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { /* лог не критичен */ }
    }
}
