# Solo lo llama el boton del launcher. Windows solicita elevacion mediante UAC.
param(
    [Parameter(Mandatory=$true)][string]$ServerPath,
    [ValidateRange(1024,65535)][int]$GamePort = 27015,
    [ValidateRange(1024,65535)][int]$DiscoveryPort = 27016
)
$ErrorActionPreference = 'Stop'
try {
    $server = (Resolve-Path -LiteralPath $ServerPath).Path
    if ([System.IO.Path]::GetFileName($server) -ne 'Arcana.Server.exe') { throw 'Servidor inesperado.' }
    # Reemplaza solo las reglas de Arcana de esta carpeta. No desactiva el cortafuegos.
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $id = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($server.ToUpperInvariant()))).Replace('-','').Substring(0,24) }
    finally { $hash.Dispose() }
    $tcpName = "Arcana-$id-TCP"
    $udpName = "Arcana-$id-UDP"
    Get-NetFirewallRule -Name $tcpName,$udpName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -Name $tcpName -DisplayName 'Arcana Arena - juego TCP' -Direction Inbound -Action Allow -Profile Private -Program $server -Protocol TCP -LocalPort $GamePort | Out-Null
    New-NetFirewallRule -Name $udpName -DisplayName 'Arcana Arena - descubrimiento LAN UDP' -Direction Inbound -Action Allow -Profile Private -Program $server -Protocol UDP -LocalPort $DiscoveryPort | Out-Null
    exit 0
} catch {
    Write-Error $_
    exit 1
}
