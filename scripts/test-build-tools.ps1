$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'android-tools-support.ps1')
. (Join-Path $PSScriptRoot 'tool-support.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("Arcana herramientas [prueba] " + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
function Assert([bool]$Condition, [string]$Reason) { if (!$Condition) { throw $Reason } }
function Write-TestArchive {
    param([string]$Path, [hashtable]$Files)
    $archive = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $Files.Keys) {
            $entry = $archive.CreateEntry($name)
            $writer = [IO.StreamWriter]::new($entry.Open())
            try { $writer.Write($Files[$name]) } finally { $writer.Dispose() }
        }
    } finally { $archive.Dispose() }
}
try {
    $sdk = Join-Path $testRoot 'android sdk'
    $latest = Join-Path $sdk 'cmdline-tools/latest'
    $zip = Join-Path $testRoot 'android.zip'
    # Un ZIP real no necesita entradas bin/ y lib/; los padres se crean al extraer archivos.
    Write-TestArchive $zip @{
        'cmdline-tools/bin/sdkmanager.bat' = 'sdkmanager'
        'cmdline-tools/lib/sdkmanager-classpath.jar' = 'classpath'
        'cmdline-tools/lib/sub carpeta/otra.jar' = 'biblioteca'
        'cmdline-tools/source.properties' = 'Pkg.Revision=19.0'
        'cmdline-tools/NOTICE.txt' = 'licencia'
    }
    Install-AndroidCommandLineTools -ArchivePath $zip -SdkPath $sdk
    Assert (Test-AndroidCommandLineTools $latest) 'Faltan sdkmanager, lib o source.properties.'
    Assert (Test-Path -LiteralPath (Join-Path $latest 'lib/sub carpeta/otra.jar')) 'No se extrajeron subdirectorios sin entradas de carpeta.'
    # Simula una instalacion interrumpida: no basta con que exista sdkmanager.bat.
    Remove-Item -LiteralPath (Join-Path $latest 'lib/sdkmanager-classpath.jar')
    Assert (!(Test-AndroidCommandLineTools $latest)) 'Se acepto una instalacion incompleta.'
    Install-AndroidCommandLineTools -ArchivePath $zip -SdkPath $sdk
    Assert (Test-AndroidCommandLineTools $latest) 'No se reparo la instalacion incompleta.'
    $hash = (Get-FileHash -LiteralPath (Join-Path $latest 'bin/sdkmanager.bat')).Hash
    $invalid = Join-Path $testRoot 'incompleto.zip'
    Write-TestArchive $invalid @{ 'cmdline-tools/bin/sdkmanager.bat' = 'incompleto' }
    $rejected = $false
    try { Install-AndroidCommandLineTools -ArchivePath $invalid -SdkPath $sdk } catch { $rejected = $true }
    Assert $rejected 'Se instalo un ZIP incompleto.'
    Assert ((Get-FileHash -LiteralPath (Join-Path $latest 'bin/sdkmanager.bat')).Hash -eq $hash) 'El ZIP incorrecto sustituyo las herramientas anteriores.'
    $corrupt = Join-Path $testRoot 'danado.zip'
    [IO.File]::WriteAllText($corrupt, 'descarga interrumpida')
    Assert (!(Test-AndroidToolsArchive $corrupt)) 'Se acepto una descarga danada.'
    Assert (@(Get-ChildItem -LiteralPath (Join-Path $sdk 'cmdline-tools') -Directory).Count -eq 1) 'Quedaron directorios temporales.'
    Write-Host 'ANDROID_TOOLS_TEST_OK: rutas con espacios y corchetes, ZIP sin directorios, bibliotecas anidadas, reparacion y fallo sin sustituir herramientas.'

    $settingsWarning = @($script:GodotSettingsMessage, '   at: _EDITOR_GET (editor/settings/editor_settings.cpp:1514)')
    Test-GodotBuildLog -Lines (@('Godot 4.5.2', 'Exportacion completada') + $settingsWarning) -ExitCode 0
    foreach ($case in @(
        @{ Lines = $settingsWarning; Code = 1 },
        @{ Lines = @($script:GodotSettingsMessage); Code = 0 },
        @{ Lines = $settingsWarning + @('ERROR: No se pudo cargar una textura.'); Code = 0 },
        @{ Lines = @('SCRIPT ERROR: Invalid call.'); Code = 0 },
        @{ Lines = @('Export failed'); Code = 0 },
        @{ Lines = @('Failed to build project'); Code = 0 }
    )) {
        $rejected = $false
        try { Test-GodotBuildLog -Lines $case.Lines -ExitCode $case.Code } catch { $rejected = $true }
        Assert $rejected 'Se oculto un error real de Godot.'
    }
    Write-Host 'GODOT_BUILD_LOG_OK: aviso concreto con su traza, errores reales incluso con codigo cero y salida fallida rechazados.'

    Assert-AndroidSdkLicenses -Lines @('All SDK package licenses accepted.') -ExitCode 0
    foreach ($case in @(
        @{ Lines = @('7 of 7 SDK package licenses not accepted.'); Code = 0 },
        @{ Lines = @('1 of 7 SDK package licenses not accepted.'); Code = 0 },
        @{ Lines = @('All SDK package licenses accepted.', '1 of 7 SDK package licenses not accepted.'); Code = 0 },
        @{ Lines = @(''); Code = 0 },
        @{ Lines = @('All SDK package licenses accepted.'); Code = 1 }
    )) {
        $rejected = $false
        try { Assert-AndroidSdkLicenses -Lines $case.Lines -ExitCode $case.Code } catch { $rejected = $true }
        Assert $rejected 'Se permitio continuar sin comprobar las licencias Android.'
    }
    $fakeManager = Join-Path $testRoot 'sdk manager.bat'
    [IO.File]::WriteAllText($fakeManager, "@echo off`r`necho All SDK package licenses accepted.`r`nexit /b 0`r`n", [Text.Encoding]::ASCII)
    Confirm-AndroidSdkLicenses -SdkManager $fakeManager -SdkPath $sdk -WorkDirectory $testRoot -Accept
    Confirm-AndroidSdkLicenses -SdkManager $fakeManager -SdkPath $sdk -WorkDirectory $testRoot
    [IO.File]::WriteAllText($fakeManager, "@echo off`r`necho 7 of 7 SDK package licenses not accepted.`r`nexit /b 0`r`n", [Text.Encoding]::ASCII)
    $rejected = $false
    try { Confirm-AndroidSdkLicenses -SdkManager $fakeManager -SdkPath $sdk -WorkDirectory $testRoot -Accept } catch { $rejected = $true }
    Assert $rejected 'sdkmanager con codigo cero y licencias pendientes permitio seguir compilando.'
    Write-Host 'ANDROID_LICENSES_TEST_OK: aceptacion explicita, consola heredada, rutas con espacios, consulta final y licencias pendientes con codigo cero rechazadas.'
} finally { Remove-Item -LiteralPath $testRoot -Recurse -Force }
