# Arcana Arena v0.11.1

Revisión del código completo con correcciones de combate, conexión, validación de datos y simplificación de la interfaz.

- Las cargas se cancelan correctamente al morir, congelarse, quedarse sin entrada o cambiar de hechizo.
- Hielo crea todos sus fragmentos con un único sonido y efecto de lanzamiento.
- Los empujes y proyectiles colisionan de forma continua. Los muros dejan de dañar al desaparecer; reaparecer o reanimar evita dejar al mago dentro de un muro.
- Un rayo combinado conserva la bonificación de daño de cada autor al golpear un escudo.
- Se pueden cancelar las conexiones y consultas de salas. Una respuesta tardía no sustituye la sesión actual ni devuelve al jugador a una partida abandonada.
- Salas, servidores y snapshots se validan antes de llegar a la interfaz.
- F1 evita reconstrucciones innecesarias. Las magias comparten nombres, costes y tiempos entre HUD y combate; se eliminan campos, parámetros y cálculos sin uso.
- Los metadatos del ejecutable Windows, Docker, Compose y el LEEME muestran la versión correcta.

Validación: 99 pruebas de reglas, HTTP, diagnóstico y red; Godot, servidor Docker y paquete Windows autónomo. Se comprueban también las cuatro escenas y los efectos de 1, 2 y 3 cargas.

Protocolo 8, compatible con v0.10.0 y v0.11.0. Para disponer de todas las correcciones, actualiza cliente y servidor.

Descarga el ZIP Windows x64, extrae todos sus archivos y abre `ArcanaArena.exe`. Incluye cliente, servidor y runtimes. `Compilar-Juego.cmd` reconstruye y sustituye la carpeta `Juego/`.

[Detalle de la revisión](AUDITORIA-v0.11.1.md).
