param(
    [string]$Serial = '',
    [string]$AndroidSdkPath = '',
    [ValidateSet('Classic', 'Citadel', 'Ascension', 'Relics')][string]$Map = 'Classic',
    [ValidateRange(1,120)][int]$Seconds = 30,
    [ValidateRange(0,30)][int]$Warmup = 5
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (!$AndroidSdkPath) { $AndroidSdkPath = Join-Path $repo '.tools/android-sdk' }
$adb = Join-Path $AndroidSdkPath 'platform-tools/adb.exe'
if (!(Test-Path -LiteralPath $adb)) { throw 'Falta ADB. Compila primero la APK o indica -AndroidSdkPath.' }
$devices = @(& $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '^\S+\s+device\s*$' } | ForEach-Object { ($_ -split '\s+')[0] })
if (!$Serial) {
    if ($devices.Count -ne 1) { throw 'Conecta un dispositivo con depuracion USB autorizada o indica -Serial para seleccionar uno.' }
    $Serial = $devices[0]
}
if ($devices -notcontains $Serial) { throw 'El dispositivo no esta conectado o no ha autorizado la depuracion USB.' }
$emulator = ((& $adb -s $Serial shell getprop ro.kernel.qemu) -join '').Trim() -eq '1'
$environment = if ($emulator) { 'adb-android-emulator' } else { 'adb-android-physical' }
& $adb -s $Serial shell am force-stop com.arcana.arena
if ($LASTEXITCODE -ne 0) { throw 'No se pudo detener la app anterior.' }
& $adb -s $Serial logcat -c
$parameters = "--,--profile-render,--profile-map=$Map,--profile-warmup=$Warmup,--profile-seconds=$Seconds,--profile-environment=$environment"
& $adb -s $Serial shell am start -n com.arcana.arena/com.godot.game.GodotApp --esa command_line_params $parameters
if ($LASTEXITCODE -ne 0) { throw 'No se pudo arrancar la medicion.' }
$deadline = (Get-Date).AddSeconds($Warmup + $Seconds + 180)
$completed = $false
while ((Get-Date) -lt $deadline) {
    $log = (& $adb -s $Serial logcat -d) -join "`n"
    if ($log -match 'PROFILE_FAILED|FATAL EXCEPTION|Unhandled exception') { throw 'La app registro un fallo durante la medicion.' }
    if ($log -match 'PROFILE_OK:') { $completed = $true; break }
    Start-Sleep -Seconds 1
}
if (!$completed) { throw 'No se obtuvo el resultado de la medicion a tiempo.' }
$json = (& $adb -s $Serial shell run-as com.arcana.arena cat files/profiles/latest.json) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'No se pudo leer el informe. La APK debe ser la compilacion de desarrollo del proyecto.' }
$report = $json | ConvertFrom-Json
if ($report.canceled -or $report.samples -lt 3 -or $report.averageFps -le 0) { throw 'La medicion esta incompleta.' }
$folder = Join-Path $repo 'artifacts/render-profile'
[IO.Directory]::CreateDirectory($folder) | Out-Null
$path = Join-Path $folder ("Android-{0}-{1}.json" -f $Map, (Get-Date -Format 'yyyyMMdd-HHmmss'))
[IO.File]::WriteAllText($path, $json, (New-Object Text.UTF8Encoding($false)))
Write-Host ("RENDER_PROFILE_OK: {0} muestras, {1:N2} FPS, {2}." -f $report.samples, $report.averageFps, $environment)
Write-Host "Informe: $path"
