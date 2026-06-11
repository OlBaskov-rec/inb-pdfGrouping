using Avalonia;
using System;
using System.Net;
using System.Net.Http;
using Velopack;

namespace PdfGrouping.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // На ряде корпоративных сетей системный прокси перехватывает TLS (подменяет сертификат),
        // из-за чего проверка обновлений падает с SSL-ошибкой. Приложение ходит в сеть только за
        // обновлениями GitHub, где прямое соединение работает и проверяется НАСТОЯЩИЙ сертификат,
        // поэтому обходим прокси по умолчанию для всего процесса (пустой WebProxy = без прокси).
        HttpClient.DefaultProxy = new WebProxy();

        // Должно идти ПЕРВЫМ: Velopack обрабатывает хуки установки/обновления
        // (--veloapp-install и т.п.) и при необходимости завершает процесс до старта UI.
        VelopackApp.Build().Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
