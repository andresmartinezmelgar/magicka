# Regenerar el paquete Windows

Para jugar, descargar el ZIP de **Releases**, extraerlo completo y abrir `ArcanaArena.exe`. Los jugadores no necesitan las herramientas de desarrollo.

Para crear de nuevo la distribución:

1. Instalar el SDK .NET 8 x64 y Godot **4.5.2 .NET**.
2. Instalar las plantillas de exportación **4.5.2 .NET** de la misma versión desde Godot.
3. Desde la raíz del repositorio, ejecutar:

```powershell
./scripts/package-windows.ps1 -GodotPath 'C:\Herramientas\Godot\Godot_v4.5.2-stable_mono_win64_console.exe'
```

El script publica `Arcana.Server.exe` para `win-x64` con su runtime, importa los recursos de Godot y exporta el cliente con el preset versionado. Crea `artifacts/windows/` con:

- `ArcanaArena.exe`: launcher y juego.
- `ArcanaArena.pck` y carpeta de datos: recursos, ensamblados y runtime del cliente.
- `server/`: ejecutable del servidor y todas sus dependencias.
- `Configurar-LAN.ps1`: utilizado por el botón del launcher, con permiso UAC.
- `LEEME.txt`, `VERSION.txt` y avisos de componentes.

Conservar todos estos archivos al distribuir. El SDK y el editor hacen falta únicamente para compilar; el runtime del cliente y el del servidor se incluyen en el ZIP. Esta versión se exporta para Windows x64; el código fuente sigue siendo multiplataforma y Docker usa Linux.

GitHub Actions realiza el proceso completo y prueba el juego exportado, la práctica, el alojamiento desde el launcher y el apagado del servidor. Si las pruebas Linux, Docker y Windows pasan, publica el ZIP y `SHA256SUMS.txt` en una Release. El artifact `ArcanaArena-Windows-x64` también queda disponible en la ejecución de Actions.
