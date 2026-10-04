# Cambios

## 0.3.0

- Logs JSONL de cliente, servidor y proceso alojado: fecha UTC, versión, sesión, conexión, sala, jugador, tick y traza de excepción.
- Captura de errores/avisos Godot y excepciones de proceso; métricas periódicas y fallos de conexión/salas/simulación.
- Exportar diagnóstico desde launcher y menú ESC, con ZIP de logs y contexto. Rotación de 2 MiB, retención de cinco archivos por componente, ocultación de contraseñas y continuidad ante fallos del disco.
- Logs de Docker persistentes en volumen; el arranque local no trunca el registro anterior.
- Corrección del cierre simultáneo con envíos y del saludo pendiente; las entradas se copian para evitar cambios por referencias compartidas.
- Entradas inválidas no consumen secuencias; snapshots con recetas independientes y validación antes del renderer.
- Respuestas HTTP/WS acotadas; IPv6 y rutas por defecto corregidas. JSON malformado devuelve 400 y formato incorrecto 415.
- Identidad única del servidor alojado, doble inicio bloqueado, arranque cancelable y aviso de salida inesperada. Configuración del servidor valida modos y acepta booleanos sin distinguir mayúsculas.
- Bots pausados en salas vacías; recetas reutilizadas y búsqueda de objetivos sin ordenar. HUD y clasificación reutilizan objetos; materiales y limpieza no asignan colecciones cada fotograma. Audio detenido y liberado al cerrar.
- 33 pruebas de núcleo, diagnóstico y red; dos pruebas de paquete Windows. CI comprueba Docker persistente y la exportación real de diagnóstico en Linux y Windows.

## 0.2.0

- Paquete Windows x64 autónomo: doble clic en `ArcanaArena.exe`, sin instalar herramientas.
- Pestaña Alojar: servidor incluido, nombre, puertos, contraseña, inicio, parada y registro.
- Configuración de reglas LAN privadas mediante botón y permiso UAC de Windows.
- Directorio, selección y creación de salas con modo, aforo, bots, fuego amigo y victoria.
- Salas independientes, contraseñas propias y limpieza después de 15 minutos vacías.
- Protocolo v2; cliente y servidor deben actualizarse juntos.
- Pruebas de aislamiento, aforo, contraseñas y paquete autónomo en Windows.
- Exportación reproducible, prueba del juego distribuido y publicación automática del ZIP solo tras validación.

## 0.1.0

Primera versión del proyecto:

- Cliente Godot .NET en C# y escenario 3D procedural con recursos propios.
- Ocho elementos, tres posiciones de selección y resolución de combinaciones.
- Rayos, chorros, electricidad, roca cargada, hielo, vapor, escudos, muros y minas.
- Curación, guardas, estados elementales, colisiones y fuego amigo configurable.
- Duelo, arena y guerra por puntos de control. Reaparición y reinicio de partida.
- Bots, práctica local, HUD, minimapa, clasificación y ayuda integrada.
- Launcher con conexión IP/dominio, búsqueda UDP LAN, contraseña y preferencia de equipo.
- Servidor ASP.NET Core autoritativo, colas acotadas y protocolo JSON versionado.
- Dockerfile multietapa, Compose normal y variante de red host para LAN en Linux.
- Documentación de ejecución, red, arquitectura y exportación.
- Pruebas de combate y conexiones reales; workflow de compilación y comprobación del contenedor.

Los recursos visuales, el mapa y los valores de balance son una primera aproximación. Esta versión no incorpora las estadísticas de equipo y tablas exactas del original ni los servicios de cuentas, matchmaking y persistencia.
