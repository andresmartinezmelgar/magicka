#requires -Version 5.1
$ErrorActionPreference = 'Stop'
foreach ($name in @('ArcanaArena-Dashboard-Iniciar','ArcanaArena-Dashboard-Actualizar')) {
    if (Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $name -Confirm:$false
    }
}
Write-Host 'Tareas retiradas. Se conservan el repositorio y los registros.'
