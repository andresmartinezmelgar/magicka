function Enter-ArcanaBuildLock {
    param([Parameter(Mandatory=$true)][string]$RepositoryRoot)
    $directory = Join-Path $RepositoryRoot '.tools'
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $path = Join-Path $directory 'arcana-build.lock'
    $lease = [IO.File]::Open($path, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::ReadWrite)
    try {
        # El mismo byte se bloquea con msvcrt/lockf en el launcher Bash/Python.
        # No se elimina el archivo: hacerlo permitiria bloquear dos archivos distintos.
        $lease.Lock(0, 1)
    } catch {
        $lease.Dispose()
        throw "Ya hay una compilacion de Arcana Arena en esta carpeta. Bloqueo: $path"
    }
    return $lease
}

function Exit-ArcanaBuildLock {
    param([IO.FileStream]$Lease)
    if ($null -ne $Lease) {
        try { $Lease.Unlock(0, 1) } finally { $Lease.Dispose() }
    }
}
