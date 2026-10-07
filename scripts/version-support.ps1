# VERSION es la fuente de las versiones de ensamblados y exportaciones.
function Get-ArcanaBuildVersion {
    param([Parameter(Mandatory=$true)][string]$RepositoryRoot)
    $name = [IO.File]::ReadAllText((Join-Path $RepositoryRoot 'VERSION')).Trim()
    if ($name -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw 'VERSION debe contener tres enteros, como 0.12.19.'
    }
    $parts = @($name.Split('.') | ForEach-Object { [long]$_ })
    # Tres cifras para minor y patch; cada nueva version mantiene un codigo mayor.
    if ($parts[0] -gt 2100 -or $parts[1] -gt 999 -or $parts[2] -gt 999) {
        throw 'VERSION excede el rango del codigo Android: major <= 2100, minor y patch <= 999.'
    }
    $code = $parts[0] * 1000000 + $parts[1] * 1000 + $parts[2]
    if ($code -lt 1 -or $code -gt 2100000000) { throw 'El codigo Android debe estar entre 1 y 2100000000.' }
    return [pscustomobject]@{ Name = $name; Windows = "$name.0"; AndroidCode = $code }
}

function Update-ArcanaExportVersion {
    param([Parameter(Mandatory=$true)][string]$Presets, [Parameter(Mandatory=$true)]$Version)
    $values = [ordered]@{
        'application/file_version' = '"' + $Version.Windows + '"'
        'application/product_version' = '"' + $Version.Windows + '"'
        'version/name' = '"' + $Version.Name + '"'
        'version/code' = [string]$Version.AndroidCode
    }
    foreach ($key in $values.Keys) {
        $pattern = '(?m)^' + [regex]::Escape($key) + '=[^\r\n]*'
        if ([regex]::Matches($Presets, $pattern).Count -ne 1) { throw "El preset debe contener una sola entrada $key." }
        $Presets = [regex]::Replace($Presets, $pattern, ($key + '=' + $values[$key]))
    }
    $pathPattern = '(?m)^export_path="[^"\r\n]*\.apk"'
    if ([regex]::Matches($Presets, $pathPattern).Count -ne 1) { throw 'Falta una ruta de exportacion Android unica.' }
    return [regex]::Replace($Presets, $pathPattern,
        ('export_path="../../artifacts/android/ArcanaArena-v' + $Version.Name + '-Android-arm64.apk"'))
}

function Assert-ArcanaWindowsVersion {
    param([Parameter(Mandatory=$true)]$Metadata, [Parameter(Mandatory=$true)]$Version)
    if ($Metadata.FileVersion -ne $Version.Windows -or $Metadata.ProductVersion -ne $Version.Windows) {
        throw "Version Windows incorrecta: esperada $($Version.Windows), archivo $($Metadata.FileVersion), producto $($Metadata.ProductVersion)."
    }
}

function Assert-ArcanaAndroidVersion {
    param([Parameter(Mandatory=$true)][string]$Manifest, [Parameter(Mandatory=$true)]$Version)
    $name = [regex]::Match($Manifest, "versionName='([^']*)'").Groups[1].Value
    $code = [regex]::Match($Manifest, "versionCode='([0-9]+)'").Groups[1].Value
    if ($name -ne $Version.Name -or $code -ne [string]$Version.AndroidCode) {
        throw "Version APK incorrecta: esperada $($Version.Name) (codigo $($Version.AndroidCode)), encontrada $name (codigo $code)."
    }
}
