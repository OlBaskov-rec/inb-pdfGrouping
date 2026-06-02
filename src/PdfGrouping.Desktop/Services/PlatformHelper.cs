using System.Diagnostics;

namespace PdfGrouping.Desktop.Services;

/// <summary>Кросс-платформенные системные действия.</summary>
public static class PlatformHelper
{
    /// <summary>Открывает папку в системном файловом менеджере (Explorer / Finder / xdg-open).</summary>
    public static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", $"\"{path}\"");
            else
                Process.Start("xdg-open", $"\"{path}\"");
        }
        catch { /* открытие папки — не критично */ }
    }
}
