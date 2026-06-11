using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace PdfGrouping.Desktop.Localization;

/// <summary>
/// XAML-расширение перевода: <c>{l:Tr Source_Title}</c>.
/// Привязывается к свойству-счётчику <see cref="Localizer.Revision"/> (меняется при смене языка)
/// и через конвертер возвращает перевод ключа на текущем языке — обновление «на лету» без перезапуска.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    private static readonly KeyConverter Converter = new();

    public TrExtension() { }
    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding(nameof(Localizer.Revision))
        {
            Source = Localizer.Instance,
            Mode = BindingMode.OneWay,
            Converter = Converter,
            ConverterParameter = Key,
        };
    }

    private sealed class KeyConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Localizer.Instance.Get(parameter as string ?? string.Empty);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => null;
    }
}
