#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
foreach ($file in Get-ChildItem (Join-Path $repository 'dashboard\windows') -Filter *.ps1) {
    $tokens = $null; $parseErrors = $null
    [Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count -gt 0) { throw ($file.Name + ': ' + ($parseErrors.Message -join '; ')) }
}
$probe = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
$probe.Start(); $port = $probe.LocalEndpoint.Port; $probe.Stop()
$script = Join-Path $repository 'dashboard\windows\Start-Dashboard.ps1'
$stdout = Join-Path $env:TEMP ('arcana-dashboard-' + [Guid]::NewGuid().ToString('N') + '.log')
$stderr = $stdout + '.error'
$arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $script + '" -Repository "' + $repository + '" -Port ' + $port
$arguments += ' -Verbose'
$process = Start-Process powershell.exe -ArgumentList $arguments -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
try {
    $base = 'http://127.0.0.1:' + $port
    $response = $null; $lastRequestError = ''
    for ($i=0; $i -lt 8; $i++) {
        if ($process.HasExited) { throw ('Servidor finalizado: ' + (Get-Content $stderr -Raw)) }
        try { $response = Invoke-WebRequest ($base + '/') -UseBasicParsing -TimeoutSec 2; break } catch { $lastRequestError = $_.Exception.Message; Start-Sleep -Milliseconds 250 }
    }
    if (-not $response -or $response.StatusCode -ne 200 -or $response.Content -notmatch 'Observatorio' -or $response.Headers['X-Content-Type-Options'] -ne 'nosniff') { throw ('El dashboard no sirve su HTML de forma correcta. Ultimo error: ' + $lastRequestError) }
    $localName = Invoke-WebRequest ('http://localhost:' + $port + '/') -UseBasicParsing -TimeoutSec 10
    if ($localName.StatusCode -ne 200) { throw 'No responde localhost.' }
    $head = Invoke-WebRequest ($base + '/model.mjs') -UseBasicParsing -Method Head -TimeoutSec 5
    if ($head.StatusCode -ne 200 -or $head.Headers['Content-Type'] -notmatch 'javascript') { throw 'HEAD o MIME de módulos inválido.' }
    $version = Invoke-RestMethod ($base + '/version.json') -TimeoutSec 5
    $expected = (& git -C $repository rev-parse HEAD) -join ''
    if ($version.version -ne $expected.Trim()) { throw 'La versión del servidor no coincide con el commit.' }
    foreach ($path in @('/missing.json','/.git/config','/..%2F..%2FVERSION')) {
        try { Invoke-WebRequest ($base + $path) -UseBasicParsing -TimeoutSec 5 | Out-Null; throw ('Se expone una ruta inválida: ' + $path) }
        catch [Net.WebException] { if ([int]$_.Exception.Response.StatusCode -notin @(403,404)) { throw } }
    }
    Write-Host 'DASHBOARD_WINDOWS_OK: PowerShell 5.1, HTML, MIME, HEAD, revision y rutas protegidas.'
} catch {
    Get-Content -LiteralPath $stdout,$stderr -ErrorAction SilentlyContinue | Write-Host
    throw
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Remove-Item -LiteralPath $stdout,$stderr -Force -ErrorAction SilentlyContinue
}
