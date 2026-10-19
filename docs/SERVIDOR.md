# Servidor y conexiones

## Alojar desde Windows

Abrir el paquete con doble clic y elegir **Alojar**. Introducir nombre, TCP, UDP y contraseña; pulsar **Iniciar servidor y ver salas**. El proceso incluido es autónomo y no abre consola. **Permitir LAN** abre el permiso UAC y añade reglas de entrada únicamente para este ejecutable, los puertos elegidos y el perfil de red privado. **Ver registros** abre la carpeta de logs dentro de los datos de usuario del juego. **Diagnóstico** exporta un ZIP con los registros de cliente y servidor.

El anfitrión puede entrar en una sala mientras el servidor continúa atendiendo las demás. Cerrar el juego o pulsar **Detener servidor** detiene el proceso y pierde las salas y estadísticas en memoria. Para un servidor que permanezca activo sin el cliente, usar Docker o ejecutar el servidor dedicado aparte.

## Salas

`GET /rooms` devuelve servidor y salas sin revelar contraseñas. `POST /rooms` permite crear una sala; requiere `serverPassword` si está configurada. El saludo WebSocket selecciona `roomId` y `roomPassword`, además de la contraseña del servidor.

Hay hasta 16 salas, cada una con 2–16 plazas, incluidas las de bots. Duelo utiliza exactamente dos plazas y Defensa admite 1–4 defensores. Las salas creadas se eliminan tras 2 minutos sin personas conectadas; los bots no impiden esa limpieza. Su simulación se pausa mientras no haya personas conectadas. La sala principal permanece. El aforo, la simulación, los jugadores, los marcadores y los mensajes son independientes por sala. Las salas no persisten al reiniciar; las cuentas y recompensas requieren la configuración Firebase del servidor.

## Parámetros

| Variable | Predeterminado | Valores |
|---|---|---|
| `SERVER_NAME` | Arcana Arena | Nombre visible en LAN |
| `GAME_MODE` | warfare | `duel`, `arena`, `warfare`, `defense` |
| `ARENA_MAP` | Classic; Bastion en Defensa | Identificador del mapa compatible con el modo; el laboratorio es local |
| `GAME_PORT` | 27015 | Puerto TCP entre 1024 y 65535 |
| `DISCOVERY_PORT` | 27016 | Puerto UDP entre 1024 y 65535 |
| `MAX_PLAYERS` | 8; 4 en Defensa | 2–16; en duelo siempre 2; Defensa 1–4. Vacío utiliza el valor del modo |
| `BOTS` | 0 | Ocupan plazas; se reserva al menos una plaza para una persona |
| `FRIENDLY_FIRE` | true; false en Defensa | `false` desactiva el daño a aliados; Defensa lo mantiene desactivado |
| `SCORE_LIMIT` | 15 | Bajas necesarias en duelo/arena |
| `TICKETS` | 75 | Tickets iniciales por equipo en guerra |
| `SERVER_PASSWORD` | vacía | Contraseña opcional del saludo; máximo 128 caracteres |
| `ARCANA_LOG_LEVEL` | Information | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical` |
| `ARCANA_LOG_DIRECTORY` | Datos locales de usuario / ArcanaArena / logs | Carpeta del servidor dedicado; Docker usa `/app/logs` |

Estos ajustes configuran la **sala principal** del servidor dedicado. Las salas adicionales tienen sus propias opciones, elegidas desde el launcher. La contraseña del servidor autoriza tanto entrar como crear salas.

Los ajustes se leen al iniciar el proceso. Después de editar `.env`, recrear el contenedor:

```sh
docker compose up -d --build --force-recreate
```

## IP directa

En el launcher son válidas `192.168.1.20`, `192.168.1.20:27015`, `juego.example.com:27015`, `[::1]:27015` y `wss://juego.example.com/play`.

Una dirección sin esquema utiliza `ws://`, puerto 27015 y ruta `/play`. Una dirección `wss://` utiliza el puerto TLS de su URL, habitualmente 443. El contenedor por defecto atiende HTTP/WebSocket sin TLS; WSS requiere terminar TLS en un proxy del servidor. La contraseña viaja en el saludo y no se guarda en el launcher.

## LAN

El launcher envía `ARCANA_DISCOVER_V8` y un nonce por UDP al puerto de búsqueda. El servidor responde con nombre, IP de origen, puerto TCP, modo, ocupación y si necesita contraseña. El cliente obtiene la IP de la respuesta, no de datos declarados por un tercero.

