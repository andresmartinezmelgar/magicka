# Solo esta consulta de EditorSettings al cerrar Godot .NET es un aviso conocido.
# Referencia del motor: https://github.com/godotengine/godot/issues/84839
$script:GodotSettingsMessage = 'ERROR: Condition "!EditorSettings::get_singleton() || !EditorSettings::get_singleton()->has_setting(p_setting)" is true. Returning: Variant()'

function Test-GodotBuildLog {
    param([string[]]$Lines, [int]$ExitCode)
    if ($ExitCode -ne 0) { throw "Godot termino con codigo $ExitCode." }
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        $line = $Lines[$index].Trim()
        if ($line -eq $script:GodotSettingsMessage -and $index + 1 -lt $Lines.Count -and
            $Lines[$index + 1] -match '^\s*at: _EDITOR_GET \(editor/(settings/)?editor_settings\.cpp:\d+\)\s*$') { continue }
        if ($line -match '^(ERROR:|SCRIPT ERROR:)|Failed to build project|Export failed') {
            throw "Godot registro un error: $line"
        }
    }
}

function Invoke-Tool {
    param([string]$File, [string[]]$Arguments, [switch]$CheckGodotErrors)
    # PowerShell 5.1 convierte stderr nativo en ErrorRecord, incluso con codigo 0.
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& $File @Arguments 2>&1 | ForEach-Object {
            $line = $_.ToString()
            if ($CheckGodotErrors -and $line.Trim() -eq $script:GodotSettingsMessage) {
                Write-Host ('AVISO GODOT (EditorSettings): ' + $line.Trim().Substring('ERROR: '.Length))
            } else { Write-Host $line }
            $line
        })
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    if ($CheckGodotErrors) { Test-GodotBuildLog -Lines $lines -ExitCode $code }
    elseif ($code -ne 0) { throw "$([IO.Path]::GetFileName($File)) termino con codigo $code." }
    return $lines
}
