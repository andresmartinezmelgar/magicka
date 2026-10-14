param([Parameter(Mandatory=$true)][string]$Package, [Parameter(Mandatory=$true)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $Package).Path
$files = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { $_.Name -ne 'UPDATE-MANIFEST.json' } |
    Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = $_.FullName.Substring($root.Length + 1).Replace('\','/'); length = $_.Length;
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
$json = [ordered]@{ version = $Version; files = $files } | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $root 'UPDATE-MANIFEST.json'), $json, (New-Object Text.UTF8Encoding($false)))
