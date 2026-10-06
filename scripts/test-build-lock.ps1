$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build-lock.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) ('arcana lock [espacios] ' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$lease = $null
$process = $null
try {
    $lease = Enter-ArcanaBuildLock -RepositoryRoot $root
    $blocked = $false
    try { $other = Enter-ArcanaBuildLock -RepositoryRoot $root; Exit-ArcanaBuildLock $other }
    catch { $blocked = $_.Exception.Message.Contains('Ya hay una compilacion') }
    if (!$blocked) { throw 'Una segunda compilacion PowerShell adquirio el bloqueo.' }
    & python (Join-Path $PSScriptRoot 'build-lock.py') $root python -c 'pass'
    if ($LASTEXITCODE -ne 75) { throw 'Python no respeta el bloqueo de PowerShell.' }
    Exit-ArcanaBuildLock $lease; $lease = $null

    $ready = Join-Path $root 'ready'
    $release = Join-Path $root 'release'
    $holder = Join-Path $root 'holder.py'
    [IO.File]::WriteAllText($holder, "from pathlib import Path`nimport sys, time`nPath(sys.argv[1]).touch()`nwhile not Path(sys.argv[2]).exists(): time.sleep(0.02)`n")
    $launcher = Join-Path $PSScriptRoot 'build-lock.py'
    $arguments = '"{0}" "{1}" python "{2}" "{3}" "{4}"' -f $launcher, $root, $holder, $ready, $release
    # Process.Start conserva el codigo de salida en Windows PowerShell 5.1.
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = (Get-Command python).Source
    $start.Arguments = $arguments
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $process = [Diagnostics.Process]::Start($start)
    $deadline = (Get-Date).AddSeconds(10)
    while (!(Test-Path -LiteralPath $ready) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 50 }
    if (!(Test-Path -LiteralPath $ready)) { throw 'Python no adquirio el bloqueo.' }
    $blocked = $false
    try { $other = Enter-ArcanaBuildLock -RepositoryRoot $root; Exit-ArcanaBuildLock $other }
    catch { $blocked = $_.Exception.Message.Contains('Ya hay una compilacion') }
    if (!$blocked) { throw 'PowerShell no respeta el bloqueo de Python.' }
    [IO.File]::WriteAllText($release, '')
    if (!$process.WaitForExit(10000) -or $process.ExitCode -ne 0) { throw 'El launcher Python no termino correctamente.' }
    $lease = Enter-ArcanaBuildLock -RepositoryRoot $root
    Exit-ArcanaBuildLock $lease; $lease = $null
    Write-Host 'BUILD_LOCK_WINDOWS_OK: PowerShell y Python comparten el bloqueo y lo liberan al terminar.'
} finally {
    Exit-ArcanaBuildLock $lease
    if ($process -and !$process.HasExited) {
        [IO.File]::WriteAllText((Join-Path $root 'release'), '')
        if (!$process.WaitForExit(5000)) { $process.Kill() }
    }
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
# El codigo 75 anterior corresponde al rechazo esperado de la segunda compilacion.
exit 0
