using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfGrouping.Desktop.Services;

namespace PdfGrouping.Desktop.ViewModels;

/// <summary>Авто-обновление (Velopack): фоновая и ручная проверка, скачивание, установка.</summary>
public partial class MainViewModel
{
    /// <summary>Обновление найдено (мигаем значком «ℹ», в меню — кнопка «Обнаружено обновление»).</summary>
    [ObservableProperty]
    private bool _isUpdateAvailable;

    /// <summary>Обновление готово к установке (показан баннер внизу с кнопками).</summary>
    [ObservableProperty]
    private bool _isUpdateReady;

    [ObservableProperty]
    private string _updateText = string.Empty;

    [ObservableProperty]
    private string _updateCheckStatus = string.Empty;

    private bool _updateDownloaded;

    // Защёлка от параллельных проверок (авто при старте + ручная из меню): UpdateService
    // хранит найденное обновление в одном поле и не рассчитан на конкурентный доступ.
    private int _updateBusy;

    /// <summary>Версия скачанного/найденного обновления (для сообщений).</summary>
    private string _availableVersion = string.Empty;

    /// <summary>Показывать кнопку ручной проверки (когда обновление ещё не найдено).</summary>
    public bool ShowCheckButton => !IsUpdateAvailable;

    /// <summary>Показывать зелёную кнопку «Обнаружено обновление» (найдено, но баннер ещё не показан).</summary>
    public bool ShowRevealButton => IsUpdateAvailable && !IsUpdateReady;

    /// <summary>Текст зелёной кнопки в меню, напр. «Обнаружено обновление 0.1.30».</summary>
    public string UpdateFoundButtonText => L.Format("Btn_UpdateFound", _availableVersion);

    /// <summary>Версия приложения (из сборки) для окна «О программе».</summary>
    public string AppVersion { get; } = AppInfo.Version;

    /// <summary>Локализованная строка «Версия X» (обновляется при смене языка).</summary>
    public string AppVersionText => L.Format("About_Version", AppVersion);

