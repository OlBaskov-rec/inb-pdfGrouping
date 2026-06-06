namespace PdfGrouping.Core;

/// <summary>
/// Проверка метки группы на пригодность как имени файла в распространённых ОС
/// (Windows, Linux, macOS, iOS, Android).
/// </summary>
public static class FileNameValidator
{
    // Запрещённые символы (объединение ограничений Windows и Unix-подобных систем).
    private static readonly char[] Invalid = "<>:\"/\\|?*".ToCharArray();

    // Зарезервированные имена Windows.
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Возвращает текст ошибки или null, если метка допустима.</summary>
    public static string? Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Введите метку группы.";

        foreach (char c in name)
        {
            if (c < ' ')
                return "Метка содержит управляющий символ.";
            if (Invalid.Contains(c))
                return $"Символ «{c}» недопустим в имени файла.";
        }

        if (name.EndsWith(".") || name.EndsWith(" "))
            return "Метка не должна заканчиваться точкой или пробелом.";

        string trimmed = name.Trim();
        if (Reserved.Contains(trimmed))
            return $"«{trimmed}» — зарезервированное имя, выберите другое.";

        return null;
    }

    public static bool IsValid(string? name) => Validate(name) is null;
}
