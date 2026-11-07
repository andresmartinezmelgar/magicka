#requires -Version 5.1
[CmdletBinding()]
param([string]$Repository = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent), [string]$Branch = 'pruebas/dashboard-estadisticas', [ValidateRange(1024,65535)][int]$Port = 8787)
$ErrorActionPreference = 'Stop'
$Repository = [IO.Path]::GetFullPath($Repository)
if ($Repository.Contains('"') -or $Branch.Contains('"')) { throw 'Ruta o rama no valida.' }
Get-Command git.exe -ErrorAction Stop | Out-Null
$scriptFolder = Join-Path $Repository 'dashboard\windows'
$update = Join-Path $scriptFolder 'Update-Dashboard.ps1'
$start = Join-Path $scriptFolder 'Start-Dashboard.ps1'
if (-not (Test-Path -LiteralPath $update) -or -not (Test-Path -LiteralPath $start)) { throw 'Los scripts del dashboard no estan en el repositorio indicado.' }
# Validate the checkout and make the first update before registering any recurring action.
& powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $update -Repository $Repository -Branch $Branch
if ($LASTEXITCODE -ne 0) { throw 'No se ha podido validar/actualizar el repositorio. No se han creado tareas.' }
$account = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$principal = New-ScheduledTaskPrincipal -UserId $account -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 10)
$exe = Join-Path $PSHOME 'powershell.exe'
$updateArgs = '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $update + '" -Repository "' + $Repository + '" -Branch "' + $Branch + '"'
$action = New-ScheduledTaskAction -Execute $exe -Argument $updateArgs -WorkingDirectory $Repository
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(30) -RepetitionInterval (New-TimeSpan -Minutes 30)
Register-ScheduledTask -TaskName 'ArcanaArena-Dashboard-Actualizar' -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description 'Actualiza por avance rapido cada 30 minutos sin descartar cambios locales.' -Force | Out-Null
$startArgs = '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $start + '" -Repository "' + $Repository + '" -Port ' + $Port + ' -OpenBrowser'
$startAction = New-ScheduledTaskAction -Execute $exe -Argument $startArgs -WorkingDirectory $Repository
$startupSettings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero)
$logon = New-ScheduledTaskTrigger -AtLogOn -User $account
Register-ScheduledTask -TaskName 'ArcanaArena-Dashboard-Iniciar' -Action $startAction -Trigger $logon -Settings $startupSettings -Principal $principal -Description 'Abre el dashboard local al iniciar sesion en Windows.' -Force | Out-Null
if (-not (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)) { Start-ScheduledTask -TaskName 'ArcanaArena-Dashboard-Iniciar' } else { Start-Process ('http://localhost:' + $Port + '/') }
Write-Host 'Instalado: actualizacion cada 30 minutos y arranque al iniciar sesion.'
Write-Host ('Dashboard: http://localhost:' + $Port + '/')
Write-Host ('Registro: ' + (Join-Path $env:LOCALAPPDATA 'ArcanaArenaDashboard\updates.log'))
