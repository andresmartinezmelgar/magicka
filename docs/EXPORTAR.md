# Regenerar el paquete Windows

Para jugar, descargar el ZIP de **Releases**, extraerlo completo y abrir `ArcanaArena.exe`. Los jugadores no necesitan las herramientas de desarrollo.

## Compilar con doble clic

1. Descarga el código de GitHub con **Code → Download ZIP** y extrae todo, o actualiza tu copia del repositorio.
2. Cierra el juego y abre **`Compilar-Juego.cmd`** en la raíz.
3. Al terminar, abre **`Juego/ArcanaArena.exe`**.

La primera ejecución descarga el SDK .NET **8.0.425 x64**, Godot **4.5.2 .NET** y sus plantillas oficiales. Instala las herramientas en `.tools/` y las plantillas en `%APPDATA%/Godot/export_templates/4.5.2.stable.mono/`, sin permisos de administrador. Requiere Internet la primera vez; las herramientas se reutilizan. Fuentes: [instalador oficial .NET](https://learn.microsoft.com/es-es/dotnet/core/tools/dotnet-install-script) y [Godot 4.5.2](https://github.com/godotengine/godot/releases/tag/4.5.2-stable).

Cada compilación correcta **sustituye completamente `Juego/`**, incluidos archivos y carpetas sobrantes. Primero se genera y valida el paquete en `artifacts/windows/`; después se publica la nueva carpeta. Si falla la compilación, permanece el juego anterior. No guardar archivos personales en `Juego/`. Los registros quedan en `artifacts/compilacion-*.log` y la ventana permanece abierta para leer el resultado.

La versión se toma de `VERSION`. Los ensamblados C# y los metadatos del ejecutable Windows se generan a partir de ese archivo. El script sincroniza los presets antes de exportar y restaura su contenido original al terminar, también si falla. La comprobación final compara la versión real del ejecutable y muestra la esperada y la encontrada.

## Compilación manual

Para crear la distribución con herramientas ya instaladas:

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

## Exportar Android

La APK ARM64 se genera con `scripts/package-android.sh` y se comprueba en la integración continua. [Herramientas, controles y requisitos](ANDROID.md).