La búsqueda dura dos segundos y también pregunta a loopback, para encontrar un servidor ejecutado en el mismo equipo. Las subredes de Wi-Fi invitado, el aislamiento entre clientes y los cortafuegos pueden impedir broadcast. En ese caso, la misma partida LAN es accesible introduciendo su IP en **Por IP**.

En Linux, `compose.lan.yaml` utiliza `network_mode: host` para recibir broadcast directamente. No declara publicación de puertos porque el proceso usa la red del host. La variante `compose.yaml` publica TCP y UDP y permite conectar por IP; en Docker Desktop el broadcast no se garantiza.

## Internet

En un VPS, abrir TCP `GAME_PORT` en el cortafuegos del servidor. En una conexión doméstica con IP pública, reenviar ese puerto TCP del router al equipo que ejecuta el servidor. Una IP privada o una red con CG-NAT necesita un acceso de red que haga alcanzable al servidor; introducir la IP privada desde otra red no basta.

El puerto UDP de descubrimiento no es necesario para jugar por IP desde Internet. No se realiza apertura automática de puertos ni se instala un servicio en el router.

## Modos

- **Defensa / Supervivencia**: `GAME_MODE=defense` selecciona Anillos (`Bastion`) por defecto. De uno a cuatro defensores, enemigos continuos y dos vidas por jugador.

- **Duelo**: dos plazas; gana el equipo que llega a `SCORE_LIMIT`.
- **Arena**: todos contra todos; gana el jugador que llega a `SCORE_LIMIT` bajas.
- **Guerra**: dos equipos. Capturar un punto requiere permanecer cerca; una presencia rival disputa la captura. La ventaja de puntos desgasta tickets rivales. Morir consume un ticket del propio equipo. Gana quien deja al rival sin tickets.

El servidor reinicia la partida nueve segundos después de terminar. Conserva a los jugadores conectados y restablece vida, puntos, estadísticas y energía.

## Diagnóstico

El servidor escribe JSONL con versión, sesión, conexiones, salas, tick y trazas de excepciones. Registra métricas de simulación cada 30 segundos. Compose conserva los archivos en el volumen `server-logs`. [Interpretación y extracción de logs](DIAGNOSTICO.md).


```sh
curl http://127.0.0.1:27015/health
curl http://127.0.0.1:27015/info
curl http://127.0.0.1:27015/rooms
docker compose logs --tail 100 server
```

Si `/health` funciona localmente pero otro PC no conecta, comprobar que el puerto TCP es accesible desde ese otro equipo. Si se puede conectar por IP pero no aparece en LAN, comprobar específicamente UDP, el puerto de búsqueda y el encaminamiento de broadcast.

Para detener: `docker compose down`. Las estadísticas de esta versión son de la partida actual y se reinician al apagar el proceso.

## Compatibilidad: protocolo 8

La versión v0.10.0 añade objetos aleatorios al Coliseo. El servidor decide sus posiciones iniciales y sus recolocaciones; los clientes solo las representan. Los snapshots transmiten objetos y espera de reaparición, mejoras acumuladas y elementos del lanzamiento activo; la recogida y sus efectos se calculan en el servidor. La sala y el saludo replican el mapa elegido. Actualizar cliente y servidor juntos; los clientes anteriores son rechazados. Para el servidor dedicado: `docker compose up -d --build`.

El mapa se elige en **Crear sala → Mapa**. Para la sala principal del servidor dedicado, `ARENA_MAP=Classic` mantiene la arena original y `ARENA_MAP=Citadel` selecciona la Ciudadela. En guerra, esta última exige 15 segundos seguidos de control total y no utiliza tickets para la victoria.

## Docker desde 0.21.0

La compilación incorpora `scripts/Apply-WindowsUpdate.ps1`, requerido por Arcana.Core. Las variantes Compose guardan logs en `server-logs` y el diario de premios en `server-data`, montado en `/app/data`; el proceso usa `/app/data/rewards` con un usuario sin privilegios. Recrear el contenedor conserva ese volumen. `docker compose down -v` elimina los datos persistentes.

La CI compila y arranca imágenes Linux x64 y ARM64, comprueba HTTP y verifica que el usuario puede conservar datos al recrear el contenedor. ARM64 se comprueba con emulación; el rendimiento de una Raspberry Pi requiere medirlo en la placa. Las credenciales y HTTPS/WSS siguen configurándose para el alojamiento elegido. No se despliega automáticamente ningún servidor.
