<#
.SYNOPSIS
  Локальная сборка релиза PDF Grouping одной командой: (опц.) bump версии в csproj,
  build, tests, publish self-contained, vpk pack (подпись + иконка).

.DESCRIPTION
  Не публикует в GitHub Releases — это всегда делается вручную (см. docs/RELEASING.md, Этап C).
  После успешного выполнения в .\Releases лежат RELEASES, *-full.nupkg, *-delta.nupkg,
  Setup.exe, Portable.zip.

.PARAMETER Version
  Версия релиза (SemVer), напр. 0.1.22.

.PARAMETER BumpCsproj
  Если указан — записать <Version> в PdfGrouping.Desktop.csproj равным -Version.

.PARAMETER SkipTests
  Пропустить dotnet test.

.PARAMETER SignParams
  Параметры подписи для vpk (--signParams). По умолчанию — самоподписанный сертификат проекта.

.EXAMPLE
  pwsh build/release.ps1 -Version 0.1.22 -BumpCsproj
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$BumpCsproj,
    [switch]$SkipTests,
    [string]$SignParams = "/fd SHA256 /sha1 C12C4A71A4B3C79C346996015D1238CC3D5DE640"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$proj     = Join-Path $repoRoot "src\PdfGrouping.Desktop\PdfGrouping.Desktop.csproj"
$sln      = Join-Path $repoRoot "PdfGrouping.sln"
$publish  = Join-Path $repoRoot "publish\win-x64"
$notes    = Join-Path $repoRoot "build\release-notes-$Version.md"

function Step($t) { Write-Host "==> $t" -ForegroundColor Cyan }

if ($BumpCsproj) {
    Step "Версия в csproj -> $Version"
    $xml = Get-Content $proj -Raw
    $xml = [regex]::Replace($xml, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
    Set-Content $proj $xml -Encoding utf8
}

if (-not (Test-Path $notes)) {
    Write-Warning "Нет файла заметок: $notes (создайте build/release-notes-$Version.md)."
}

Step "build (Release)"
dotnet build $sln -c Release
if ($LASTEXITCODE -ne 0) { throw "Сборка не удалась." }

if (-not $SkipTests) {
    Step "tests"
    dotnet test $sln -c Release
    if ($LASTEXITCODE -ne 0) { throw "Тесты не прошли." }
}

Step "publish (self-contained win-x64)"
dotnet publish $proj -c Release -r win-x64 --self-contained -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish не удался." }

Step "vpk pack (подпись + иконка)"
$packArgs = @("-Version", $Version)
if (Test-Path $notes) { $packArgs += @("-ReleaseNotes", $notes) }
if ($SignParams)      { $packArgs += @("-SignParams", $SignParams) }
& (Join-Path $PSScriptRoot "pack-win.ps1") @packArgs
if ($LASTEXITCODE -ne 0) { throw "Упаковка не удалась." }

Write-Host ""
Write-Host "Готово. Артефакты в .\Releases. Дальше — публикация в GitHub Releases вручную (docs/RELEASING.md, Этап C)." -ForegroundColor Green
