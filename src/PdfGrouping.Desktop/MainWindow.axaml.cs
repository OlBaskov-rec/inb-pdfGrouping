using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using PdfGrouping.Desktop.Localization;
using PdfGrouping.Desktop.Services;
using PdfGrouping.Desktop.ViewModels;

namespace PdfGrouping.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        UpdateTitle();
        Localizer.Instance.LanguageChanged += (_, _) => UpdateTitle();

        _viewModel = new MainViewModel(new StorageProviderFilePicker(() => this), new UpdateService());
        DataContext = _viewModel;

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // В ширину окно подстраивается при включении предпросмотра.
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsPreviewEnabled))
                AdjustWidthForPreview();
        };

        AdjustWidthForPreview();
    }

    /// <summary>
    /// После открытия окна подгоняем его под рабочую область экрана: на мониторах с низким
    /// разрешением окно (и его минимальные размеры) не должны превышать экран — иначе нижние
    /// кнопки уезжают за край без возможности прокрутки. Если окно не помещается, оно ужимается,
    /// а внутренняя прокрутка (ScrollViewer) даёт доступ ко всему содержимому.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Восстанавливаем размер окна с прошлого запуска (если сохранён и проходит по минимумам),
        // затем ClampToScreen ужмёт его под рабочую область, если экран меньше.
        var saved = WindowStateService.Load();
        if (saved is not null && saved.Width >= MinWidth && saved.Height >= MinHeight)
        {
            Width = saved.Width;
            Height = saved.Height;
        }

        ClampToScreen();

        // Проверку обновлений запускаем НЕЗАМЕТНО: после открытия окна и небольшой паузы,
        // чтобы старт интерфейса гарантированно ни на что не влиял. Сама проверка — вне UI-потока.
        _ = StartUpdateCheckDelayedAsync();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Сохраняем размер окна. Если открыта панель предпросмотра, она временно расширяет окно —
        // храним ширину «до предпросмотра», чтобы при следующем запуске не открываться слишком широким.
        double width = _viewModel.IsPreviewEnabled && _widthBeforePreview > 0 ? _widthBeforePreview : Width;
        WindowStateService.Save(width, Height);
        base.OnClosing(e);
    }

    private async System.Threading.Tasks.Task StartUpdateCheckDelayedAsync()
    {
        await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(3));
        await _viewModel.CheckForUpdatesAsync();
    }

    private void ClampToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        // WorkingArea — в физических пикселях; переводим в DIP. Небольшой запас на рамку окна.
        double scaling = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
        double maxH = screen.WorkingArea.Height / scaling - 8;
        double maxW = screen.WorkingArea.Width / scaling - 8;

        if (maxH > 0)
        {
            if (MinHeight > maxH) MinHeight = maxH;
            if (Height > maxH) Height = maxH;
        }
        if (maxW > 0)
        {
            if (MinWidth > maxW) MinWidth = maxW;
            if (Width > maxW) Width = maxW;
        }
    }

    private void UpdateTitle() =>
        Title = $"PDF Grouping v{GetAppVersion()} — {Localizer.Instance.Get("Win_Subtitle")}";

    private double _widthBeforePreview;

    /// <summary>
    /// Минимальная ширина окна, чтобы секции не сжимались внахлёст.
    /// При открытой панели предпросмотра она забирает ~312px слева — нужна бо́льшая ширина.
    /// При отключении предпросмотра возвращаем прежнюю ширину (окно не должно остаться шире).
    /// Высоту не трогаем: панель предпросмотра прокручивается, окно не должно расти вниз.
    /// </summary>
    private void AdjustWidthForPreview()
    {
        if (_viewModel.IsPreviewEnabled)
        {
            _widthBeforePreview = Width;     // запоминаем ширину до открытия панели
            MinWidth = 1190;
            if (Width < 1190) Width = 1190;
        }
        else
        {
            MinWidth = 880;
            double target = _widthBeforePreview > 0 ? _widthBeforePreview : Width;
            Width = System.Math.Max(880, System.Math.Min(Width, target));
        }

        // На узких экранах не даём окну вылезти за рабочую область (ширина с открытой панелью велика).
        ClampToScreen();
    }

    private void StartThumb_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel.ZoomStartCommand.CanExecute(null))
            _viewModel.ZoomStartCommand.Execute(null);
        e.Handled = true;
    }

    private void EndThumb_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel.ZoomEndCommand.CanExecute(null))
            _viewModel.ZoomEndCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>Версия приложения из сборки (источник — &lt;Version&gt; в csproj).</summary>
    private static string GetAppVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        // InformationalVersion может содержать суффикс сборки (+hash) — отбрасываем его.
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
            return info.Split('+')[0];

        var v = asm.GetName().Version;
        return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void ZoomOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Клик по кнопкам поворота не должен закрывать просмотр.
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;

        _viewModel.CloseZoom();
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File))
            return;

        var file = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault();
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
            _viewModel.LoadFromPath(path);
    }
}
