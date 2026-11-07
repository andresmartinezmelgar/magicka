#requires -Version 5.1
[CmdletBinding()]
param([string]$Repository = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent), [string]$Branch = 'pruebas/dashboard-estadisticas')
$ErrorActionPreference = 'Stop'
$Repository = [IO.Path]::GetFullPath($Repository)
$stateFolder = Join-Path $env:LOCALAPPDATA 'ArcanaArenaDashboard'
New-Item -ItemType Directory -Force -Path $stateFolder | Out-Null
$logFile = Join-Path $stateFolder 'updates.log'
function Write-Log([string]$Message) { Add-Content -LiteralPath $logFile -Encoding UTF8 -Value ((Get-Date -Format 's') + ' ' + $Message) }
if ((Test-Path -LiteralPath $logFile) -and (Get-Item -LiteralPath $logFile).Length -gt 1048576) { Move-Item -LiteralPath $logFile -Destination ($logFile + '.previous') -Force }
$hasher = [Security.Cryptography.SHA256]::Create()
$repoKey = [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($Repository.ToLowerInvariant()))).Replace('-', '').Substring(0, 20)
$hasher.Dispose()
$mutex = New-Object Threading.Mutex($false, ('Local\ArcanaDashboard-' + $repoKey))
$locked = $false
$previousPrompt = $env:GIT_TERMINAL_PROMPT
try {
    try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { Write-Log 'Omitido: otra actualizacion sigue en curso.'; exit 0 }
    $gitExe = (Get-Command git.exe -ErrorAction Stop).Source
    function Invoke-RepoGit([string[]]$Arguments) {
        $result = & $gitExe -C $Repository @Arguments 2>&1
        if ($LASTEXITCODE -ne 0) { throw ('git ' + $Arguments[0] + ' ha fallado: ' + ($result -join ' ')) }
        return $result
    }
    $env:GIT_TERMINAL_PROMPT = '0'
    Invoke-RepoGit -Arguments @('check-ref-format', '--branch', $Branch) | Out-Null
    $remote = ((Invoke-RepoGit -Arguments @('remote', 'get-url', 'origin')) -join '').Trim()
    if ($remote -notmatch '^(https://github\.com/andresmartinezmelgar/magicka(?:\.git)?|git@github\.com:andresmartinezmelgar/magicka(?:\.git)?)$') { throw 'El remoto origin no corresponde a Arcana Arena.' }
    $current = ((Invoke-RepoGit -Arguments @('branch', '--show-current')) -join '').Trim()
    if ($current -ne $Branch) { throw ('Rama inesperada: ' + $current + '. Se esperaba ' + $Branch) }
    $dirty = Invoke-RepoGit -Arguments @('status', '--porcelain', '--untracked-files=normal')
    if ($dirty) { throw 'Hay cambios locales. No se ha modificado ni descartado ningun archivo.' }
    $before = ((Invoke-RepoGit -Arguments @('rev-parse', 'HEAD')) -join '').Trim()
    Invoke-RepoGit -Arguments @('fetch', '--no-tags', 'origin', ('refs/heads/' + $Branch + ':refs/remotes/origin/' + $Branch)) | Out-Null
    Invoke-RepoGit -Arguments @('merge', '--ff-only', ('refs/remotes/origin/' + $Branch)) | Out-Null
    $after = ((Invoke-RepoGit -Arguments @('rev-parse', 'HEAD')) -join '').Trim()
    Write-Log ('OK ' + $before.Substring(0, 8) + ' -> ' + $after.Substring(0, 8))
} catch {
    Write-Log ('ERROR ' + $_.Exception.Message)
    Write-Error $_
    exit 1
} finally {
    $env:GIT_TERMINAL_PROMPT = $previousPrompt
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
