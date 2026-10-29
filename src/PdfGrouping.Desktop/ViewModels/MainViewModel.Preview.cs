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

    // ===== Предпросмотр исходного файла (без диапазона) и «переходящий фокус» =====
    // Фокус предпросмотра — либо на выбранном диапазоне (IsRangeFocus), либо на активном
    // исходном файле (IsSourcePreview). Одновременно подсвечено только одно из двух.

    /// <summary>Фокус предпросмотра на диапазоне из списка «Диапазоны страниц».</summary>
    [ObservableProperty]
    private bool _isRangeFocus;

    /// <summary>Фокус предпросмотра на активном файле из «Источники для обработки».</summary>
    [ObservableProperty]
    private bool _isSourcePreview;

    /// <summary>Первая из двух показываемых страниц исходного файла (запоминается по файлам).</summary>
    [ObservableProperty]
    private int _sourcePreviewPage = 1;

    [ObservableProperty]
    private int _startThumbPage;

    [ObservableProperty]
    private int _endThumbPage;

    private readonly Dictionary<string, int> _lastSourcePage = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressPageChange;

    private void UpdateFocusState()
    {
        IsRangeFocus = SelectedRange is not null;
        IsSourcePreview = SelectedRange is null && SelectedSourceFile is not null;
    }

    /// <summary>Вернуть фокус предпросмотра на исходный файл (клик по строке в списке источников).</summary>
    public void FocusSource()
    {
        if (SelectedRange is not null)
            SelectedRange = null;
    }

    /// <summary>Смена активного файла: показать его последние просматриваемые страницы (или с первой).</summary>
    private void OnSourceSwitchedForPreview(SourceFileEntry? value)
    {
        _suppressPageChange = true;
        SourcePreviewPage = value is not null && _lastSourcePage.TryGetValue(value.FilePath, out int p)
            ? Math.Clamp(p, 1, Math.Max(1, value.PageCount))
            : 1;
        _suppressPageChange = false;

        if (SelectedRange is not null)
            SelectedRange = null; // сам обновит фокус и предпросмотр
        else
        {
            UpdateFocusState();
            if (IsPreviewEnabled)
                _ = RefreshPreviewAsync();
        }
    }

    partial void OnSourcePreviewPageChanged(int value)
    {
        if (_suppressPageChange || SelectedSourceFile is null) return;

        int clamped = Math.Clamp(value, 1, Math.Max(1, SelectedSourceFile.PageCount));
        if (clamped != value)
        {
            SourcePreviewPage = clamped;
            return;
        }

        _lastSourcePage[SelectedSourceFile.FilePath] = value;
        if (IsSourcePreview && IsPreviewEnabled)
            _ = RefreshPreviewAsync();
    }

    partial void OnSelectedRangeChanged(PageRange? value)
    {
        PreviewRangeText = value is null
            ? string.Empty
            : L.Format("Preview_RangeInfo", value.PageCount, value.StartPage, value.EndPage);

        UpdateFocusState();

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
        string? path;
        int startPage, endPage; // endPage = 0 — второй страницы нет
        if (range is not null)
        {
            // Каждый диапазон несёт СВОЙ исходный файл: выбранный диапазон в списке может быть из
            // файла, отличного от текущего активного — предпросмотр обязан брать пример именно оттуда.
            path = range.SourceFile;
            startPage = range.StartPage;
            endPage = range.EndPage;
        }
        else if (SelectedSourceFile is { } file)
        {
            // Нет выбранного диапазона — смотрим сам активный файл: две подряд страницы.
            path = file.FilePath;
            startPage = Math.Clamp(SourcePreviewPage, 1, Math.Max(1, file.PageCount));
            endPage = startPage + 1 <= file.PageCount ? startPage + 1 : 0;
            PreviewRangeText = $"{L["Range_PagesShort"]} {startPage} / {file.PageCount}";
        }
        else
        {
            SetThumbs(null, null);
            return;
        }

        if (string.IsNullOrEmpty(path))
        {
            SetThumbs(null, null);
            return;
        }

        StartThumbPage = startPage;
        EndThumbPage = endPage;

        try
        {
            var (s, e) = await Task.Run(() =>
            {
                var sp = ImageHelper.ToBitmap(_renderService.RenderPage(path, startPage, 360, 1000));
                var ep = endPage > 0
                    ? ImageHelper.ToBitmap(_renderService.RenderPage(path, endPage, 360, 1000))
                    : null;
                return (sp, ep);
            });

            if (generation != _previewGeneration)
            {
                // Пока рендерили, выбрали другой диапазон — результат устарел, освобождаем.
                s.Dispose();
                e?.Dispose();
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
    private Task ZoomStartAsync() => OpenZoomAsync(StartThumbPage > 0 ? StartThumbPage : null);

    [RelayCommand]
    private Task ZoomEndAsync() => OpenZoomAsync(EndThumbPage > 0 ? EndThumbPage : null);

    private async Task OpenZoomAsync(int? page)
    {
        // Зум — из файла ВЫБРАННОГО диапазона (не обязательно текущего активного файла);
        // без диапазона — из активного исходного файла.
        var path = SelectedRange?.SourceFile ?? SelectedSourceFile?.FilePath;
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
