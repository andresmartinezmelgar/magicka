param(
    [Parameter(Mandatory=$true)][string]$ArchivePath,
    [Parameter(Mandatory=$true)][string]$InstallDirectory,
    [Parameter(Mandatory=$true)][string]$ExpectedVersion,
    [Parameter(Mandatory=$true)][string]$ExpectedSha256,
    [Parameter(Mandatory=$true)][string]$WorkDirectory,
    [int]$ParentPid = 0,
    [long]$ParentStartTicks = 0,
    [switch]$NoRestart
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
$ArchivePath = [IO.Path]::GetFullPath($ArchivePath)
$changed = [Collections.Generic.List[object]]::new()
$stage = Join-Path $WorkDirectory 'staged'
$backup = Join-Path $WorkDirectory 'backup'
$temporaryFiles = [Collections.Generic.List[string]]::new()
$restarted = $false

function Safe-RelativePath([string]$Name) {
    $name = $Name.Replace('\', '/').TrimEnd([char]'/')
    if (!$name -or $name.Length -gt 240 -or $name.StartsWith('/') -or $name -match '[<>:"|?*\x00-\x1f]') {
        throw 'El paquete contiene una ruta no valida.'
    }
    foreach ($part in $name.Split('/')) {
        if (!$part -or $part -in @('.', '..') -or $part.EndsWith('.') -or $part.EndsWith(' ') -or
            $part -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') {
            throw "Ruta de paquete no permitida: $Name"
        }
    }
    return $name
}

function Check-Destination([string]$Relative) {
    $target = Join-Path $InstallDirectory $Relative
    $at = $target
    while ($at -and $at.Length -ge $InstallDirectory.Length) {
        if (Test-Path -LiteralPath $at) {
            if ((Get-Item -LiteralPath $at -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'La instalacion contiene enlaces; no se sustituiran sus archivos.'
            }
        }
        if ($at -eq $InstallDirectory) { break }
        $at = [IO.Path]::GetDirectoryName($at)
    }
    if ((Test-Path -LiteralPath $target) -and !(Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "Una carpeta ocupa el lugar de $Relative."
    }
}

function Protected-Configuration([string]$Relative) {
    return $Relative -in @('account-server.json','google-desktop-oauth.json','service-account.json',
        'server/account-server.json','server/service-account.json','server/appsettings.json',
        'server/appsettings.Production.json')
}

function Replace-File([string]$Source, [string]$Target) {
    $next = $Target + '.arcana-new-' + [Guid]::NewGuid().ToString('N')
    $temporaryFiles.Add($next)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Target)) | Out-Null
    [IO.File]::Copy($Source, $next, $false)
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            if ([IO.File]::Exists($Target)) { [IO.File]::Replace($next, $Target, [NullString]::Value) }
            else { [IO.File]::Move($next, $Target) }
            return
        } catch [IO.IOException] {
            if ($attempt -eq 19) { throw }
            Start-Sleep -Milliseconds 250
        }
    }
}

