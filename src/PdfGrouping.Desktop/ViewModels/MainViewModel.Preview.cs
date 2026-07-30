using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfGrouping.Core.Models;
using PdfGrouping.Desktop.Services;

namespace PdfGrouping.Desktop.ViewModels;

/// <summary>Предпросмотр страниц выбранного диапазона: миниатюры, увеличение, поворот.</summary>
public partial class MainViewModel
{
    [ObservableProperty]
    private bool _isPreviewEnabled;

    [ObservableProperty]
    private PageRange? _selectedRange;

    [ObservableProperty]
    private Bitmap? _startThumb;

    [ObservableProperty]
    private Bitmap? _endThumb;

    [ObservableProperty]
    private bool _isZoomOpen;

    [ObservableProperty]
    private Bitmap? _zoomImage;

    /// <summary>Угол поворота страницы в увеличенном просмотре (0/90/180/270°).</summary>
    [ObservableProperty]
    private double _zoomRotation;

    /// <summary>Строка-разделитель между миниатюрами, напр. «↕ 233 стр.  (112 → 344)».</summary>
    [ObservableProperty]
    private string _previewRangeText = string.Empty;

    /// <summary>Есть ли загруженные миниатюры (для подсказки в панели).</summary>
    [ObservableProperty]
    private bool _hasPreview;

    // Номер поколения предпросмотра: результат фонового рендера применяется, только если за время
    // работы не запросили другой диапазон (иначе показали бы миниатюры «чужого» диапазона).
    private int _previewGeneration;

    [RelayCommand]
    private void RotateZoomLeft() => ZoomRotation = ((ZoomRotation - 90) % 360 + 360) % 360;

    [RelayCommand]
    private void RotateZoomRight() => ZoomRotation = (ZoomRotation + 90) % 360;

    partial void OnIsPreviewEnabledChanged(bool value)
    {
        if (value)
            _ = RefreshPreviewAsync();
        else
        {
            _previewGeneration++; // невыполненные рендеры устарели
            SetThumbs(null, null);
        }
    }

    partial void OnSelectedRangeChanged(PageRange? value)
    {
        PreviewRangeText = value is null
            ? string.Empty
            : L.Format("Preview_RangeInfo", value.PageCount, value.StartPage, value.EndPage);

        if (IsPreviewEnabled)
            _ = RefreshPreviewAsync();
    }

    /// <summary>
    /// Заменяет миниатюры, освобождая прежние битмапы (иначе неуправляемые буферы копятся до GC).
    /// Освобождение отложено до конца цикла отрисовки — старый битмап может ещё рисоваться.
    /// </summary>
    private void SetThumbs(Bitmap? start, Bitmap? end)
    {
        var oldStart = StartThumb;
        var oldEnd = EndThumb;
        StartThumb = start;
        EndThumb = end;
        HasPreview = start is not null || end is not null;
        if (!ReferenceEquals(oldStart, start)) DisposeAfterRender(oldStart);
        if (!ReferenceEquals(oldEnd, end)) DisposeAfterRender(oldEnd);
    }

    private static void DisposeAfterRender(IDisposable? resource)
    {
        if (resource is null) return;
        Dispatcher.UIThread.Post(resource.Dispose, DispatcherPriority.Background);
    }

    private async Task RefreshPreviewAsync()
    {
        int generation = ++_previewGeneration;
        var range = SelectedRange;
        // Каждый диапазон несёт СВОЙ исходный файл: выбранный диапазон в списке может быть из
        // файла, отличного от текущего активного — предпросмотр обязан брать пример именно оттуда.
        var path = range?.SourceFile;

        if (range is null || string.IsNullOrEmpty(path))
        {
            SetThumbs(null, null);
            return;
        }

        int startPage = range.StartPage;
        int endPage = range.EndPage;

        try
        {
            var (s, e) = await Task.Run(() =>
            {
                var sp = ImageHelper.ToBitmap(_renderService.RenderPage(path, startPage, 360, 1000));
                var ep = ImageHelper.ToBitmap(_renderService.RenderPage(path, endPage, 360, 1000));
                return (sp, ep);
            });

            if (generation != _previewGeneration)
            {
                // Пока рендерили, выбрали другой диапазон — результат устарел, освобождаем.
                s.Dispose();
                e.Dispose();
                return;
            }

            SetThumbs(s, e);
        }
        catch (Exception ex)
        {
            if (generation == _previewGeneration)
                SetThumbs(null, null);
            AppLog.Error($"Не удалось построить предпросмотр стр. {startPage}/{endPage}", ex);
        }
    }

    [RelayCommand]
    private Task ZoomStartAsync() => OpenZoomAsync(SelectedRange?.StartPage);

    [RelayCommand]
    private Task ZoomEndAsync() => OpenZoomAsync(SelectedRange?.EndPage);

    private async Task OpenZoomAsync(int? page)
    {
        // Зум — из файла ВЫБРАННОГО диапазона (не обязательно текущего активного файла).
        var path = SelectedRange?.SourceFile;
        if (page is null || string.IsNullOrEmpty(path)) return;

        int p = page.Value;
        try
        {
            var big = await Task.Run(() =>
                ImageHelper.ToBitmap(_renderService.RenderPage(path, p, 1600, 2200)));
            ZoomRotation = 0; // каждый просмотр открываем без поворота
            var old = ZoomImage;
            ZoomImage = big;
            DisposeAfterRender(old); // крупный битмап (~14 МБ) освобождаем сразу, не ждём GC
            IsZoomOpen = true;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Не удалось открыть увеличенный просмотр стр. {p}", ex);
        }
    }

    /// <summary>Свернуть увеличенный просмотр (по клику в любом месте).</summary>
    public void CloseZoom()
    {
        IsZoomOpen = false;
        var old = ZoomImage;
        ZoomImage = null;
        DisposeAfterRender(old);
    }
}
