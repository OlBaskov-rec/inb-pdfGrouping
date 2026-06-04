using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using PdfGrouping.Desktop.Services;
using PdfGrouping.Desktop.ViewModels;

namespace PdfGrouping.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        Title = $"PDF Grouping v{GetAppVersion()} — Разделение и группировка PDF";

        _viewModel = new MainViewModel(new StorageProviderFilePicker(() => this), new UpdateService());
        DataContext = _viewModel;

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Фоновая проверка обновлений (no-op в dev-запуске).
        _ = _viewModel.CheckForUpdatesAsync();

        // Окно подстраивается: вниз — при предупреждениях, в ширину — при предпросмотре.
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.HasOverlapWarning))
                AdjustHeightForWarnings();
            else if (e.PropertyName == nameof(MainViewModel.IsPreviewEnabled))
                AdjustWidthForPreview();
        };
        _viewModel.Overlaps.CollectionChanged += (_, _) => AdjustHeightForWarnings();

        AdjustWidthForPreview();
    }

    /// <summary>
    /// Минимальные размеры окна, чтобы интерфейс не сжимался внахлёст.
    /// При открытой панели предпросмотра требуется больше ширины/высоты.
    /// </summary>
    private void AdjustWidthForPreview()
    {
        double minW = _viewModel.IsPreviewEnabled ? 1090 : 780;
        double minH = _viewModel.IsPreviewEnabled ? 720 : 660;

        MinWidth = minW;
        MinHeight = minH;

        if (Width < minW) Width = minW;
        if (Height < minH) Height = minH;
    }

    private double? _baselineHeight;
    private bool _adjustingHeight;

    private void AdjustHeightForWarnings()
    {
        // Меняем высоту окна вне текущего прохода разметки.
        Avalonia.Threading.Dispatcher.UIThread.Post(ApplyHeightForWarnings,
            Avalonia.Threading.DispatcherPriority.Normal);
    }

    private void ApplyHeightForWarnings()
    {
        if (_adjustingHeight)
            return;

        _adjustingHeight = true;
        try
        {
            if (_viewModel.HasOverlapWarning)
            {
                _baselineHeight ??= Height;
                int lines = System.Math.Max(1, _viewModel.Overlaps.Count);
                double target = System.Math.Min(
                    (_baselineHeight ?? Height) + 160 + lines * 32,
                    MaxAllowedHeight());
                if (System.Math.Abs(Height - target) > 1)
                    Height = target;
            }
            else if (_baselineHeight is double h)
            {
                _baselineHeight = null;
                if (System.Math.Abs(Height - h) > 1)
                    Height = h;
            }
        }
        finally
        {
            _adjustingHeight = false;
        }
    }

    private double MaxAllowedHeight()
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen is null)
            return 1200;
        return screen.WorkingArea.Height / RenderScaling - 48;
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
