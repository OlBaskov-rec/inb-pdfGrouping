using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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

        // Восстанавливаем ШИРИНУ окна с прошлого запуска (высоту не храним: она авто — окно
        // подгоняется по содержимому).
        var saved = WindowStateService.Load();
        if (saved is not null && saved.Width >= MinWidth)
            Width = saved.Width;

        ClampToScreen();

        // При авто-росте окна удерживаем его в пределах экрана (не заползать под панель задач).
        SizeChanged += (_, _) => KeepOnScreen();

        // Авто-высота: окно подгоняется под содержимое. Списки фиксированы, поэтому высота меняется
        // в основном при появлении/исчезновении сообщений в красной зоне. Когда содержимое выше
        // экрана — окно упирается в рабочую область, середина прокручивается, нижний бар закреплён.
        ContentScroll.LayoutUpdated += (_, _) => AdjustHeightForContent();
        Dispatcher.UIThread.Post(AdjustHeightForContent);

        // Проверку обновлений запускаем НЕЗАМЕТНО: после открытия окна и небольшой паузы,
        // чтобы старт интерфейса гарантированно ни на что не влиял. Сама проверка — вне UI-потока.
        _ = StartUpdateCheckDelayedAsync();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Сохраняем размер окна. Если открыта панель предпросмотра, она временно расширяет окно —
        // храним ширину «до предпросмотра», чтобы при следующем запуске не открываться слишком широким.
        double width = _viewModel.IsPreviewEnabled && _widthBeforePreview > 0 ? _widthBeforePreview : Width;
        WindowStateService.Save(width, Height); // высота сейчас авто; сохраняем для совместимости формата
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

        // WorkingArea — в физических пикселях; переводим в DIP.
        double scaling = screen.Scaling <= 0 ? 1.0 : screen.Scaling;

        // MaxHeight/MaxWidth в Avalonia ограничивают КЛИЕНТСКУЮ область; ОС добавляет сверху
        // заголовок и рамки. Чтобы полное окно не вылезало под панель задач, вычитаем их высоту.
        double osChromeH = OsChromeHeight();
        double maxH = screen.WorkingArea.Height / scaling - osChromeH - 2;
        double maxW = screen.WorkingArea.Width / scaling - 2;

        // MaxHeight — «понимание места на экране»: окно растёт под содержимое строго до рабочей
        // области (от верха экрана до панели задач, не заползая под неё); дальше — прокрутка.
        if (maxH > 0)
        {
            MaxHeight = maxH;
            if (MinHeight > maxH) MinHeight = maxH;
        }
        if (maxW > 0)
        {
            MaxWidth = maxW;
            if (MinWidth > maxW) MinWidth = maxW;
            if (Width > maxW) Width = maxW;
        }
    }

    /// <summary>Высота «обрамления» ОС (заголовок + рамки) = полный размер окна минус клиентская область.</summary>
    private double OsChromeHeight()
    {
        var fs = FrameSize;
        if (fs.HasValue && ClientSize.Height > 0)
        {
            double diff = fs.Value.Height - ClientSize.Height;
            if (diff > 0 && diff < 200) return diff;
        }
        return 40; // запасная оценка, если FrameSize ещё недоступен
    }

    private bool _adjustingHeight;

    /// <summary>
    /// Подгоняет высоту окна под фактическое содержимое: если контент не помещается в видимую
    /// область прокрутки — окно подрастает (строго до рабочей области экрана), если помещается
    /// с запасом — ужимается. Так окно растёт под зону сообщений и возвращается обратно, а при
    /// упоре в экран середина прокручивается (нижний бар «Обработать» остаётся закреплённым).
    /// </summary>
    private void AdjustHeightForContent()
    {
        if (_adjustingHeight || ContentScroll is null) return;

        double extent = ContentScroll.Extent.Height;     // полная высота содержимого
        double viewport = ContentScroll.Viewport.Height;  // видимая высота области прокрутки
        if (extent <= 0 || viewport <= 0) return;

        // chrome — всё вне области прокрутки: заголовок окна, верхняя панель, нижний бар, отступы.
        double chrome = Height - viewport;
        double maxH = MaxHeight > 0 && !double.IsInfinity(MaxHeight) ? MaxHeight : double.MaxValue;
        double target = Math.Clamp(extent + chrome, MinHeight, maxH);

        if (Math.Abs(target - Height) > 1.0)
        {
            _adjustingHeight = true;
            Height = target;
            _adjustingHeight = false;
        }
    }

    /// <summary>
    /// При авто-росте окна не даём ему уйти за нижний/правый край рабочей области (под панель задач):
    /// если выросшее окно вылезает — сдвигаем его вверх/влево, сохраняя видимость целиком.
    /// </summary>
    private void KeepOnScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        double scaling = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
        var wa = screen.WorkingArea; // пиксели
        // Полный размер окна (с заголовком ОС), иначе нижний край уедет под панель задач.
        double frameH = FrameSize?.Height ?? (Height + OsChromeHeight());
        double frameW = FrameSize?.Width ?? Width;
        int winH = (int)System.Math.Ceiling(frameH * scaling);
        int winW = (int)System.Math.Ceiling(frameW * scaling);

        int x = Position.X, y = Position.Y;
        if (y + winH > wa.Y + wa.Height) y = wa.Y + wa.Height - winH;
        if (x + winW > wa.X + wa.Width) x = wa.X + wa.Width - winW;
        if (y < wa.Y) y = wa.Y;
        if (x < wa.X) x = wa.X;

        if (x != Position.X || y != Position.Y)
            Position = new PixelPoint(x, y);
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
