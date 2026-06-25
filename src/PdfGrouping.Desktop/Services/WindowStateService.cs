using System;
using System.IO;
using System.Text.Json;

namespace PdfGrouping.Desktop.Services;

/// <summary>
/// Запоминает размер окна между запусками в %AppData%/PdfGrouping/window.json,
/// чтобы выбранный пользователем размер не «сбрасывался» к значению по умолчанию.
/// Позицию намеренно не храним (центрирование надёжнее на мульти-мониторных конфигурациях).
/// </summary>
public static class WindowStateService
{
    public sealed class Geometry
    {
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "PdfGrouping", "window.json");

    public static Geometry? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            using var stream = File.OpenRead(FilePath);
            var g = JsonSerializer.Deserialize<Geometry>(stream);
            return g is { Width: > 0, Height: > 0 } ? g : null;
        }
        catch { return null; }
    }

    public static void Save(double width, double height)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Geometry { Width = width, Height = height }));
        }
        catch { /* размер окна — не критичная настройка */ }
    }
}
