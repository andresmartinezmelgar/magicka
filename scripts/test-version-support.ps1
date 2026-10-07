$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'version-support.ps1')
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Arcana versiones [prueba] ' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Assert([bool]$Condition, [string]$Reason) { if (!$Condition) { throw $Reason } }
function Set-TestVersion([string]$Name) {
    [IO.File]::WriteAllText((Join-Path $testRoot 'VERSION'), $Name, $utf8)
    return Get-ArcanaBuildVersion -RepositoryRoot $testRoot
}
function Reject([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Assert $rejected 'Se aceptaron metadatos o versiones invalidos.'
}
try {
    $base = [IO.File]::ReadAllText((Join-Path $repo 'src/Arcana.Client/export_presets.cfg'))
    $stale = [regex]::Replace($base, '(?m)^(application/(file|product)_version)=[^\r\n]*', '$1="0.12.14.0"')
    $stale = [regex]::Replace($stale, '(?m)^version/name=[^\r\n]*', 'version/name="0.12.14"')
    $stale = [regex]::Replace($stale, '(?m)^version/code=[^\r\n]*', 'version/code=134')
    $version = Set-TestVersion "0.12.19`r`n"
    $updated = Update-ArcanaExportVersion -Presets $stale -Version $version
    Assert ($version.Windows -eq '0.12.19.0' -and $version.AndroidCode -eq 12019) 'Formato de version incorrecto.'
    foreach ($line in @('application/file_version="0.12.19.0"', 'application/product_version="0.12.19.0"',
        'version/name="0.12.19"', 'version/code=12019', 'export_path="../../artifacts/android/ArcanaArena-v0.12.19-Android-arm64.apk"')) {
        Assert ($updated.Contains($line)) "No se sincronizo $line."
    }
    Assert ($updated.Contains('package/unique_name="com.arcana.arena"')) 'Se modifico la identidad de la app.'
    Assert ((Update-ArcanaExportVersion -Presets $updated -Version $version) -ceq $updated) 'Sincronizar dos veces altera el preset.'
    $crlf = $stale.Replace("`r`n", "`n").Replace("`n", "`r`n")
    $updatedCrlf = Update-ArcanaExportVersion -Presets $crlf -Version $version
    Assert (!$updatedCrlf.Replace("`r`n", '').Contains("`n")) 'Se modificaron los saltos CRLF.'

    Assert-ArcanaWindowsVersion -Metadata ([pscustomobject]@{FileVersion='0.12.19.0';ProductVersion='0.12.19.0'}) -Version $version
    Reject { Assert-ArcanaWindowsVersion -Metadata ([pscustomobject]@{FileVersion='0.12.14.0';ProductVersion='0.12.19.0'}) -Version $version }
    Reject { Assert-ArcanaWindowsVersion -Metadata ([pscustomobject]@{FileVersion='0.12.19.0';ProductVersion='0.12.14.0'}) -Version $version }
    Assert-ArcanaAndroidVersion -Manifest "package: name='com.arcana.arena' versionCode='12019' versionName='0.12.19'" -Version $version
    Reject { Assert-ArcanaAndroidVersion -Manifest "versionCode='12019' versionName='0.12.14'" -Version $version }
    Reject { Assert-ArcanaAndroidVersion -Manifest "versionCode='134' versionName='0.12.19'" -Version $version }
    Reject { Assert-ArcanaAndroidVersion -Manifest 'sin version' -Version $version }

    $future = Set-TestVersion '0.13.0'
    $futurePresets = Update-ArcanaExportVersion -Presets $stale -Version $future
    Assert ($future.AndroidCode -eq 13000 -and $futurePresets.Contains('application/file_version="0.13.0.0"')) 'La siguiente version no se deriva de VERSION.'
    $lastPatch = Set-TestVersion '0.12.999'
    Assert ($lastPatch.AndroidCode -lt $future.AndroidCode) 'El cambio de minor reduce el codigo Android.'
    foreach ($invalid in @('', '0.12', '0.12.19-dev', '00.12.19', '0.0.0', '0.12.1000', '2100.0.1', '-1.2.3')) {
        Reject { Set-TestVersion $invalid }
    }
    foreach ($key in @('application/file_version','application/product_version','version/name','version/code')) {
        $missing = [regex]::Replace($stale, '(?m)^' + [regex]::Escape($key) + '=[^\r\n]*', '')
        Reject { Update-ArcanaExportVersion -Presets $missing -Version $version }
        Reject { Update-ArcanaExportVersion -Presets ($stale + "`n$key=duplicado") -Version $version }
    }
    Write-Host 'VERSION_SUPPORT_PS_OK: presets antiguos, VERSION futura, Windows, Android, codigo creciente, CRLF y errores reales rechazados.'
} finally { Remove-Item -LiteralPath $testRoot -Recurse -Force }
