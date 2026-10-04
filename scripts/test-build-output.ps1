$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build-support.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("Arcana compilacion pruebas " + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
function Assert([bool]$Condition, [string]$Reason) { if (!$Condition) { throw $Reason } }
try {
    $repo = Join-Path $testRoot 'Proyecto con espacios'
    $source = Join-Path $repo 'artifacts/windows'
    New-Item -ItemType Directory -Path "$source/server", "$source/data", "$repo/Juego/carpeta-antigua" -Force | Out-Null
    foreach ($file in @('ArcanaArena.exe','ArcanaArena.pck','server/Arcana.Server.exe','data/coreclr.dll','data/Arcana.Client.dll')) {
        'primera' | Set-Content -LiteralPath (Join-Path $source $file)
    }
    'sobrante' | Set-Content -LiteralPath "$repo/Juego/carpeta-antigua/obsoleto.txt"
    Publish-GamePackage -RepositoryRoot $repo -Package $source
    Assert (!(Test-Path -LiteralPath "$repo/Juego/carpeta-antigua")) 'La carpeta anterior dejo archivos obsoletos.'
    Assert (Test-Path -LiteralPath "$repo/Juego/server/Arcana.Server.exe") 'Falta el servidor.'
    'segunda' | Set-Content -LiteralPath "$source/ArcanaArena.exe"
    Publish-GamePackage -RepositoryRoot $repo -Package $source
    Assert ((Get-Content -LiteralPath "$repo/Juego/ArcanaArena.exe" -Raw).Trim() -eq 'segunda') 'No se sustituyo la compilacion anterior.'
    $hash = (Get-FileHash -LiteralPath "$repo/Juego/ArcanaArena.exe").Hash
    Remove-Item -LiteralPath "$source/ArcanaArena.pck"
    $rejected = $false
    try { Publish-GamePackage -RepositoryRoot $repo -Package $source } catch { $rejected = $true }
    Assert $rejected 'Se publico un paquete incompleto.'
    Assert ((Get-FileHash -LiteralPath "$repo/Juego/ArcanaArena.exe").Hash -eq $hash) 'Se perdio el juego anterior al fallar la compilacion.'
    Assert ((Get-ChildItem -LiteralPath "$repo/artifacts" -Directory -Filter 'Juego-*').Count -eq 0) 'Quedaron carpetas temporales.'
    Write-Host 'BUILD_OUTPUT_OK: sustitucion completa, segunda compilacion, rutas con espacios y fallo sin perder el juego.'
} finally { Remove-Item -LiteralPath $testRoot -Recurse -Force }
