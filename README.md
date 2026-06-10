# PDF Grouping — Разделение и группировка PDF

Кросс-платформенное приложение для разделения PDF-документа на группы страниц по заданным
диапазонам и склейки каждой группы в отдельный PDF-файл.

## Возможности

- Разделение PDF на группы через указание **диапазонов страниц**; склейка диапазонов группы
  в один файл с заданной **меткой** (имя файла, до 15 символов, проверка на недопустимые символы)
- **Предпросмотр** начальной и конечной страницы выбранного диапазона + увеличение
- **Предупреждения о пересечении** страниц (с указанием конкретных дублей) и режим
  **«Без пересечений»**, запрещающий выбирать пересекающиеся страницы
- **Объединение** выбранных диапазонов в уже существующую группу (по запросу)
- Быстрый выбор метки кнопками A–F; Drag & Drop PDF; список результатов и открытие папки
- **Авто-обновление** (Velopack, GitHub Releases); окно «О программе»
- **Без внешних зависимостей** — весь разбор PDF встроенный (без qpdf)

## Технологии

| Компонент | Технология | Лицензия |
|-----------|-----------|----------|
| UI | **Avalonia 12** (.NET 10) — Windows / macOS / Linux | MIT |
| MVVM | CommunityToolkit.Mvvm | MIT |
| Работа с PDF | **PdfSharp 6** (split / merge) | MIT |
| Тесты | xUnit | Apache-2.0 |

Полностью свободные лицензии (MIT/Apache), без AGPL и без внешних утилит (qpdf не требуется).

## Архитектура

```
src/
  PdfGrouping.Core/            # Класс-библиотека без UI (кросс-платформенная, тестируемая)
    Models/                    #   PageRange, PdfGroup
    Services/PdfDocumentService.cs   # Движок split/merge на PdfSharp
  PdfGrouping.Desktop/         # Avalonia-приложение (Win/macOS), ссылается на Core
    ViewModels/MainViewModel.cs
    Services/                  #   StorageProviderFilePicker, PlatformHelper
    Converters/
    MainWindow.axaml(.cs)
tests/
  PdfGrouping.Core.Tests/      # xUnit-тесты движка
```

Ядро (`PdfGrouping.Core`) не зависит от UI-фреймворка — это упрощает тестирование и будущий
перенос на macOS (UI на Avalonia уже кросс-платформенный).

## Требования

