# Extrae los archivos del ZIP sin depender de entradas de directorio ni comodines de PowerShell.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

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
