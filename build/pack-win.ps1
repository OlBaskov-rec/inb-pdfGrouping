<#
.SYNOPSIS
  Сборка portable Windows-релиза PDF Grouping через Velopack.

.DESCRIPTION
  1. publish self-contained win-x64
  2. vpk pack  -> создаёт Setup.exe, portable .zip и пакеты в .\Releases
  3. (опционально) vpk upload github -> публикует релиз в GitHub Releases

.PARAMETER Version
  Версия релиза (SemVer), напр. 0.1.0. По умолчанию берётся из csproj.

.PARAMETER Upload
  Если указан — загрузить релиз в GitHub Releases (нужен -Token или $env:GITHUB_TOKEN).

.PARAMETER Token
  GitHub PAT с правом на запись релизов. По умолчанию — $env:GITHUB_TOKEN.

.EXAMPLE
  pwsh build/pack-win.ps1 -Version 0.1.0
  pwsh build/pack-win.ps1 -Version 0.1.0 -Upload
#>
param(
    [string]$Version = "",
    [switch]$Upload,
    [string]$Token = $env:GITHUB_TOKEN
)

$ErrorActionPreference = "Stop"
$repoRoot  = Split-Path -Parent $PSScriptRoot
$proj      = Join-Path $repoRoot "src\PdfGrouping.Desktop\PdfGrouping.Desktop.csproj"
$publishDir= Join-Path $repoRoot "publish"
$releaseDir= Join-Path $repoRoot "Releases"
$repoUrl   = "https://github.com/OlBaskov-rec/inb-pdfGrouping"

$packId    = "PdfGrouping"
$mainExe   = "PdfGrouping.exe"
$title     = "PDF Grouping"
$rid       = "win-x64"

# Версия из csproj, если не задана явно
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$csproj = Get-Content $proj
    $Version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($Version)) { throw "Не удалось определить версию. Укажите -Version." }
}
Write-Host "==> Версия релиза: $Version" -ForegroundColor Cyan

# Инструмент vpk (из манифеста .config/dotnet-tools.json)
Push-Location $repoRoot
try {
    dotnet tool restore

    Write-Host "==> publish self-contained $rid" -ForegroundColor Cyan
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    dotnet publish $proj -c Release -r $rid --self-contained true -o $publishDir

    Write-Host "==> vpk pack" -ForegroundColor Cyan
    dotnet vpk pack `
        --packId $packId `
        --packVersion $Version `
        --packDir $publishDir `
        --mainExe $mainExe `
        --packTitle $title `
        --runtime $rid `
        --outputDir $releaseDir

    if ($Upload) {
        if ([string]::IsNullOrWhiteSpace($Token)) {
            throw "Для -Upload нужен GitHub-токен: параметр -Token или переменная окружения GITHUB_TOKEN."
        }
        Write-Host "==> vpk upload github (publish)" -ForegroundColor Cyan
        dotnet vpk upload github `
            --outputDir $releaseDir `
            --repoUrl $repoUrl `
            --token $Token `
            --publish true `
            --releaseName "v$Version" `
            --tag "v$Version" `
            --merge true
    }

    Write-Host "==> Готово. Артефакты в: $releaseDir" -ForegroundColor Green
    Get-ChildItem $releaseDir | Select-Object Name, Length
}
finally {
    Pop-Location
}
