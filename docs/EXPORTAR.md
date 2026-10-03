# Crear un ejecutable del cliente

La primera versión contiene el proyecto fuente completo. Abrir y ejecutar el proyecto en Godot no requiere exportar un `.exe`.

Para distribuir el juego en Windows:

1. Abrir `src/Arcana.Client/project.godot` con **Godot 4.5.2 .NET**.
2. Instalar las plantillas de exportación **.NET de esa misma versión** desde el gestor de plantillas de Godot.
3. Compilar el proyecto.
4. Abrir **Proyecto → Exportar** y añadir **Windows Desktop**, arquitectura x86_64.
5. Exportar a una carpeta de distribución. En C#, conservar y entregar todos los archivos que produzca Godot, incluidos los ensamblados y dependencias; no copiar únicamente el `.exe`.
6. Abrir el ejecutable distribuido y elegir IP, LAN o práctica en su launcher.

Godot incorpora las partes de .NET necesarias para ejecutar los juegos exportados. El SDK .NET es necesario en el equipo de desarrollo; no equivale a exigir que cada jugador instale el SDK. Esta versión se prepara para escritorio, no para exportación web en C#.

El servidor se distribuye aparte. Puede publicarse para el runtime .NET del equipo de destino:

```powershell
dotnet publish src/Arcana.Server -c Release -o artifacts/server
dotnet artifacts/server/Arcana.Server.dll
```

O ejecutarse directamente con Docker. El ejecutable del cliente no incorpora un servidor dedicado ni instala Docker.
