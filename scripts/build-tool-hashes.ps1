function Assert-ArcanaBuildTool {
    param([Parameter(Mandatory=$true)][string]$Path, [Parameter(Mandatory=$true)][string]$Name)
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'build-tool-hashes.json') -Raw | ConvertFrom-Json
    $entry = $manifest.PSObject.Properties[$Name]
    if (!$entry) { throw "No hay un hash aprobado para $Name." }
    $algorithm = 'SHA256'; $digest = $entry.Value
    if ($digest -isnot [string]) { $algorithm = $entry.Value.algorithm; $digest = $entry.Value.digest }
    if ((Get-FileHash -LiteralPath $Path -Algorithm $algorithm).Hash -ne $digest) {
        throw "La herramienta $Name no supera la comprobación de integridad."
    }
}

function Install-ArcanaDotnetTools {
    param([Parameter(Mandatory=$true)][string]$Root, [Parameter(Mandatory=$true)][string]$Cache)
    $metadata = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dotnet-tool-downloads.json') -Raw | ConvertFrom-Json
    foreach ($channel in @('10.0')) {
        $sdk = $metadata.PSObject.Properties[$channel].Value
        if (Test-Path -LiteralPath (Join-Path $Root "sdk/$($sdk.version)")) { continue }
        $archive = Join-Path $Cache $sdk.name
        Download-Tool $sdk.url $archive
        Assert-ArcanaBuildTool -Path $archive -Name $sdk.name
        Expand-Archive -LiteralPath $archive -DestinationPath $Root -Force
    }
}