- **.NET 10 SDK** — для сборки ([скачать](https://dotnet.microsoft.com/download/dotnet/10.0))
- Для запуска portable-сборки рантайм не нужен (self-contained, см. ниже)

## Сборка и запуск (для разработки)

```bash
# Сборка всего решения
dotnet build PdfGrouping.sln -c Release

# Запуск приложения
dotnet run --project src/PdfGrouping.Desktop

# Тесты
dotnet test
```

## Использование

1. **Откройте PDF-файл** (кнопка «Обзор…» или перетащите файл в окно)
2. **Добавьте диапазоны страниц** (начальная–конечная, «+ Добавить диапазон»).
   Несвязанные диапазоны (`1–3`, `10–15`) добавляются последовательно. При пересечении
   с уже выбранными — появляется предупреждение; «👁 Предпросмотр» показывает страницы
3. **Задайте метку группы** (поле или кнопки A–F) и нажмите «Создать группу ►».
   Если имя совпадает с существующей группой — будет предложено добавить диапазоны в неё
4. Повторите шаги 2–3 для остальных групп
5. **Выберите папку** для сохранения и нажмите **«▶ Обработать»**
6. Готовые файлы появятся в списке; кнопка «📂 Открыть папку» откроет результаты

### Пример

Исходный PDF из 50 страниц → 3 документа:

| Метка | Диапазоны | Результат |
|-------|-----------|-----------|
| **A** | 1–10, 25–30 | `A.pdf` (16 стр.) |
| **B** | 11–24 | `B.pdf` (14 стр.) |
| **C** | 31–50 | `C.pdf` (20 стр.) |

## Portable-сборка и обновления (Velopack)

Распространение — **portable self-contained** сборка с авто-обновлением через
**[Velopack](https://velopack.io)**. Фид обновлений — **GitHub Releases** публичного
репозитория (токен для скачивания не нужен).

### Как это работает

- `VelopackApp.Build().Run()` вызывается первым в [Program.cs](src/PdfGrouping.Desktop/Program.cs)
  и обрабатывает хуки установки/обновления.
- При старте приложение в фоне проверяет GitHub Releases
  ([UpdateService.cs](src/PdfGrouping.Desktop/Services/UpdateService.cs)); если есть новая
  версия — тихо скачивает её и показывает баннер **«⟳ Обновить и перезапустить»**.
- В режиме разработки (`dotnet run`) проверка обновлений — безопасный no-op.

### Сборка релиза

> 📋 Пошаговая памятка по выпуску версии (сборка, подпись, публикация ассетов в
> GitHub Releases, проверка обновления) — в [docs/RELEASING.md](docs/RELEASING.md).

Инструмент `vpk` подключён через манифест `.config/dotnet-tools.json` (восстанавливается
автоматически). Скрипт упаковки:

```powershell
# Локальная сборка (Setup.exe + Portable.zip + пакет обновления в .\Releases)
pwsh build/pack-win.ps1 -Version 0.1.8

# Сборка и публикация релиза в GitHub Releases (нужен PAT-токен)
$env:GITHUB_TOKEN = "<ghp_...>"
pwsh build/pack-win.ps1 -Version 0.1.8 -Upload
```

Артефакты в `.\Releases`:
- `PdfGrouping-win-Portable.zip` — portable-приложение (со встроенным `Update.exe`);
- `PdfGrouping-win-Setup.exe` — установщик;
- `*-full.nupkg`, `RELEASES`, `releases.win.json` — пакет и метаданные фида обновлений.

> **Версионирование:** поднимайте `<Version>` в
> [PdfGrouping.Desktop.csproj](src/PdfGrouping.Desktop/PdfGrouping.Desktop.csproj) (или
> передавайте `-Version`) перед каждым релизом — Velopack обновляет клиентов только на
> бóльшую версию.

### Токен GitHub для публикации

Нужен только для `-Upload`. Создание: **GitHub → Settings → Developer settings →
Personal access tokens → Fine-grained tokens → Generate new token**, доступ к репозиторию
`inb-pdfGrouping`, разрешение **Contents: Read and write**. Полученный `ghp_…`/`github_pat_…`
положить в `GITHUB_TOKEN` (не коммитить в репозиторий).

### Подпись кода (самоподписанный, внутренний)

Сборки подписываются **самоподписанным** сертификатом (CN=PDF Grouping (Internal)) — приватный
ключ хранится в Windows-хранилище машины-сборщика (`Cert:\CurrentUser\My`), в репозиторий
попадает только **публичная** часть [build/PdfGrouping-PublicCert.cer](build/PdfGrouping-PublicCert.cer).

Подпись при упаковке (отпечаток подставьте свой, если пересоздавали сертификат):

```powershell
pwsh build/pack-win.ps1 -Version 0.1.8 `
  -SignParams "/fd SHA256 /sha1 <THUMBPRINT>"
```

Чтобы Windows считала подпись действительной (без «Неизвестного издателя»), на **каждой**
рабочей машине нужно один раз установить публичный сертификат в доверенные:

```powershell
pwsh build/trust-cert.ps1            # для текущего пользователя
pwsh build/trust-cert.ps1 -Machine   # для всей машины (от администратора)
```

> Самоподписанная подпись доверенна только там, где установлен этот публичный сертификат.
> Для доверия «из коробки» на любых машинах нужен коммерческий сертификат (OV/EV) или
> Azure Trusted Signing — плумбинг в `pack-win.ps1` (`-SignParams`) уже это поддерживает.

#### Пересоздать сертификат (при необходимости)

```powershell
$c = New-SelfSignedCertificate -Type CodeSigningCert `
  -Subject "CN=PDF Grouping (Internal), O=INB" `
  -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(5) `
  -KeyExportPolicy Exportable -HashAlgorithm SHA256
Export-Certificate -Cert $c -FilePath build/PdfGrouping-PublicCert.cer -Type CERT
$c.Thumbprint
```

### macOS (планируется)

Та же кодовая база (Avalonia + PdfSharp кросс-платформенны). Сборка под mac:
`dotnet publish -r osx-arm64` (или `osx-x64`) + `vpk pack` на macOS — отдельным этапом после
стабилизации Windows-версии.

## Лицензия

Все используемые библиотеки распространяются под свободными лицензиями MIT / Apache-2.0.
