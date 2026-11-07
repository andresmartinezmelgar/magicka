#requires -Version 5.1
[CmdletBinding()]
param([string]$Repository = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent), [ValidateRange(1024,65535)][int]$Port = 8787, [switch]$OpenBrowser)
$ErrorActionPreference = 'Stop'
$Repository = [IO.Path]::GetFullPath($Repository)
$root = [IO.Path]::GetFullPath((Join-Path $Repository 'dashboard\web'))
if (-not (Test-Path -LiteralPath (Join-Path $root 'index.html'))) { throw 'No se encuentra dashboard\web\index.html.' }
$prefix = $root.TrimEnd('\') + '\'
$url = 'http://localhost:' + $Port + '/'
# Loopback TCP avoids an administrator-only HTTP.sys URL reservation on Windows 10.
$listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $Port)
try { $listener.Start() } catch { throw ('No se puede abrir el puerto ' + $Port + '. Comprueba si el dashboard ya esta iniciado.') }
if ($OpenBrowser) { Start-Process $url }
Write-Host ('Dashboard: ' + $url + ' (Ctrl+C para cerrar)')
$mime = @{ '.html'='text/html; charset=utf-8'; '.css'='text/css; charset=utf-8'; '.mjs'='application/javascript; charset=utf-8'; '.js'='application/javascript; charset=utf-8'; '.json'='application/json; charset=utf-8'; '.svg'='image/svg+xml'; '.png'='image/png' }
try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            Write-Verbose 'Conexion HTTP local recibida.'
            $client.ReceiveTimeout = 2500; $client.SendTimeout = 5000
            $stream = $client.GetStream()
            # Strict byte limit before parsing; do not let a local request allocate an unbounded line.
            $headerBytes = New-Object 'Collections.Generic.List[byte]'
            while ($headerBytes.Count -lt 8192) {
                $b = $stream.ReadByte(); if ($b -lt 0) { break }; $headerBytes.Add([byte]$b)
                $n = $headerBytes.Count
                if ($n -ge 4 -and $headerBytes[$n-4] -eq 13 -and $headerBytes[$n-3] -eq 10 -and $headerBytes[$n-2] -eq 13 -and $headerBytes[$n-1] -eq 10) { break }
            }
            $headers = [Text.Encoding]::ASCII.GetString($headerBytes.ToArray())
            $requestLine = ($headers -split "`r`n")[0] -split ' '
            $code = 200; $reason = 'OK'; $contentType = 'text/plain; charset=utf-8'; $body = [Text.Encoding]::UTF8.GetBytes('Bad request')
            if ($headerBytes.Count -ge 8192 -or $requestLine.Count -ne 3 -or $requestLine[2] -notmatch '^HTTP/1\.[01]$') { $code=400; $reason='Bad Request' }
            elseif ($requestLine[0] -notin @('GET','HEAD')) { $code=405; $reason='Method Not Allowed' }
            elseif ($headers -notmatch ('(?im)^Host: (localhost|127\.0\.0\.1):' + $Port + '\r?$')) { $code=403; $reason='Forbidden' }
            else {
                $relative = [Uri]::UnescapeDataString(($requestLine[1] -split '\?')[0])
                if (-not $relative.StartsWith('/') -or $relative.Contains([char]0)) { $code=400; $reason='Bad Request' }
                elseif ($relative -eq '/version.json') {
                    $commit = (& git.exe -C $Repository rev-parse HEAD 2>$null) -join ''
                    $body = [Text.Encoding]::UTF8.GetBytes((@{version=$commit.Trim()} | ConvertTo-Json -Compress)); $contentType='application/json; charset=utf-8'
                } else {
                    if ($relative -eq '/') { $relative='/index.html' }
                    $childPath = $relative.TrimStart('/').Replace('/','\')
                    $path = [IO.Path]::GetFullPath((Join-Path -Path $root -ChildPath $childPath))
                    $extension = [IO.Path]::GetExtension($path).ToLowerInvariant()
                    if (-not $path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or -not $mime.ContainsKey($extension)) { $code=403; $reason='Forbidden' }
                    elseif (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $code=404; $reason='Not Found' }
                    else { $body=[IO.File]::ReadAllBytes($path); $contentType=$mime[$extension] }
                }
            }
            if ($code -ne 200) { $body=[Text.Encoding]::UTF8.GetBytes($reason) }
            $response = "HTTP/1.1 $code $reason`r`nContent-Type: $contentType`r`nContent-Length: $($body.Length)`r`nConnection: close`r`nCache-Control: no-store`r`nX-Content-Type-Options: nosniff`r`nReferrer-Policy: no-referrer`r`nCross-Origin-Opener-Policy: same-origin-allow-popups`r`n`r`n"
            $head = [Text.Encoding]::ASCII.GetBytes($response)
            $stream.Write($head,0,$head.Length)
            if ($requestLine[0] -ne 'HEAD') { $stream.Write($body,0,$body.Length) }
            $stream.Flush()
        } catch { Write-Verbose $_.Exception.Message } finally { $client.Dispose() }
    }
} finally { $listener.Stop() }