    partial void OnIsUpdateAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCheckButton));
        OnPropertyChanged(nameof(ShowRevealButton));
        OnPropertyChanged(nameof(UpdateFoundButtonText));
    }

    partial void OnIsUpdateReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCheckButton));
        OnPropertyChanged(nameof(ShowRevealButton));
    }

    [RelayCommand]
    private void ApplyUpdate() => _updateService.ApplyAndRestart();

    /// <summary>«Отложить»: скрыть баннер внизу. Значок остаётся зелёным — обновиться можно позже.</summary>
    [RelayCommand]
    private void PostponeUpdate() => IsUpdateReady = false;

    /// <summary>Ручная проверка обновления из окна «О программе» (когда автоматически не найдено).</summary>
    [RelayCommand]
    private async Task CheckUpdatesManualAsync()
    {
        if (!_updateService.IsSupported)
        {
            UpdateCheckStatus = L["Upd_OnlyInstalled"];
            return;
        }

        if (System.Threading.Interlocked.Exchange(ref _updateBusy, 1) == 1)
        {
            UpdateCheckStatus = L["Upd_Checking"]; // проверка уже идёт (фоновая при старте)
            return;
        }

        UpdateCheckStatus = L["Upd_Checking"];
        try
        {
            // Таймаут на проверку: иначе при недоступной сети статус навсегда застрял бы
            // на «Проверка…», а кнопка осталась бы заблокированной (RelayCommand).
            var version = await RunWithTimeout(() => _updateService.CheckAsync(), 30);
            if (version is null)
            {
                UpdateCheckStatus = L["Upd_Latest"];
                return;
            }

            _availableVersion = version;
            IsUpdateAvailable = true;
            UpdateCheckStatus = L.Format("Upd_Found", version);
            await RunWithTimeout(() => _updateService.DownloadAsync(), 120);
            _updateDownloaded = true;

            // Ручная проверка — это явное действие пользователя, сразу показываем баннер.
            UpdateText = L.Format("Upd_ReadyText", version);
            UpdateCheckStatus = L.Format("Upd_Downloaded", version);
            IsUpdateReady = true;
        }
        catch (TimeoutException)
        {
            UpdateCheckStatus = IsUpdateReady
                ? L.Format("Upd_Downloaded", _availableVersion)
                : L["Upd_Timeout"];
        }
        catch (Exception ex)
        {
            AppLog.Error("Ручная проверка обновлений не удалась", ex);
            UpdateCheckStatus = IsUpdateReady
                ? L.Format("Upd_Downloaded", _availableVersion)
                : L.Format("Upd_Failed", DescribeError(ex));
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _updateBusy, 0);
        }
    }

    /// <summary>«Обнаружено обновление» в меню: докачивает (если нужно) и показывает баннер внизу.</summary>
    [RelayCommand]
    private async Task RevealUpdateAsync()
    {
        if (!IsUpdateAvailable) return;
        if (!_updateDownloaded)
        {
            UpdateCheckStatus = L.Format("Upd_Found", _availableVersion);
            try
            {
                await RunWithTimeout(() => _updateService.DownloadAsync(), 120);
                _updateDownloaded = true;
            }
            catch (TimeoutException)
            {
                UpdateCheckStatus = L["Upd_Timeout"];
                return;
            }
            catch (Exception ex)
            {
                AppLog.Error("Скачивание обновления не удалось", ex);
                UpdateCheckStatus = L.Format("Upd_Failed", DescribeError(ex));
                return;
            }
        }

        UpdateText = L.Format("Upd_ReadyText", _availableVersion);
        UpdateCheckStatus = L.Format("Upd_Downloaded", _availableVersion);
        IsUpdateReady = true;
    }

    /// <summary>
    /// Фоновая проверка обновлений при старте. В dev-запуске — безопасный no-op.
    /// Находит и (для удобства) скачивает обновление в фоне, но НЕ показывает баннер сразу —
    /// лишь подсвечивает значок «ℹ». Баннер появится после «Обнаружено обновление» в меню.
    /// Ошибки сети не мешают работе приложения.
    /// </summary>
    public async Task CheckForUpdatesAsync()
    {
        if (System.Threading.Interlocked.Exchange(ref _updateBusy, 1) == 1)
            return; // проверка уже идёт
        try
        {
            // ВСЯ работа Velopack (включая синхронные участки и сеть) — на пуле потоков, НИКОГДА
            // не на UI-потоке, плюс таймаут: запуск приложения не должен подвисать из-за проверки.
            var checkTask = Task.Run(() => _updateService.CheckAsync());
            if (await Task.WhenAny(checkTask, Task.Delay(TimeSpan.FromSeconds(20))) != checkTask)
                return; // сеть не ответила вовремя — тихо выходим, приложение работает

            var version = await checkTask;
            if (version is null)
                return;

            _availableVersion = version;
            IsUpdateAvailable = true; // мигание значка «ℹ» (продолжение — на UI-потоке)

            // Фоновое скачивание — чтобы по «Обнаружено обновление» применилось мгновенно.
            try { await Task.Run(() => _updateService.DownloadAsync()); _updateDownloaded = true; }
            catch (Exception ex) { AppLog.Error("Фоновое скачивание обновления не удалось (докачаем по требованию)", ex); }
        }
        catch (Exception ex)
        {
            // Обновление не критично для основной работы — только фиксируем в логе.
            AppLog.Error("Фоновая проверка обновлений не удалась", ex);
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _updateBusy, 0);
        }
    }

    /// <summary>Разворачивает цепочку вложенных исключений в одну строку (для диагностики).</summary>
    private static string DescribeError(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (sb.Length > 0) sb.Append(" → ");
            sb.Append(e.Message);
        }
        return sb.ToString();
    }

    /// <summary>Выполняет работу на пуле потоков с таймаутом; по истечении бросает TimeoutException.</summary>
    private static async Task<T> RunWithTimeout<T>(Func<Task<T>> work, int seconds)
    {
        var task = Task.Run(work);
        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds))) != task)
            throw new TimeoutException();
        return await task;
    }

    /// <summary>Перегрузка для операций без результата.</summary>
    private static async Task RunWithTimeout(Func<Task> work, int seconds)
    {
        var task = Task.Run(work);
        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds))) != task)
            throw new TimeoutException();
        await task;
    }
}
