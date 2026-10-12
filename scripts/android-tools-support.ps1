# Extrae los archivos del ZIP sin depender de entradas de directorio ni comodines de PowerShell.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

function Assert-AndroidSdkLicenses {
    param([string[]]$Lines, [int]$ExitCode)
    if ($ExitCode -ne 0) { throw "sdkmanager termino con codigo $ExitCode al comprobar las licencias." }
    $output = $Lines -join "`n"
    if ($output -match '(?i)\b\d+ of \d+ SDK package licenses not accepted' -or
        $output -notmatch '(?im)^\s*All SDK package licenses accepted\.?\s*$') {
        throw 'Quedan licencias del SDK Android sin aceptar. Responde y a la revision y a cada licencia, o ejecuta Compilar-APK.cmd -AcceptAndroidLicenses si aceptas sus terminos.'
    }
}

function Confirm-AndroidSdkLicenses {
    param([string]$SdkManager, [string]$SdkPath, [string]$WorkDirectory, [switch]$Accept)
    $arguments = '/d /s /c ""{0}" "--sdk_root={1}" --licenses"' -f $SdkManager, $SdkPath
    $launch = @{ FilePath = $env:ComSpec; ArgumentList = $arguments; NoNewWindow = $true; PassThru = $true }
    if ($Accept) {
        # Start-Process interpreta '[' y ']' de RedirectStandardInput como comodines.
        # Usa un temporal del sistema para que directorios válidos con corchetes no rompan la compilación.
        $answers = [IO.Path]::GetTempFileName()
        [IO.File]::WriteAllText($answers, ("y`r`n" * 100), [Text.Encoding]::ASCII)
        $launch.RedirectStandardInput = $answers
    }
    # La consola heredada permite responder sin que una tuberia de PowerShell 5.1 cierre stdin.
    $process = Start-Process @launch
    try {
        $null = $process.Handle
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "sdkmanager termino con codigo $($process.ExitCode) al mostrar las licencias." }
    } finally {
        $process.Dispose()
        if ($Accept -and $answers -and (Test-Path -LiteralPath $answers)) { Remove-Item -LiteralPath $answers -Force }
    }

    # Reconsulta no interactiva: rechazar lo pendiente nunca acepta una licencia nueva.
    # sdkmanager puede devolver 0 aunque se haya respondido n a todas las licencias.
    $probeInput = [IO.Path]::GetTempFileName()
    $probeOutput = [IO.Path]::GetTempFileName()
    $probeError = [IO.Path]::GetTempFileName()
    [IO.File]::WriteAllText($probeInput, ("n`r`n" * 100), [Text.Encoding]::ASCII)
    $launch.RedirectStandardInput = $probeInput
    $launch.RedirectStandardOutput = $probeOutput
    $launch.RedirectStandardError = $probeError
    $process = Start-Process @launch
    try {
        $null = $process.Handle
        $process.WaitForExit()
        $code = $process.ExitCode
    } finally { $process.Dispose() }
    try {
        $lines = @([IO.File]::ReadAllLines($probeOutput)) + @([IO.File]::ReadAllLines($probeError))
        $lines | ForEach-Object { Write-Host $_ }
        Assert-AndroidSdkLicenses -Lines $lines -ExitCode $code
        Write-Host 'ANDROID_LICENSES_OK: todas las licencias aceptadas en el SDK usado para esta APK.'
    } finally {
        foreach ($temp in @($probeInput, $probeOutput, $probeError)) {
            if ($temp -and (Test-Path -LiteralPath $temp)) { Remove-Item -LiteralPath $temp -Force }
        }
    }
}

function Test-AndroidCommandLineTools {
    param([string]$Directory)
    foreach ($file in @('bin/sdkmanager.bat', 'lib/sdkmanager-classpath.jar', 'source.properties')) {
        $path = Join-Path $Directory $file
        if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) { return $false }
    }
    return $true
}

function Test-AndroidToolsArchive {
    param([string]$ArchivePath)
    $archive = $null
    try {
        $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
        $files = @($archive.Entries | Where-Object { $_.Length -gt 0 } | ForEach-Object { $_.FullName.Replace('\', '/') })
        foreach ($file in @('bin/sdkmanager.bat', 'lib/sdkmanager-classpath.jar', 'source.properties')) {
            if ($files -notcontains "cmdline-tools/$file") { return $false }
        }
        return $true
    } catch { return $false }
    finally { if ($archive) { $archive.Dispose() } }
}

function Install-AndroidCommandLineTools {
    param([Parameter(Mandatory=$true)][string]$ArchivePath, [Parameter(Mandatory=$true)][string]$SdkPath)
    if (!(Test-AndroidToolsArchive $ArchivePath)) { throw 'El ZIP de herramientas Android esta incompleto o danado.' }
    $parent = Join-Path $SdkPath 'cmdline-tools'
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $id = [Guid]::NewGuid().ToString('N')
    $staging = Join-Path $parent "nuevo-$id"
    $backup = Join-Path $parent "anterior-$id"
    $destination = Join-Path $parent 'latest'
    $oldMoved = $false
    $archive = $null
    try {
        $rootPath = [IO.Path]::GetFullPath($staging) + [IO.Path]::DirectorySeparatorChar
        $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if (!$name.StartsWith('cmdline-tools/', [StringComparison]::Ordinal) -or $name.EndsWith('/')) { continue }
            $relative = $name.Substring('cmdline-tools/'.Length)
            $path = [IO.Path]::GetFullPath((Join-Path $staging $relative))
            if (!$path.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) { throw 'El ZIP Android contiene una ruta fuera de su carpeta.' }
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $path, $true)
        }
        $archive.Dispose(); $archive = $null
        if (!(Test-AndroidCommandLineTools $staging)) { throw 'La extraccion no genero sdkmanager y sus bibliotecas.' }
        if (Test-Path -LiteralPath $destination) {
            Move-Item -LiteralPath $destination -Destination $backup
            $oldMoved = $true
        }
        try { Move-Item -LiteralPath $staging -Destination $destination }
        catch {
            if ($oldMoved -and !(Test-Path -LiteralPath $destination)) { Move-Item -LiteralPath $backup -Destination $destination }
            throw
        }
        if ($oldMoved) { Remove-Item -LiteralPath $backup -Recurse -Force }
        Write-Host 'ANDROID_TOOLS_OK: cmdline-tools/latest, sdkmanager y bibliotecas comprobados.'
    } finally {
        if ($archive) { $archive.Dispose() }
        if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    }
}
