using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Avalonia.Platform;

namespace PdfGrouping.Desktop.Localization;

/// <summary>
/// Локализация интерфейса с переключением языка «на лету».
/// Строки лежат в Assets/i18n/{code}.json (встроенные ресурсы). Выбор языка сохраняется
/// в %AppData%/PdfGrouping/settings.json. Любой отсутствующий ключ падает на русский, затем на сам ключ.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    /// <summary>Язык: код, краткая подпись для кнопки, родное название для меню.</summary>
    public sealed record LanguageOption(string Code, string Short, string Native);

    /// <summary>Список поддерживаемых языков.</summary>
    public static readonly LanguageOption[] Languages =
    {
        new("ru", "RU", "Русский"),
        new("en", "EN", "English"),
        new("fr", "FR", "Français"),
        new("es", "ES", "Español"),
        new("de", "DE", "Deutsch"),
        new("it", "IT", "Italiano"),
        new("zh", "中", "中文"),
        new("ja", "日", "日本語"),
        new("ko", "한", "한국어"),
    };

    // Объявлено ПОСЛЕ Languages: статические поля инициализируются по порядку,
    // а конструктор Instance использует Languages.
    public static Localizer Instance { get; } = new();

    private const string Fallback = "ru";

    private readonly Dictionary<string, Dictionary<string, string>> _all = new();
    private string _lang = Fallback;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    /// <summary>Счётчик смен языка. Привязки {l:Tr} следят за ним и перечитывают перевод.</summary>
    public int Revision { get; private set; }

    private Localizer()
    {
        foreach (var opt in Languages)
            _all[opt.Code] = Load(opt.Code);

        _lang = LoadSavedLanguage() ?? DetectSystemLanguage();
        if (!_all.ContainsKey(_lang)) _lang = Fallback;
    }

    /// <summary>Текущий код языка (напр. «en»).</summary>
    public string Current => _lang;

    /// <summary>Краткая подпись текущего языка для кнопки (напр. «EN», «中»).</summary>
    public string CurrentShort
    {
        get
        {
            foreach (var opt in Languages)
                if (opt.Code == _lang) return opt.Short;
            return _lang.ToUpperInvariant();
        }
    }

    /// <summary>Индексатор для XAML-привязок: {Binding [Key], Source=...}.</summary>
    public string this[string key] => Get(key);

    public string Get(string key)
    {
        if (_all.TryGetValue(_lang, out var dict) && dict.TryGetValue(key, out var value))
            return value;
        if (_all.TryGetValue(Fallback, out var ru) && ru.TryGetValue(key, out var rv))
            return rv;
        return key;
    }

    /// <summary>Локализованный шаблон с подстановкой аргументов ({0}, {1}…).</summary>
    public string Format(string key, params object?[] args) => string.Format(Get(key), args);

    public void SetLanguage(string code)
    {
        if (_lang == code || !_all.ContainsKey(code)) return;
        _lang = code;
        Revision++;
        // Привязки {l:Tr} следят за Revision и перечитывают перевод через конвертер.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Revision)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentShort)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        SaveLanguage(code);
    }

    private static Dictionary<string, string> Load(string code)
    {
        try
        {
            // Имя сборки берём динамически: <AssemblyName> = «PdfGrouping», а не «PdfGrouping.Desktop».
            var asm = typeof(Localizer).Assembly.GetName().Name;
            var uri = new Uri($"avares://{asm}/Assets/i18n/{code}.json");
            using var stream = AssetLoader.Open(uri);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            return dict ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static string DetectSystemLanguage()
    {
        var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        foreach (var opt in Languages)
            if (opt.Code == two) return opt.Code;
        return Fallback;
    }

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "PdfGrouping", "settings.json");

    private static string? LoadSavedLanguage()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            using var stream = File.OpenRead(SettingsPath);
            var doc = JsonSerializer.Deserialize<Settings>(stream);
            return string.IsNullOrWhiteSpace(doc?.Lang) ? null : doc!.Lang;
        }
        catch { return null; }
    }

    private static void SaveLanguage(string code)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings { Lang = code }));
        }
        catch { /* настройки не критичны */ }
    }

    private sealed class Settings
    {
        public string? Lang { get; set; }
    }
}
