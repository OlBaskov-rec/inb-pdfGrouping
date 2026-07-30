using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfGrouping.Core.Services;
using PdfGrouping.Desktop.Localization;
using PdfGrouping.Desktop.Services;

namespace PdfGrouping.Desktop.ViewModels;

/// <summary>
/// Главная модель представления. Разнесена на partial-файлы по областям:
/// MainViewModel.SourceFiles.cs — список исходных PDF (панель слева), MainViewModel.Ranges.cs —
/// диапазоны, MainViewModel.Overlaps.cs — пересечения и сообщения, MainViewModel.Groups.cs —
/// группы и обработка, MainViewModel.Preview.cs — предпросмотр/зум, MainViewModel.Updates.cs —
/// обновления. Здесь — конструктор, язык, активный файл (производные свойства) и статус.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly PdfDocumentService _pdfService = new();
    private readonly PdfRenderService _renderService = new();
    private readonly IFilePickerService _filePicker;
    private readonly UpdateService _updateService;

    /// <summary>Доступ к локализатору.</summary>
    private static Localizer L => Localizer.Instance;

    public MainViewModel(IFilePickerService filePicker, UpdateService updateService)
    {
        _filePicker = filePicker;
        _updateService = updateService;

        // Высота списка диапазонов зависит от числа строк (3..5), дальше — прокрутка.
        Ranges.CollectionChanged += (_, _) => OnPropertyChanged(nameof(RangesListHeight));

        // Файловую метку у диапазонов показываем, только когда загружено больше одного файла —
        // иначе она лишний визуальный шум для типичного (единственный файл) сценария.
        // Номера («№») пересчитываются заново при любом добавлении/удалении файла.
        SourceFiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowFileTags));
            RenumberSourceFiles();
        };

        // Обновление зависящих от языка строк при переключении языка «на лету».
        L.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(LanguageShort));
            OnPropertyChanged(nameof(AppVersionText));
            OnPropertyChanged(nameof(PagesOfText));
            OnPropertyChanged(nameof(UpdateFoundButtonText));
            // Перерисовать динамические строки активных сообщений на новом языке.
            RebuildOverlapTexts();
            RebuildResolvedRanges();
            if (HasMergePrompt && _mergeTarget != null)
                MergePromptText = L.Format("Merge_Prompt", _mergeTarget.Label);
        };
    }

    // --- Язык интерфейса ---

    /// <summary>Список языков для меню выбора.</summary>
    public IReadOnlyList<Localizer.LanguageOption> Languages => Localizer.Languages;

    /// <summary>Краткая подпись текущего языка для кнопки.</summary>
    public string LanguageShort => L.CurrentShort;

    [RelayCommand]
    private void SetLanguage(string code) => L.SetLanguage(code);

    // --- Активный исходный файл (производные от SelectedSourceFile — см. MainViewModel.SourceFiles.cs) ---

    /// <summary>Путь активного файла (для поля «Путь к файлу» и как ключ рендера предпросмотра/зума).</summary>
    [ObservableProperty]
    private string _sourceFilePath = string.Empty;

    /// <summary>Число страниц активного файла — определяет пределы ввода диапазона.</summary>
    [ObservableProperty]
    private int _totalPages;

    /// <summary>Максимум для полей ввода страниц (минимум 1, чтобы NumericUpDown был корректен).</summary>
    public decimal MaxPage => Math.Max(1, TotalPages);

    /// <summary>Локализованная подпись «Страницы (из N):» (число в середине — через шаблон).</summary>
    public string PagesOfText => L.Format("Ranges_PagesOf", TotalPages);

    partial void OnTotalPagesChanged(int value)
    {
        OnPropertyChanged(nameof(MaxPage));
        OnPropertyChanged(nameof(PagesOfText));
    }

    // --- Статус ---

    [ObservableProperty]
    private string _statusText = Localizer.Instance.Get("Status_Ready");

    [ObservableProperty]
    private bool _statusIsError;

    // Рутинные подтверждения у кнопок больше не выводятся: любое инфо-сообщение
    // лишь скрывает баннер ошибки. Ошибки выводятся в общую область сообщений (баннер).
    private void SetInfo(string text)
    {
        StatusIsError = false;
        StatusText = text;
        HasBlockMessage = false;
    }

    private void SetError(string text)
    {
        StatusIsError = true;
        StatusText = text;
        ShowBlockMessage(text);
    }

    // --- Сброс ---

    /// <summary>Сбрасывает все рабочие данные (диапазоны, группы, предупреждения, предпросмотр).</summary>
    private void ResetWorkspace()
    {
        Ranges.Clear();
        Groups.Clear();
        OutputFiles.Clear();
        HasResults = false;
        ClearOverlapState();
        _mergeTarget = null;
        HasMergePrompt = false;
        SelectedRange = null;
        _previewGeneration++;
        SetThumbs(null, null);
        CloseZoom();
        GroupLabelText = string.Empty;
    }

    [RelayCommand]
    private void ClearAll()
    {
        ResetWorkspace();
        SourceFiles.Clear();
        SelectedSourceFile = null; // сбрасывает SourceFilePath/TotalPages/RangeStart/RangeEnd
        OutputDirectory = string.Empty;
        SetInfo(L["Status_Ready"]);
    }
}
