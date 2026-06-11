# Выпуск новой версии PDF Grouping

Памятка по сборке и публикации релиза. Авто-обновление работает через **Velopack**,
источник обновлений — раздел **GitHub Releases** (не дерево файлов репозитория).

---

## Откуда программа берёт обновления

`UpdateService` использует `GithubSource` →
`https://github.com/OlBaskov-rec/inb-pdfGrouping/releases`.

Velopack через GitHub API находит **последний релиз-тег** и качает прикреплённые к нему
**ассеты**: `RELEASES`, `*-full.nupkg`, `*-delta.nupkg`, `assets.win.json`.

> ⚠️ Файлы из локальной папки `Releases/` **не** нужно коммитить в git — она в `.gitignore`.
> Обновление идёт только из ассетов GitHub-релиза. Коммит `.nupkg` в дерево репозитория
> ничего не даёт для обновления и лишь раздувает историю.

---

## Шаг 1. Подготовка изменений

1. Внести правки в код.
2. Поднять версию в `src/PdfGrouping.Desktop/PdfGrouping.Desktop.csproj`
   (`<Version>X.Y.Z</Version>`). Версия окна берётся отсюда же.
3. Создать `build/release-notes-X.Y.Z.md` с описанием изменений.
4. Добавить раздел `## [X.Y.Z] — ГГГГ-ММ-ДД` в `CHANGELOG.md`.
5. Сборка и тесты:
   ```powershell
   dotnet build PdfGrouping.sln -c Debug
   dotnet test
   ```
6. Закоммитить и запушить:
   ```powershell
   git add -A
   git commit -m "vX.Y.Z: краткое описание"
   git push origin HEAD
   ```

---

## Шаг 2. Сборка подписанного пакета

Отпечаток самоподписанного сертификата хранится в `C:\temp\pdfg-thumb.txt`
(thumbprint `C12C4A71A4B3C79C346996015D1238CC3D5DE640`, сертификат в `Cert:\CurrentUser\My`).

```powershell
# 1) publish self-contained win-x64
dotnet publish src\PdfGrouping.Desktop\PdfGrouping.Desktop.csproj `
  -c Release -r win-x64 --self-contained -o publish\win-x64

# 2) упаковка Velopack с подписью
dotnet vpk pack `
  --packId PdfGrouping --packVersion X.Y.Z `
  --packDir publish\win-x64 --mainExe PdfGrouping.exe `
  --packTitle "PDF Grouping" --packAuthors "Oleg Baskov" `
  --icon src\PdfGrouping.Desktop\Assets\app.ico `
  --releaseNotes build\release-notes-X.Y.Z.md `
  --signParams "/fd SHA256 /sha1 C12C4A71A4B3C79C346996015D1238CC3D5DE640"
```

Либо одной командой через скрипт:
```powershell
pwsh build\pack-win.ps1 -Version X.Y.Z -SignParams "/fd SHA256 /sha1 C12C4A71A4B3C79C346996015D1238CC3D5DE640"
```

После сборки в папке `Releases/` появятся:
- `RELEASES` — манифест (Velopack читает его первым);
- `PdfGrouping-X.Y.Z-full.nupkg` — полный пакет;
- `PdfGrouping-X.Y.Z-delta.nupkg` — дельта от предыдущей версии;
- `PdfGrouping-win-Setup.exe` — установщик для новых машин;
- `assets.win.json` — описание ассетов.

---

## Шаг 3. Публикация в GitHub Releases

### Способ A — автоматически (если сеть без TLS-перехвата)

```powershell
pwsh build\pack-win.ps1 -Version X.Y.Z -Upload -Token <GITHUB_PAT> `
  -SignParams "/fd SHA256 /sha1 C12C4A71A4B3C79C346996015D1238CC3D5DE640"
```
или напрямую `vpk upload github ...`.

> На рабочей машине за корпоративным прокси `vpk upload` падает с
> `SEC_E_UNTRUSTED_ROOT` / `PartialChain` (перехват TLS до `api.github.com`).
> В этом случае — Способ B. Флаг `--insecure` использовать **не** нужно.

### Способ B — вручную через веб-интерфейс

1. Открыть `https://github.com/OlBaskov-rec/inb-pdfGrouping/releases` → **Draft a new release**.
2. **Choose a tag** → ввести `vX.Y.Z` → **Create new tag on publish**.
3. **Title**: `PDF Grouping X.Y.Z`; в описание вставить текст из `build/release-notes-X.Y.Z.md`.
4. В **Attach binaries** перетащить **все файлы из папки `Releases/`**
   (минимум `RELEASES`, `*-full.nupkg`, `*-delta.nupkg`; плюс `Setup.exe`, `assets.win.json`).
5. **Publish release**.

> Если не приложить `RELEASES` или `*-full.nupkg`, Velopack не соберёт цепочку обновления.

---

## Шаг 4. Проверка обновления

1. На машине с **установленной** (через `Setup.exe`) предыдущей версией запустить приложение —
   фоновая проверка найдёт новый релиз.
   (В dev-запуске `dotnet run` обновления — no-op: `IsInstalled == false`.)
2. Либо вручную через «ℹ О программе» → проверка обновлений.

---

## Сертификат на новых машинах

Перед первым запуском установщика — доверить публичный сертификат:
```powershell
pwsh build\trust-cert.ps1
```
(сертификат `build/PdfGrouping-PublicCert.cer`). Иначе при запуске возможно предупреждение SmartScreen.
