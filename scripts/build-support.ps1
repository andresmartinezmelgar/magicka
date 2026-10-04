# Funciones compartidas por la compilacion y su prueba de sustitucion en Windows.
function Test-GamePackage {
    param([Parameter(Mandatory=$true)][string]$Package)
    foreach ($file in @('ArcanaArena.exe', 'ArcanaArena.pck', 'server/Arcana.Server.exe')) {
        if (!(Test-Path -LiteralPath (Join-Path $Package $file) -PathType Leaf)) { throw "Falta $file en el paquete." }
    }
    $coreClr = Get-ChildItem -LiteralPath $Package -Recurse -Filter coreclr.dll |
        Where-Object { $_.FullName -notlike "$(Join-Path $Package 'server')\*" }
    $clientDll = Get-ChildItem -LiteralPath $Package -Recurse -Filter Arcana.Client.dll
    if (!$coreClr -or !$clientDll) { throw 'Falta el runtime .NET o el cliente compilado.' }
}

function Publish-GamePackage {
    param([Parameter(Mandatory=$true)][string]$RepositoryRoot, [Parameter(Mandatory=$true)][string]$Package)
    $repoRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $destination = Join-Path $repoRoot 'Juego'
    $artifactRoot = Join-Path $repoRoot 'artifacts'
    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
    foreach ($path in @($destination, $artifactRoot)) {
        if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "La carpeta de salida no puede ser un enlace: $path"
        }
    }
    Test-GamePackage -Package $Package
    $id = [Guid]::NewGuid().ToString('N')
    $staging = Join-Path $artifactRoot "Juego-nuevo-$id"
    $backup = Join-Path $artifactRoot "Juego-anterior-$id"
    $oldMoved = $false
    try {
        New-Item -ItemType Directory -Path $staging | Out-Null
        Get-ChildItem -LiteralPath $Package -Force | Copy-Item -Destination $staging -Recurse -Force
        Test-GamePackage -Package $staging
        if (Test-Path -LiteralPath $destination) { Move-Item -LiteralPath $destination -Destination $backup; $oldMoved = $true }
        try { Move-Item -LiteralPath $staging -Destination $destination }
        catch {
            if ($oldMoved -and !(Test-Path -LiteralPath $destination)) { Move-Item -LiteralPath $backup -Destination $destination }
            throw
        }
        if ($oldMoved) {
            try { Remove-Item -LiteralPath $backup -Recurse -Force }
            catch { Write-Warning "Juego actualizado. La copia anterior sigue en $backup porque hay archivos en uso." }
        }
        Write-Host "JUEGO_LISTO: $(Join-Path $destination 'ArcanaArena.exe')"
    } finally {
        if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    }
}
