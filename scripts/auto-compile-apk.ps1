param(
    [string]$GodotPath = '',
    [string]$JavaHome = '',
    [string]$AndroidSdkPath = '',
    [ValidateSet('arm64', 'x86_64')][string]$Architecture = 'arm64',
    [switch]$AcceptAndroidLicenses
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or ![Environment]::Is64BitOperatingSystem) {
    throw 'La compilacion con doble clic requiere Windows x64.'
}
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$cache = Join-Path $repo '.tools'
$artifactRoot = Join-Path $repo 'artifacts'
$outputRoot = Join-Path $artifactRoot 'android'
New-Item -ItemType Directory -Path $cache, $outputRoot -Force | Out-Null
$log = Join-Path $artifactRoot ("compilacion-apk-{0}-{1}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss'), [Guid]::NewGuid().ToString('N').Substring(0,8))
$temp = Join-Path $cache ("apk-build-{0}" -f [Guid]::NewGuid().ToString('N'))
$client = Join-Path $repo 'src/Arcana.Client'
$presetPath = Join-Path $client 'export_presets.cfg'
$presetBackup = $null
$buildLease = $null
$environmentBackup = @{}
foreach ($name in @('PATH', 'APPDATA', 'LOCALAPPDATA', 'JAVA_HOME', 'ANDROID_HOME', 'ANDROID_SDK_ROOT', 'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'UseSharedCompilation', 'MSBUILDDISABLENODEREUSE', 'DOTNET_CLI_USE_MSBUILD_SERVER', 'GRADLE_OPTS', 'GODOT_ANDROID_KEYSTORE_DEBUG_PATH', 'GODOT_ANDROID_KEYSTORE_DEBUG_USER', 'GODOT_ANDROID_KEYSTORE_DEBUG_PASSWORD')) {
    $environmentBackup[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$sdkVersion = '8.0.425'
$godotVersion = '4.5.2'
$templateVersion = "$godotVersion.stable.mono"
$utf8 = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.IO.Compression.FileSystem
. (Join-Path $PSScriptRoot 'android-tools-support.ps1')
. (Join-Path $PSScriptRoot 'tool-support.ps1')
. (Join-Path $PSScriptRoot 'build-lock.ps1')
. (Join-Path $PSScriptRoot 'version-support.ps1')

function Download-Tool {
    param([string]$Url, [string]$Target)
    if (Test-Path -LiteralPath $Target) { return }
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Write-Host "Descargando $([IO.Path]::GetFileName($Target))..."
            Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile "$Target.partial"
            Move-Item -LiteralPath "$Target.partial" -Destination $Target -Force
            return
        } catch {
            Remove-Item -LiteralPath "$Target.partial" -Force -ErrorAction SilentlyContinue
            if ($attempt -eq 3) { throw }
            Start-Sleep -Seconds $attempt
        }
    }
}

function Test-Jdk17 {
    param([string]$Path)
    if (!$Path -or !(Test-Path -LiteralPath (Join-Path $Path 'release'))) { return $false }
    return (Test-Path -LiteralPath (Join-Path $Path 'bin/keytool.exe')) -and
        (Test-Path -LiteralPath (Join-Path $Path 'bin/java.exe')) -and
        ((Get-Content -LiteralPath (Join-Path $Path 'release') -Raw) -match '(?m)^JAVA_VERSION="17\.')
}

Start-Transcript -Path $log | Out-Null
Push-Location -LiteralPath $repo
try {
    $buildLease = Enter-ArcanaBuildLock -RepositoryRoot $repo
    $presetBackup = [IO.File]::ReadAllBytes($presetPath)
    Write-Host 'Compilando la APK de Arcana Arena. La primera vez necesita Internet y varios GB libres.'
    $buildVersion = Get-ArcanaBuildVersion -RepositoryRoot $repo
    $version = $buildVersion.Name
    $presets = Update-ArcanaExportVersion -Presets ([IO.File]::ReadAllText($presetPath)) -Version $buildVersion
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    $dotnetRoot = Join-Path $cache 'dotnet'
    $dotnet = Join-Path $dotnetRoot 'dotnet.exe'
    if (!(Test-Path -LiteralPath (Join-Path $dotnetRoot "sdk/$sdkVersion")) -or !(Test-Path -LiteralPath $dotnet)) {
        $installer = Join-Path $cache 'dotnet-install.ps1'
        Download-Tool 'https://dot.net/v1/dotnet-install.ps1' $installer
        & $installer -Version $sdkVersion -Architecture x64 -InstallDir $dotnetRoot -NoPath
    }
    if (!(Test-Path -LiteralPath $dotnet)) { throw 'No se pudo instalar el SDK .NET 8.' }
    $env:DOTNET_ROOT = $dotnetRoot
    $env:DOTNET_ROOT_X64 = $dotnetRoot
    # Evita que el servidor persistente de Roslyn mantenga abierto Godot en Windows.
    $env:UseSharedCompilation = 'false'
    $env:MSBUILDDISABLENODEREUSE = '1'
    $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
    # El wrapper de consola espera a sus hijos: Gradle tambien debe cerrar su daemon.
    $env:GRADLE_OPTS = "$env:GRADLE_OPTS -Dorg.gradle.daemon=false"
    $env:PATH = "$dotnetRoot;$env:PATH"

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
    $GodotPath = (Resolve-Path -LiteralPath $GodotPath).Path
    $engineVersion = (Invoke-Tool $GodotPath @('--version')) -join "`n"
    if ($engineVersion -notmatch '^4\.5\.2\.stable\.mono') { throw 'Se necesita Godot 4.5.2 .NET.' }

    if ($JavaHome -and !(Test-Jdk17 $JavaHome)) { throw 'JavaHome debe indicar un JDK 17 con java.exe y keytool.exe.' }
    if (!$JavaHome -and (Test-Jdk17 $env:JAVA_HOME)) { $JavaHome = $env:JAVA_HOME }
    if (!$JavaHome) {
        $jdkRoot = Join-Path $cache 'jdk-17'
        $jdk = Get-ChildItem -LiteralPath $jdkRoot -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Jdk17 $_.FullName } | Select-Object -First 1
        if (!$jdk) {
            $zip = Join-Path $cache 'temurin-17-windows-x64.zip'
            Download-Tool 'https://api.adoptium.net/v3/binary/latest/17/ga/windows/x64/jdk/hotspot/normal/eclipse' $zip
            Expand-Archive -LiteralPath $zip -DestinationPath $jdkRoot -Force
            $jdk = Get-ChildItem -LiteralPath $jdkRoot -Directory | Where-Object { Test-Jdk17 $_.FullName } | Select-Object -First 1
        }
        if (!$jdk) { throw 'No se pudo preparar OpenJDK 17.' }
        $JavaHome = $jdk.FullName
    }
    $JavaHome = (Resolve-Path -LiteralPath $JavaHome).Path
    $env:JAVA_HOME = $JavaHome
    $env:PATH = "$(Join-Path $JavaHome 'bin');$env:PATH"
    if (!$AndroidSdkPath) { $AndroidSdkPath = Join-Path $cache 'android-sdk' }
    New-Item -ItemType Directory -Path $AndroidSdkPath -Force | Out-Null
    $AndroidSdkPath = (Resolve-Path -LiteralPath $AndroidSdkPath).Path
    $sdkManager = Join-Path $AndroidSdkPath 'cmdline-tools/latest/bin/sdkmanager.bat'
    if (!(Test-AndroidCommandLineTools (Split-Path -Parent (Split-Path -Parent $sdkManager)))) {
        $zip = Join-Path $cache 'android-cli-win-13114758.zip'
        if ((Test-Path -LiteralPath $zip) -and !(Test-AndroidToolsArchive $zip)) {
            Write-Host 'El ZIP Android guardado esta incompleto. Se descargara otra vez.'
            Remove-Item -LiteralPath $zip -Force
        }
        Download-Tool 'https://dl.google.com/android/repository/commandlinetools-win-13114758_latest.zip' $zip
        Install-AndroidCommandLineTools -ArchivePath $zip -SdkPath $AndroidSdkPath
    }
    $env:ANDROID_HOME = $AndroidSdkPath
    $env:ANDROID_SDK_ROOT = $AndroidSdkPath
    Write-Host 'Comprobando las licencias del SDK Android. Responde y si aceptas los terminos mostrados.'
    Confirm-AndroidSdkLicenses -SdkManager $sdkManager -SdkPath $AndroidSdkPath -JavaHome $JavaHome -Accept:$AcceptAndroidLicenses
    $sdkCommand = Get-AndroidSdkManagerCommand -SdkManager $sdkManager -JavaHome $JavaHome
    Invoke-Tool $sdkCommand.FilePath ($sdkCommand.Arguments + @("--sdk_root=$AndroidSdkPath", 'platform-tools', 'build-tools;35.0.0', 'platforms;android-35')) | Out-Null
    $buildTools = Join-Path $AndroidSdkPath 'build-tools/35.0.0'
    if (!(Test-Path -LiteralPath (Join-Path $buildTools 'apksigner.bat')) -or !(Test-Path -LiteralPath (Join-Path $AndroidSdkPath 'platforms/android-35/android.jar'))) {
        throw 'Faltan herramientas Android 35. Comprueba que aceptaste las licencias.'
    }

    # Configuracion aislada: no modifica las preferencias del editor del usuario.
    $templateNames = @('android_debug.apk', 'android_release.apk', 'android_source.zip', 'version.txt')
    $templateCache = Join-Path $cache "android-templates-$templateVersion"
    New-Item -ItemType Directory -Path $templateCache -Force | Out-Null
    $userTemplates = Join-Path $environmentBackup['APPDATA'] "Godot/export_templates/$templateVersion"
    foreach ($name in $templateNames) {
        if (!(Test-Path -LiteralPath (Join-Path $templateCache $name)) -and (Test-Path -LiteralPath (Join-Path $userTemplates $name))) {
            Copy-Item -LiteralPath (Join-Path $userTemplates $name) -Destination $templateCache
        }
    }
    if (@($templateNames | Where-Object { !(Test-Path -LiteralPath (Join-Path $templateCache $_)) }).Count -gt 0) {
        $zip = Join-Path $cache "Godot-$godotVersion-mono-templates.zip"
        Download-Tool "$release/Godot_v$godotVersion-stable_mono_export_templates.tpz" $zip
        $archive = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            foreach ($name in $templateNames) {
                $entry = $archive.GetEntry("templates/$name")
                if (!$entry) { throw "Falta la plantilla $name." }
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $templateCache $name), $true)
            }
        } finally { $archive.Dispose() }
        Remove-Item -LiteralPath $zip -Force
    }
    $env:APPDATA = Join-Path $temp 'config'
    $env:LOCALAPPDATA = $env:APPDATA
    $godotConfig = Join-Path $env:APPDATA 'Godot'
    $templates = Join-Path $godotConfig "export_templates/$templateVersion"
    New-Item -ItemType Directory -Path $templates -Force | Out-Null
    foreach ($name in $templateNames) { Copy-Item -LiteralPath (Join-Path $templateCache $name) -Destination $templates }
    $javaSetting = $JavaHome.Replace('\', '/') | ConvertTo-Json -Compress
    $sdkSetting = $AndroidSdkPath.Replace('\', '/') | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $godotConfig 'editor_settings-4.5.tres'), "[gd_resource type=`"EditorSettings`" format=3]`n`n[resource]`nexport/android/java_sdk_path=$javaSetting`nexport/android/android_sdk_path=$sdkSetting`n", $utf8)
    $key = Join-Path $cache 'android-debug.keystore'
    $keytool = Join-Path $JavaHome 'bin/keytool.exe'
    if (!(Test-Path -LiteralPath $key)) {
        Invoke-Tool $keytool @('-genkeypair', '-noprompt', '-keystore', $key, '-storepass', 'android', '-keypass', 'android', '-alias', 'androiddebugkey', '-keyalg', 'RSA', '-keysize', '2048', '-validity', '10000', '-dname', 'CN=Arcana Arena Development') | Out-Null
    }
    Invoke-Tool $keytool @('-list', '-keystore', $key, '-storepass', 'android', '-alias', 'androiddebugkey') | Out-Null
    $env:GODOT_ANDROID_KEYSTORE_DEBUG_PATH = $key
    $env:GODOT_ANDROID_KEYSTORE_DEBUG_USER = 'androiddebugkey'
    $env:GODOT_ANDROID_KEYSTORE_DEBUG_PASSWORD = 'android'
    $abi = if ($Architecture -eq 'arm64') { 'arm64-v8a' } else { 'x86_64' }
    foreach ($candidate in @('armeabi-v7a', 'arm64-v8a', 'x86', 'x86_64')) {
        $enabled = ($candidate -eq $abi).ToString().ToLowerInvariant()
        $presets = [regex]::Replace($presets, ('(?m)^architectures/' + [regex]::Escape($candidate) + '=(true|false)'), "architectures/$candidate=$enabled")
    }
    [IO.File]::WriteAllText($presetPath, $presets, $utf8)
    Invoke-Tool $dotnet @('build', (Join-Path $client 'Arcana.Client.csproj'), '-c', 'Debug', '--nologo') | Out-Null
    Invoke-Tool $GodotPath @('--headless', '--editor', '--path', $client, '--import') -CheckGodotErrors | Out-Null
    $stagedApk = Join-Path $temp 'ArcanaArena.apk'
    Invoke-Tool $GodotPath @('--headless', '--path', $client, '--install-android-build-template', '--export-debug', 'Android', $stagedApk) -CheckGodotErrors | Out-Null
    if (!(Test-Path -LiteralPath $stagedApk)) { throw 'Godot no genero la APK.' }
    Invoke-Tool (Join-Path $buildTools 'apksigner.bat') @('verify', '--verbose', $stagedApk) | Out-Null
    $manifest = (Invoke-Tool (Join-Path $buildTools 'aapt.exe') @('dump', 'badging', $stagedApk)) -join "`n"
    Assert-ArcanaAndroidVersion -Manifest $manifest -Version $buildVersion
    foreach ($expected in @("package: name='com.arcana.arena'", "sdkVersion:'26'", "targetSdkVersion:'35'", 'android.permission.INTERNET')) {
        if (!$manifest.Contains($expected)) { throw "Manifest Android incorrecto: falta $expected." }
    }
    $archive = [IO.Compression.ZipFile]::OpenRead($stagedApk)
    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName })
        if ($names -notcontains "lib/$abi/libgodot_android.so" -or !($names | Where-Object { $_ -like "lib/$abi/*mono*.so" })) { throw 'Faltan Godot o el runtime C# en la APK.' }
        if ($names | Where-Object { $_ -match '^lib/[^/]+/.*\.so$' -and $_ -notlike "lib/$abi/*" }) { throw 'La APK contiene una arquitectura inesperada.' }
    } finally { $archive.Dispose() }
    $output = Join-Path $outputRoot "ArcanaArena-v$version-Android-$Architecture.apk"
    # SHA256 directo con .NET: no depende de modulos autoload de PowerShell.
    $hashAlgorithm = [Security.Cryptography.SHA256]::Create()
    $apkStream = [IO.File]::OpenRead($stagedApk)
    try { $hash = [BitConverter]::ToString($hashAlgorithm.ComputeHash($apkStream)).Replace('-', '').ToLowerInvariant() }
    finally { $apkStream.Dispose(); $hashAlgorithm.Dispose() }
    # Solo publica la salida una vez verificados firma, manifest y runtime.
    # PowerShell 5.1 convierte $null en una ruta vacia al enlazar este overload.
    if (Test-Path -LiteralPath $output) { [IO.File]::Replace($stagedApk, $output, (Join-Path $temp 'previous.apk')) }
    else { [IO.File]::Move($stagedApk, $output) }
    $hashFile = if ($Architecture -eq 'arm64') { 'SHA256SUMS-Android.txt' } else { 'SHA256SUMS-Android-x86_64.txt' }
    [IO.File]::WriteAllText((Join-Path $outputRoot $hashFile), "$hash  $([IO.Path]::GetFileName($output))`n", $utf8)
    [IO.File]::WriteAllText((Join-Path $outputRoot "manifest-$Architecture.txt"), $manifest, $utf8)
    Write-Host 'ANDROID_PACKAGE_OK: firma, version, Android 8+, red, Godot y C#.'
    Write-Host "APK: $output" -ForegroundColor Green
    Write-Host "Registro: $log"
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Registro: $log"
    exit 1
} finally {
    try {
        if ($null -ne $presetBackup) { [IO.File]::WriteAllBytes($presetPath, $presetBackup) }
        foreach ($name in $environmentBackup.Keys) { [Environment]::SetEnvironmentVariable($name, $environmentBackup[$name], 'Process') }
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
        Pop-Location
        Stop-Transcript | Out-Null
    } finally { Exit-ArcanaBuildLock -Lease $buildLease }
}
