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

        // В ширину окно подстраивается при включении предпросмотра.
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsPreviewEnabled))
                AdjustWidthForPreview();
        };

        AdjustWidthForPreview();
    }

    /// <summary>
    /// Минимальная ширина окна, чтобы секции не сжимались внахлёст.
    /// При открытой панели предпросмотра она забирает ~312px слева — нужна бо́льшая ширина.
    /// Высоту не трогаем: панель предпросмотра прокручивается, окно не должно расти вниз.
    /// </summary>
    private void AdjustWidthForPreview()
    {
        double minW = _viewModel.IsPreviewEnabled ? 1190 : 880;

        MinWidth = minW;
        if (Width < minW) Width = minW;
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
