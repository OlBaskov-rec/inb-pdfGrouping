<#
.SYNOPSIS
  Устанавливает публичный сертификат PDF Grouping в доверенные на ЭТОЙ машине,
  чтобы подпись приложения считалась действительной (убирает «Неизвестный издатель»).

.DESCRIPTION
  Импортирует build/PdfGrouping-PublicCert.cer в:
    - Trusted Root Certification Authorities (доверенные корневые)
    - Trusted Publishers (доверенные издатели)

  По умолчанию — для текущего пользователя (без прав администратора).
  С ключом -Machine — для всей машины (нужен запуск от администратора).

  ВНИМАНИЕ: это изменение политики доверия. Выполняйте только на машинах,
  где вы осознанно доверяете внутренним сборкам PDF Grouping.

.EXAMPLE
  pwsh build/trust-cert.ps1            # для текущего пользователя
  pwsh build/trust-cert.ps1 -Machine  # для всей машины (от администратора)
#>
param(
    [switch]$Machine
)

$ErrorActionPreference = "Stop"
$cer = Join-Path $PSScriptRoot "PdfGrouping-PublicCert.cer"
if (-not (Test-Path $cer)) { throw "Не найден публичный сертификат: $cer" }

$scope = if ($Machine) { "LocalMachine" } else { "CurrentUser" }
Write-Host "Устанавливаю доверие к сертификату ($scope)..." -ForegroundColor Cyan

Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\$scope\Root" | Out-Null
Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\$scope\TrustedPublisher" | Out-Null

Write-Host "Готово. Подпись PDF Grouping теперь доверенная на этой машине." -ForegroundColor Green
