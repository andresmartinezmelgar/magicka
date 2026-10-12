param([string]$ServerPath, [string]$CloudflaredPath='cloudflared', [int]$Port=27015, [string]$PrivateDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$privateDir = if ($PrivateDirectory) { [IO.Path]::GetFullPath($PrivateDirectory) } else { Join-Path $env:APPDATA 'Godot/app_userdata/Arcana Arena/firebase' }
$env:GOOGLE_APPLICATION_CREDENTIALS = Join-Path $privateDir 'service-account.json'
$env:ARCANA_FIREBASE_CONFIG = Join-Path $privateDir 'server.local.json'
$env:ARCANA_REWARD_OUTBOX = Join-Path $privateDir 'reward-outbox'
$env:ARCANA_LOG_DIRECTORY = Join-Path $root '.tools/firebase-tests/server-logs'
$env:GAME_PORT = $Port.ToString()
$env:SERVER_NAME = 'Arcana Arena Firebase'
$env:NO_UPDATE_NOTIFIER = '1'
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Puerto fuera de rango.' }
if (!$ServerPath) { $ServerPath = Join-Path $root 'artifacts/windows/server/Arcana.Server.exe' }
$ServerPath = [IO.Path]::GetFullPath($ServerPath)
if (!(Test-Path $ServerPath)) { throw 'No existe el servidor. Compila el paquete Windows o indica -ServerPath.' }
if (!(Get-Command node -ErrorAction SilentlyContinue)) { throw 'Instala Node.js 20 o posterior.' }
if (!(Test-Path (Join-Path $PSScriptRoot 'firebase-tests/node_modules/firebase-tools'))) { throw 'Ejecuta npm install --prefix scripts/firebase-tests antes de iniciar.' }
New-Item -ItemType Directory -Force (Join-Path $root '.tools/firebase-tests') | Out-Null
$tunnel = $null
$server = $null
try {
  if (!(Test-Path $env:GOOGLE_APPLICATION_CREDENTIALS)) { throw 'Falta service-account.json en la carpeta Firebase del juego.' }
  $log = Join-Path $root '.tools/firebase-tests/tunnel-active.log'
  $out = Join-Path $root '.tools/firebase-tests/tunnel-active.stdout.log'
  $tunnel = Start-Process -FilePath $CloudflaredPath -ArgumentList @('tunnel','--url',"http://127.0.0.1:$Port",'--no-autoupdate','--protocol','http2') -WindowStyle Hidden -RedirectStandardError $log -RedirectStandardOutput $out -PassThru
  $url = $null
  for ($i=0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 1
    if (Test-Path $log) {
      $match = [regex]::Match((Get-Content $log -Raw), 'https://[a-z0-9-]+\.trycloudflare\.com')
      if ($match.Success) { $url=$match.Value; break }
    }
    if ($tunnel.HasExited) { throw 'El tunel no pudo iniciarse.' }
  }
  if (!$url) { throw 'No se obtuvo una direccion HTTPS.' }
  Push-Location $root
  try {
    & node (Join-Path $PSScriptRoot 'firebase-tests/authorize-domain.cjs') $env:ARCANA_FIREBASE_CONFIG ([uri]$url).Host
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo autorizar el dominio en Firebase.' }
  } finally { Pop-Location }
  $cfg = Get-Content $env:ARCANA_FIREBASE_CONFIG -Raw | ConvertFrom-Json
  $cfg.publicUrl = $url
  $cfg | ConvertTo-Json | Set-Content $env:ARCANA_FIREBASE_CONFIG -Encoding utf8
  $server = Start-Process -FilePath $ServerPath -WorkingDirectory (Split-Path -Parent $ServerPath) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $root '.tools/firebase-tests/server-active.stdout.log') -RedirectStandardError (Join-Path $root '.tools/firebase-tests/server-active.stderr.log') -PassThru
  $health = $null
  for ($i=0; $i -lt 20; $i++) {
    Start-Sleep -Seconds 1
    try { $health=Invoke-RestMethod "http://127.0.0.1:$Port/health" -TimeoutSec 2; break } catch {}
    if ($server.HasExited) { throw 'El servidor no pudo iniciarse. Consulta .tools/firebase-tests/server-active.stderr.log.' }
  }
  if (!$health) { throw 'El servidor no responde por localhost.' }
  $account = Invoke-RestMethod "http://127.0.0.1:$Port/account/config" -TimeoutSec 10
  $publicHealth = $null
  for ($i=0; $i -lt 6; $i++) {
    try { $publicHealth=Invoke-RestMethod "$url/health" -TimeoutSec 10; break } catch { Start-Sleep -Seconds 2 }
  }
  if (!$publicHealth) { throw 'HTTPS no responde todavia. Revisa la conexion y vuelve a iniciar.' }
  $address = $url.Replace('https://','wss://')+'/play'
  if (!$account.enabled) { throw 'Firebase no esta habilitado en el servidor.' }
  [pscustomobject]@{address=$address;version=$health.version;firebase=$account;publicHealth=$publicHealth} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $root '.tools/firebase-tests/conexion-android.json')
  Write-Host "Servidor listo. Android: $address"
  Write-Host 'Usa un APK de la misma version que el servidor. Manten esta ventana abierta. Ctrl+C detiene el servidor y el tunel.'
  while (!$server.HasExited -and !$tunnel.HasExited) { Start-Sleep -Seconds 2 }
} finally {
  if ($server -and !$server.HasExited) { Stop-Process -Id $server.Id -ErrorAction SilentlyContinue }
  if ($tunnel -and !$tunnel.HasExited) { Stop-Process -Id $tunnel.Id -ErrorAction SilentlyContinue }
}

