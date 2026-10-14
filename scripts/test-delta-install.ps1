param([Parameter(Mandatory=$true)][string]$FixtureDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$fixtures = (Resolve-Path -LiteralPath $FixtureDirectory).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ("Arcana differential O'Connor " + [Guid]::NewGuid().ToString('N'))
$updater = Join-Path $PSScriptRoot 'Apply-WindowsUpdate.ps1'
$shell = Join-Path $PSHOME $(if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell.exe' })
if ($env:OS -eq 'Windows_NT' -and $PSVersionTable.PSEdition -eq 'Core') { $shell += '.exe' }
$passed = 0
function Assert([bool]$Condition, [string]$Reason) { if (!$Condition) { throw $Reason } }
function Install-Fixture {
    $case = Join-Path $root ([Guid]::NewGuid().ToString('N'))
    $install = Join-Path $case 'Juego'
    [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $fixtures 'base-windows.zip'), $install)
    [IO.File]::WriteAllText((Join-Path $install 'account-server.json'), 'local-private-config')
    [IO.File]::WriteAllText((Join-Path $install 'personal.txt'), 'personal-file')
    $patch = Join-Path $case 'update.zip'
    Copy-Item -LiteralPath (Join-Path $fixtures 'windows-delta.zip') -Destination $patch
    return [pscustomobject]@{ root=$case; install=$install; patch=$patch }
}
function Apply($Case, [string]$Archive = '', [int]$WaitPid = 0, [long]$WaitTicks = 0) {
    if (!$Archive) { $Archive = $Case.patch }
    $work = Join-Path $Case.root ('updates/install-' + [Guid]::NewGuid().ToString('N'))
    & $shell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $updater -ArchivePath $Archive `
        -InstallDirectory $Case.install -ExpectedVersion '0.17.17' -ExpectedSha256 (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash `
        -WorkDirectory $work -ParentPid $WaitPid -ParentStartTicks $WaitTicks -NoRestart
    return $LASTEXITCODE
}
function Check-New($Case) {
    $manifest = Get-Content -LiteralPath (Join-Path $Case.install 'UPDATE-MANIFEST.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($file in $manifest.files) {
        if ($file.path -eq 'account-server.json') { continue }
        Assert ((Get-FileHash -LiteralPath (Join-Path $Case.install $file.path) -Algorithm SHA256).Hash -eq $file.sha256) 'Un archivo nuevo no coincide con la distribucion completa.'
    }
    Assert ((Get-Content -LiteralPath (Join-Path $Case.install 'account-server.json') -Raw) -eq 'local-private-config') 'Se sustituyo la configuracion privada.'
    Assert ((Get-Content -LiteralPath (Join-Path $Case.install 'personal.txt') -Raw) -eq 'personal-file') 'Se elimino un archivo del usuario.'
}
try {
    $case = Install-Fixture
    Assert ((Apply $case) -eq 0) 'Fallo la instalacion diferencial.'
    Check-New $case
    Assert (!(Test-Path -LiteralPath (Join-Path $case.install 'legacy.dll'))) 'No se retiro el archivo obsoleto perteneciente al paquete.'
    $passed++

    $case = Install-Fixture
    [IO.File]::WriteAllText((Join-Path $case.install 'runtime/coreclr.dll'), 'changed-local-base')
    Assert ((Apply $case) -ne 0) 'Se acepto una base alterada.'
    Assert ((Get-Content -LiteralPath (Join-Path $case.install 'VERSION.txt') -Raw) -eq '0.17.16') 'Se altero la version tras rechazar el parche.'
    Assert ((Get-Content -LiteralPath (Join-Path $case.install 'ArcanaArena.exe') -Raw) -eq 'old-exe') 'Se sustituyo el EXE antes de verificar la base.'
    $full = Join-Path $case.root 'complete.zip'
    Copy-Item -LiteralPath (Join-Path $fixtures 'target-windows.zip') -Destination $full
    Assert ((Apply $case $full) -eq 0) 'El paquete completo no recupera una base incompatible.'
    Check-New $case
    $passed++

    $case = Install-Fixture
    [IO.File]::WriteAllText((Join-Path $case.install 'legacy.dll'), 'user-modified-file')
    Assert ((Apply $case) -eq 0) 'Fallo el parche con un archivo antiguo modificado.'
    Assert ((Get-Content -LiteralPath (Join-Path $case.install 'legacy.dll') -Raw) -eq 'user-modified-file') 'Se borro un archivo modificado ajeno al contenido publicado.'
    $passed++

    if ($env:OS -eq 'Windows_NT') {
        $case = Install-Fixture
        $held = [IO.File]::Open((Join-Path $case.install 'ArcanaArena.pck'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        try { Assert ((Apply $case) -ne 0) 'Se sustituyo un PCK bloqueado.' } finally { $held.Dispose() }
        Assert ((Get-Content -LiteralPath (Join-Path $case.install 'legacy.dll') -Raw) -eq 'obsolete-owned-file') 'No se recupero el archivo eliminado tras el fallo.'
        Assert ((Get-Content -LiteralPath (Join-Path $case.install 'ArcanaArena.exe') -Raw) -eq 'old-exe') 'No se recupero el ejecutable anterior.'
        $passed++

        $case = Install-Fixture
        $child = Join-Path $case.root 'parent.ps1'
        $ready = Join-Path $case.root 'holding'
        [IO.File]::WriteAllText($child, 'param($Pck,$Ready)' + "`n" +
            '$held=[IO.File]::Open($Pck,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)' + "`n" +
            '[IO.File]::WriteAllText($Ready,"ready"); Start-Sleep -Milliseconds 2500; $held.Dispose()')
        $parent = Start-Process -FilePath $shell -ArgumentList @('-NoProfile','-File',('"'+$child+'"'),
            '-Pck',('"'+(Join-Path $case.install 'ArcanaArena.pck')+'"'),'-Ready',('"'+$ready+'"')) -PassThru
        try {
            $deadline = [Diagnostics.Stopwatch]::StartNew()
            while (!(Test-Path -LiteralPath $ready)) {
                if ($deadline.Elapsed.TotalSeconds -gt 10) { throw 'No se inicio el proceso de prueba.' }
                Start-Sleep -Milliseconds 30
            }
            $ticks = $parent.StartTime.ToUniversalTime().Ticks
            Assert ((Apply $case '' $parent.Id $ticks) -eq 0) 'El instalador no espero al cierre del proceso original.'
            Check-New $case
            $passed++
        } finally { if (!$parent.HasExited) { $parent.Kill() }; $parent.Dispose() }
    }
    Write-Host "WINDOWS_DIFFERENTIAL_UPDATE_OK: $passed casos, reutilizacion, SHA256, configuracion, eliminaciones, alternativa completa y rollback."
} catch {
    foreach ($errorFile in Get-ChildItem -LiteralPath $root -Recurse -File -Filter error.txt -ErrorAction SilentlyContinue) {
        Write-Host (Get-Content -LiteralPath $errorFile.FullName -Raw)
    }
    throw
} finally { if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force } }
