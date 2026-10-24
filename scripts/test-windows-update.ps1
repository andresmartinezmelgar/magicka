$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Join-Path ([IO.Path]::GetTempPath()) ("Arcana update O'Connor " + [Guid]::NewGuid().ToString('N'))
$updater = Join-Path $PSScriptRoot 'Apply-WindowsUpdate.ps1'
$shell = Join-Path $PSHOME $(if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell.exe' })
if ($env:OS -eq 'Windows_NT' -and $PSVersionTable.PSEdition -eq 'Core') { $shell += '.exe' }
$passed = 0
function Assert([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Text([string]$Path, [string]$Content) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Content)
}
function Fixture {
    $case = Join-Path $root ([Guid]::NewGuid().ToString('N'))
    $install = Join-Path $case 'Juego [instalado]'
    $package = Join-Path $case 'nuevo'
    Text (Join-Path $install 'ArcanaArena.exe') 'old-exe'
    Text (Join-Path $install 'ArcanaArena.pck') 'old-pck'
    Text (Join-Path $install 'VERSION.txt') '0.17.16'
    Text (Join-Path $install 'account-server.json') 'local-private-settings'
    Text (Join-Path $install 'personal.txt') 'keep-me'
    foreach ($relative in @('ArcanaArena.exe','ArcanaArena.pck','server/Arcana.Server.exe',
        'runtime/Arcana.Client.dll','runtime/coreclr.dll')) { Text (Join-Path $package $relative) 'new-content' }
    Text (Join-Path $package 'account-server.json') 'distributed-defaults'
    Text (Join-Path $package 'VERSION.txt') '99.0.1'
    return [pscustomobject]@{ install=$install; package=$package; zip=(Join-Path $case 'release.zip');
        work=(Join-Path $case 'updates/install-case') }
}
function Zip($Fixture) {
    & (Join-Path $PSScriptRoot 'write-update-manifest.ps1') -Package $Fixture.package -Version '99.0.1'
    [IO.Compression.ZipFile]::CreateFromDirectory($Fixture.package, $Fixture.zip)
}
function Run($Fixture, [string]$Hash = '', [switch]$Preview) {
    if (!$Hash) { $Hash = (Get-FileHash -LiteralPath $Fixture.zip -Algorithm SHA256).Hash }
    $options = @()
    if ($Preview) { $options += '-AllowSameVersion' }
    & $shell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $updater -ArchivePath $Fixture.zip `
        -InstallDirectory $Fixture.install -ExpectedVersion '99.0.1' -ExpectedSha256 $Hash -WorkDirectory $Fixture.work -NoRestart @options
    return $LASTEXITCODE
}
function Untouched($Fixture) {
    Assert ((Get-Content -LiteralPath (Join-Path $Fixture.install 'ArcanaArena.exe') -Raw) -eq 'old-exe') 'Se altero el EXE anterior.'
    Assert ((Get-Content -LiteralPath (Join-Path $Fixture.install 'VERSION.txt') -Raw) -eq '0.17.16') 'Se altero la version anterior.'
    Assert (!(Test-Path -LiteralPath (Join-Path $Fixture.install 'runtime/Arcana.Client.dll'))) 'Se instalo parcialmente el runtime.'
}
try {
    $fixture = Fixture; Zip $fixture
    Assert ((Run $fixture) -eq 0) 'Fallo una instalacion completa y valida.'
    Assert ((Get-Content -LiteralPath (Join-Path $fixture.install 'VERSION.txt') -Raw) -eq '99.0.1') 'No se instalo la version nueva.'
    Assert ((Get-Content -LiteralPath (Join-Path $fixture.install 'ArcanaArena.pck') -Raw) -eq 'new-content') 'No se sustituyo el juego.'
    Assert ((Get-Content -LiteralPath (Join-Path $fixture.install 'account-server.json') -Raw) -eq 'local-private-settings') 'Se perdio la configuracion local.'
    Assert ((Get-Content -LiteralPath (Join-Path $fixture.install 'personal.txt') -Raw) -eq 'keep-me') 'Se borro un archivo ajeno al juego.'
    Assert ((Get-Content -LiteralPath (Join-Path $fixture.work 'backup/ArcanaArena.exe') -Raw) -eq 'old-exe') 'Falta la copia de recuperacion.'
    Assert ((Test-Path -LiteralPath (Join-Path $fixture.work 'ready')) -and (Test-Path -LiteralPath (Join-Path $fixture.work 'success'))) 'Falta el handshake de preparacion o resultado.'
    Assert (!(Test-Path -LiteralPath $fixture.zip)) 'La descarga instalada no se limpio.'
    $passed++

    foreach ($installed in @('99.0.1', '99.0.2')) {
        foreach ($preview in @($false, $true)) {
            $fixture = Fixture; Text (Join-Path $fixture.install 'VERSION.txt') $installed; Zip $fixture
            $result = Run $fixture -Preview:$preview
            if ($installed -eq '99.0.1' -and $preview) {
                Assert ($result -eq 0) 'Se bloqueo una compilacion de pruebas posterior de igual version.'
                Assert ((Get-Content -LiteralPath (Join-Path $fixture.install 'ArcanaArena.pck') -Raw) -eq 'new-content') 'No se sustituyo la compilacion de pruebas.'
            } else {
                Assert ($result -ne 0) 'Se reinstalo la version estable o se permitio retroceder.'
                Assert ((Get-Content -LiteralPath (Join-Path $fixture.install 'ArcanaArena.exe') -Raw) -eq 'old-exe') 'La operacion rechazada altero la instalacion.'
                Assert (!(Test-Path -LiteralPath (Join-Path $fixture.work 'ready'))) 'Se solicito cerrar el juego antes de rechazar la version.'
            }
            $passed++
        }
    }

    $fixture = Fixture; Zip $fixture
    Assert ((Run $fixture ('0' * 64)) -ne 0) 'Se acepto un ZIP con hash incorrecto.'
    Untouched $fixture
    Assert (!(Test-Path -LiteralPath (Join-Path $fixture.work 'ready'))) 'Se solicito cerrar el juego antes de verificar.'
    $passed++

    foreach ($entryName in @('../outside.txt','C:/outside.txt','CON.txt','runtime/../outside.txt','ArcanaArena.EXE')) {
        $fixture = Fixture; Zip $fixture
        $zip = [IO.Compression.ZipFile]::Open($fixture.zip, [IO.Compression.ZipArchiveMode]::Update)
        try { $entry = $zip.CreateEntry($entryName); $writer = [IO.StreamWriter]::new($entry.Open()); $writer.Write('unsafe'); $writer.Dispose() }
        finally { $zip.Dispose() }
        Assert ((Run $fixture) -ne 0) "Se acepto la entrada insegura $entryName."
        Untouched $fixture
        $passed++
    }

    $fixture = Fixture
    Text (Join-Path $fixture.package 'VERSION.txt') '99.0.0'
    Zip $fixture
    Assert ((Run $fixture) -ne 0) 'Se acepto otra version dentro del ZIP.'
    Untouched $fixture
    $passed++

    $fixture = Fixture
    Remove-Item -LiteralPath (Join-Path $fixture.package 'runtime/coreclr.dll')
    Zip $fixture
    Assert ((Run $fixture) -ne 0) 'Se acepto un paquete sin runtime del cliente.'
    Untouched $fixture
    $passed++

    $fixture = Fixture; Zip $fixture
    $zip = [IO.Compression.ZipFile]::Open($fixture.zip, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $zip.GetEntry('ArcanaArena.pck').Delete()
        $entry = $zip.CreateEntry('ArcanaArena.pck'); $writer = [IO.StreamWriter]::new($entry.Open()); $writer.Write('changed'); $writer.Dispose()
    } finally { $zip.Dispose() }
    Assert ((Run $fixture) -ne 0) 'Se acepto un archivo distinto del manifiesto interno.'
    Untouched $fixture
    $passed++

    if ($env:OS -eq 'Windows_NT') {
        $fixture = Fixture; Zip $fixture
        $held = [IO.File]::Open((Join-Path $fixture.install 'ArcanaArena.pck'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        try { Assert ((Run $fixture) -ne 0) 'Se acepto una sustitucion con el PCK en uso.' }
        finally { $held.Dispose() }
        Untouched $fixture
        Assert ((Get-Content -LiteralPath (Join-Path $fixture.install 'ArcanaArena.pck') -Raw) -eq 'old-pck') 'Se altero el archivo bloqueado.'
        Assert (!(Get-Content -LiteralPath (Join-Path $fixture.work 'error.txt') -Raw).Contains('Recuperacion pendiente')) 'El rollback no termino.'
        $passed++
    }
    Write-Host "WINDOWS_AUTOMATIC_UPDATE_OK: $passed casos, instalacion, rutas, version, SHA256, configuracion, limpieza y recuperacion."
} catch {
    foreach ($errorFile in Get-ChildItem -LiteralPath $root -Recurse -File -Filter error.txt -ErrorAction SilentlyContinue) {
        Write-Host (Get-Content -LiteralPath $errorFile.FullName -Raw)
    }
    throw
} finally { if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force } }