try {
    $InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory)
    if ($InstallDirectory -ne [IO.Path]::GetPathRoot($InstallDirectory)) {
        $InstallDirectory = $InstallDirectory.TrimEnd([char]'\', [char]'/')
    }
    if (!(Test-Path -LiteralPath (Join-Path $InstallDirectory 'ArcanaArena.exe') -PathType Leaf)) {
        throw 'No se ha encontrado la instalacion actual de Arcana Arena.'
    }
    $installedVersionFile = Join-Path $InstallDirectory 'VERSION.txt'
    if ((Test-Path -LiteralPath $installedVersionFile) -and
        [version]$ExpectedVersion -le [version](Get-Content -LiteralPath $installedVersionFile -Raw).Trim()) {
        throw 'La carpeta ya contiene esta version o una posterior; no se sustituira por una anterior.'
    }
    if ($ExpectedVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or
        $ExpectedSha256 -notmatch '^[a-fA-F0-9]{64}$' -or
        (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash -ne $ExpectedSha256) {
        throw 'La integridad del archivo descargado no coincide.'
    }
    New-Item -ItemType Directory -Path $WorkDirectory, $stage, $backup -Force | Out-Null
    $zip = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [long]$expanded = 0
    try {
        if ($zip.Entries.Count -gt 8192) { throw 'Demasiados archivos en la actualizacion.' }
        foreach ($entry in $zip.Entries) {
            $relative = Safe-RelativePath $entry.FullName
            if (!$seen.Add($relative) -or (($entry.ExternalAttributes -shr 16) -band 61440) -eq 40960) {
                throw 'El paquete contiene rutas duplicadas o enlaces.'
            }
            $expanded += $entry.Length
            if ($expanded -gt 4294967296) { throw 'El paquete descomprimido supera 4 GB.' }
            $target = Join-Path $stage $relative
            if ($entry.FullName.EndsWith('/') -or $entry.FullName.EndsWith('\')) {
                [IO.Directory]::CreateDirectory($target) | Out-Null
            } else {
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
            }
        }
    } finally { $zip.Dispose() }
    foreach ($relative in @('ArcanaArena.exe','ArcanaArena.pck','server/Arcana.Server.exe',
        'VERSION.txt','UPDATE-MANIFEST.json')) {
        if (!(Test-Path -LiteralPath (Join-Path $stage $relative) -PathType Leaf)) { throw "Falta $relative." }
    }
    if ((Get-Content -LiteralPath (Join-Path $stage 'VERSION.txt') -Raw).Trim() -ne $ExpectedVersion) {
        throw 'La version del paquete no coincide con la Release.'
    }
    $client = @(Get-ChildItem -LiteralPath $stage -Recurse -File -Filter Arcana.Client.dll)
    $runtime = @(Get-ChildItem -LiteralPath $stage -Recurse -File -Filter coreclr.dll |
        Where-Object { !$_.FullName.StartsWith((Join-Path $stage 'server') + [IO.Path]::DirectorySeparatorChar) })
    if (!$client.Count -or !$runtime.Count) { throw 'Falta el cliente o su runtime .NET.' }
    $manifest = Get-Content -LiteralPath (Join-Path $stage 'UPDATE-MANIFEST.json') -Raw | ConvertFrom-Json
    if ($manifest.version -ne $ExpectedVersion -or @($manifest.files).Count -gt 8192) {
        throw 'El manifiesto de archivos no corresponde a esta version.'
    }
    $owned = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $manifest.files) {
        $relative = Safe-RelativePath $file.path
        $source = Join-Path $stage $relative
        if (!$owned.Add($relative) -or $relative -eq 'UPDATE-MANIFEST.json' -or
            !(Test-Path -LiteralPath $source -PathType Leaf) -or $file.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or
            (Get-Item -LiteralPath $source).Length -ne $file.length -or
            (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $file.sha256) {
            throw "Archivo incompleto o alterado: $relative"
        }
        Check-Destination $relative
    }
    foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
        $relative = $file.FullName.Substring($stage.Length + 1).Replace('\','/')
        if ($relative -ne 'UPDATE-MANIFEST.json' -and !$owned.Contains($relative)) {
            throw 'El ZIP contiene un archivo ajeno al manifiesto.'
        }
    }
    Check-Destination 'UPDATE-MANIFEST.json'
    $probe = Join-Path $InstallDirectory ('.arcana-write-' + [Guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($probe, ''); [IO.File]::Delete($probe)
    [IO.File]::WriteAllText((Join-Path $WorkDirectory 'ready'), 'ready')

    if ($ParentPid -gt 0) {
        $parent = $null
        try { $parent = [Diagnostics.Process]::GetProcessById($ParentPid) } catch [ArgumentException] { }
        if ($null -ne $parent) {
            try {
                if ($parent.StartTime.ToUniversalTime().Ticks -eq $ParentStartTicks -and !$parent.WaitForExit(60000)) {
                    throw 'El juego no se ha cerrado; la instalacion se conserva.'
                }
            } finally { $parent.Dispose() }
        }
    }
    $files = @($manifest.files | Sort-Object @{ Expression = { $_.path -eq 'VERSION.txt' } }, path)
    $files += [pscustomobject]@{ path = 'UPDATE-MANIFEST.json' }
    foreach ($file in $files) {
        $relative = Safe-RelativePath $file.path
        $target = Join-Path $InstallDirectory $relative
        Check-Destination $relative
        if ((Protected-Configuration $relative) -and [IO.File]::Exists($target)) { continue }
        $previous = Join-Path $backup $relative
        $existed = [IO.File]::Exists($target)
        if ($existed) {
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($previous)) | Out-Null
            [IO.File]::Copy($target, $previous, $false)
        }
        $item = [pscustomobject]@{ path=$relative; existed=$existed; applied=$false }
        $changed.Add($item)
        $changed | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $WorkDirectory 'rollback.json') -Encoding UTF8
        Replace-File (Join-Path $stage $relative) $target
        $item.applied = $true
    }
    if (!$NoRestart) {
        $game = Start-Process -FilePath (Join-Path $InstallDirectory 'ArcanaArena.exe') -WorkingDirectory $InstallDirectory -PassThru
        try {
            if ($game.WaitForExit(2000) -and $game.ExitCode -ne 0) { throw 'La nueva version no ha podido iniciar.' }
        } finally { $game.Dispose() }
        $restarted = $true
    }
    [IO.File]::WriteAllText((Join-Path $WorkDirectory 'success'), $ExpectedVersion)
    $failed = Join-Path ([IO.Path]::GetDirectoryName($WorkDirectory)) 'last-failure.json'
    try { if ([IO.File]::Exists($failed)) { [IO.File]::Delete($failed) } } catch [IO.IOException] { }
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
    try { [IO.File]::Delete($ArchivePath) } catch [IO.IOException] { }
    foreach ($older in Get-ChildItem -LiteralPath ([IO.Path]::GetDirectoryName($WorkDirectory)) -Directory -Filter 'install-*') {
        if ($older.FullName -ne $WorkDirectory -and (Test-Path -LiteralPath (Join-Path $older.FullName 'success'))) {
            Remove-Item -LiteralPath $older.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    # Conservar una copia completa de los archivos sustituidos hasta la siguiente actualizacion.
    exit 0
} catch {
    $reason = $_.Exception.Message
    $recoveryErrors = [Collections.Generic.List[string]]::new()
    for ($i = $changed.Count - 1; $i -ge 0; $i--) {
        $item = $changed[$i]
        if (!$item.applied) { continue }
        try {
            $target = Join-Path $InstallDirectory $item.path
            if ($item.existed) { Replace-File (Join-Path $backup $item.path) $target }
            elseif ([IO.File]::Exists($target)) { [IO.File]::Delete($target) }
        } catch { $recoveryErrors.Add($_.Exception.Message) }
    }
    New-Item -ItemType Directory -Path $WorkDirectory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $WorkDirectory 'error.txt'), $reason +
        $(if ($recoveryErrors.Count) { ' Recuperacion pendiente: ' + ($recoveryErrors -join '; ') } else { '' }))
    $failure = @{ version=$ExpectedVersion; reason=$reason } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path ([IO.Path]::GetDirectoryName($WorkDirectory)) 'last-failure.json'), $failure)
    if ($changed.Count -and !$NoRestart -and !$restarted -and !$recoveryErrors.Count) {
        Start-Process -FilePath (Join-Path $InstallDirectory 'ArcanaArena.exe') -WorkingDirectory $InstallDirectory
    }
    exit 1
} finally {
    foreach ($file in $temporaryFiles) { if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) } }
}
