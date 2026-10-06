param([string]$GodotPath = '')
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or ![Environment]::Is64BitOperatingSystem) {
    throw 'La compilacion con doble clic requiere Windows x64.'
}
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$cache = Join-Path $repo '.tools'
$artifactRoot = Join-Path $repo 'artifacts'
New-Item -ItemType Directory -Path $cache, $artifactRoot -Force | Out-Null
$log = Join-Path $artifactRoot ("compilacion-{0}-{1}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss'), [Guid]::NewGuid().ToString('N').Substring(0,8))
$sdkVersion = '8.0.425'
$godotVersion = '4.5.2'
$templateVersion = "$godotVersion.stable.mono"
. (Join-Path $PSScriptRoot 'build-support.ps1')
. (Join-Path $PSScriptRoot 'build-lock.ps1')
$buildLease = $null

function Download-Tool {
    param([string]$Url, [string]$Target)
    if (Test-Path -LiteralPath $Target) { return }
    $partial = "$Target.partial"
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $partial
            Move-Item -LiteralPath $partial -Destination $Target -Force
            return
        } catch {
            if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
            if ($attempt -eq 3) { throw }
            Start-Sleep -Seconds $attempt
        }
    }
}

Start-Transcript -Path $log | Out-Null
try {
    $buildLease = Enter-ArcanaBuildLock -RepositoryRoot $repo
    Write-Host 'Compilando Arcana Arena. La primera vez se descargan las herramientas oficiales.'
    Write-Host 'La carpeta Juego se sustituye despues de compilar correctamente. Cierra el juego antes de compilar.'
    $sdkRoot = Join-Path $cache 'dotnet'
    $localDotnet = Join-Path $sdkRoot 'dotnet.exe'
    if (!(Test-Path -LiteralPath (Join-Path $sdkRoot "sdk/$sdkVersion")) -or !(Test-Path -LiteralPath $localDotnet)) {
        $installer = Join-Path $cache 'dotnet-install.ps1'
        Download-Tool 'https://dot.net/v1/dotnet-install.ps1' $installer
        & $installer -Version $sdkVersion -Architecture x64 -InstallDir $sdkRoot -NoPath
    }
    if (!(Test-Path -LiteralPath $localDotnet)) { throw 'No se pudo instalar el SDK .NET 8.' }
    $env:DOTNET_ROOT = $sdkRoot
    $env:DOTNET_ROOT_X64 = $sdkRoot
    $env:PATH = "$sdkRoot;$env:PATH"
    $release = "https://github.com/godotengine/godot/releases/download/$godotVersion-stable"
    if (!$GodotPath) {
        $editorRoot = Join-Path $cache "godot-$godotVersion"
        $editor = Get-ChildItem -LiteralPath $editorRoot -Recurse -Filter '*win64_console.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
        if (!$editor) {
            $zip = Join-Path $cache "Godot-$godotVersion-mono-win64.zip"
            Download-Tool "$release/Godot_v$godotVersion-stable_mono_win64.zip" $zip
            Expand-Archive -LiteralPath $zip -DestinationPath $editorRoot -Force
            $editor = Get-ChildItem -LiteralPath $editorRoot -Recurse -Filter '*win64_console.exe' | Select-Object -First 1
        }
        if (!$editor) { throw 'No se encuentra Godot .NET despues de extraerlo.' }
        $GodotPath = $editor.FullName
    }
    $engineVersion = & $GodotPath --version
    if ($LASTEXITCODE -ne 0 -or $engineVersion -notmatch '^4\.5\.2\.stable\.mono') { throw 'Se necesita Godot 4.5.2 .NET.' }
    $templates = Join-Path $env:APPDATA "Godot/export_templates/$templateVersion"
    if (!(Test-Path -LiteralPath (Join-Path $templates 'windows_release_x86_64.exe'))) {
        $archiveFile = Join-Path $cache "Godot-$godotVersion-mono-templates.zip"
        Write-Host 'Descargando las plantillas de exportacion. Solo se extraen las de Windows x64.'
        Download-Tool "$release/Godot_v$godotVersion-stable_mono_export_templates.tpz" $archiveFile
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($archiveFile)
        try {
            New-Item -ItemType Directory -Path $templates -Force | Out-Null
            foreach ($entry in $archive.Entries | Where-Object { $_.Name -match '^windows_.*x86_64' -or $_.Name -eq 'version.txt' }) {
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $templates $entry.Name), $true)
            }
        } finally { $archive.Dispose() }
        Remove-Item -LiteralPath $archiveFile -Force
    }
    & (Join-Path $PSScriptRoot 'package-windows.ps1') -GodotPath $GodotPath -BuildLease $buildLease
    Publish-GamePackage -RepositoryRoot $repo -Package (Join-Path $artifactRoot 'windows')
    Write-Host "Registro: $log"
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Registro: $log"
    exit 1
} finally { Exit-ArcanaBuildLock -Lease $buildLease; Stop-Transcript | Out-Null }
