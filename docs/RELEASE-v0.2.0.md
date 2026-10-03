Primera distribución Windows x64 de Arcana Arena: combate elemental PvP en C#, Godot .NET y servidor dedicado Docker.

**Descarga `ArcanaArena-v0.2.0-Windows-x64.zip`, extrae todo y abre `ArcanaArena.exe` con doble clic.** El paquete incluye motor, cliente, servidor y runtimes; no requiere instalar Godot, .NET ni Docker.

Desde el launcher: práctica con bots, conexión por IP, búsqueda LAN y alojamiento. En Alojar se configuran nombre, puertos y contraseña, y se inicia el servidor incluido. La pantalla de salas permite crear partidas con modo, aforo, bots, fuego amigo, límites y contraseña propia, o entrar en una existente.

El botón Permitir LAN añade las reglas correspondientes en redes privadas con permiso UAC. El servidor permanece activo mientras el juego está abierto y se cierra con él. Para alojamiento permanente, el repositorio incluye Docker y configuración de red host para LAN Linux.

Cliente y servidor utilizan protocolo v2 y deben actualizarse juntos. Salas y estadísticas son temporales. Recursos, mapa y balance propios: es una primera recreación jugable, con gráficos y balance aproximados.

La publicación requiere compilación, pruebas de reglas/red/salas, comprobaciones Docker y ejecución del paquete exportado en Windows. `SHA256SUMS.txt` contiene la suma del ZIP.
