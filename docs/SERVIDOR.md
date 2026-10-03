# Servidor y conexiones

## Parámetros

| Variable | Predeterminado | Valores |
|---|---|---|
| `SERVER_NAME` | Arcana Arena | Nombre visible en LAN |
| `GAME_MODE` | warfare | `duel`, `arena`, `warfare` |
| `GAME_PORT` | 27015 | Puerto TCP entre 1024 y 65535 |
| `DISCOVERY_PORT` | 27016 | Puerto UDP entre 1024 y 65535 |
| `MAX_PLAYERS` | 8 | 2–16; en duelo siempre 2 |
| `BOTS` | 0 | Ocupan plazas; se reserva al menos una plaza para una persona |
| `FRIENDLY_FIRE` | true | `false` desactiva el daño a aliados |
| `SCORE_LIMIT` | 15 | Bajas necesarias en duelo/arena |
| `TICKETS` | 75 | Tickets iniciales por equipo en guerra |
| `SERVER_PASSWORD` | vacía | Contraseña opcional del saludo |

Los ajustes se leen al iniciar el proceso. Después de editar `.env`, recrear el contenedor:

```sh
docker compose up -d --build --force-recreate
```

## IP directa

En el launcher son válidas `192.168.1.20`, `192.168.1.20:27015`, `juego.example.com:27015`, `[::1]:27015` y `wss://juego.example.com/play`.

Una dirección sin esquema utiliza `ws://`, puerto 27015 y ruta `/play`. Una dirección `wss://` utiliza el puerto TLS de su URL, habitualmente 443. El contenedor por defecto atiende HTTP/WebSocket sin TLS; WSS requiere terminar TLS en un proxy del servidor. La contraseña viaja en el saludo y no se guarda en el launcher.

## LAN

El launcher envía `ARCANA_DISCOVER_V1` y un nonce por UDP al puerto de búsqueda. El servidor responde con nombre, IP de origen, puerto TCP, modo, ocupación y si necesita contraseña. El cliente obtiene la IP de la respuesta, no de datos declarados por un tercero.

La búsqueda dura dos segundos y también pregunta a loopback, para encontrar un servidor ejecutado en el mismo equipo. Las subredes de Wi-Fi invitado, el aislamiento entre clientes y los cortafuegos pueden impedir broadcast. En ese caso, la misma partida LAN es accesible introduciendo su IP en **Por IP**.

En Linux, `compose.lan.yaml` utiliza `network_mode: host` para recibir broadcast directamente. No declara publicación de puertos porque el proceso usa la red del host. La variante `compose.yaml` publica TCP y UDP y permite conectar por IP; en Docker Desktop el broadcast no se garantiza.

## Internet

En un VPS, abrir TCP `GAME_PORT` en el cortafuegos del servidor. En una conexión doméstica con IP pública, reenviar ese puerto TCP del router al equipo que ejecuta el servidor. Una IP privada o una red con CG-NAT necesita un acceso de red que haga alcanzable al servidor; introducir la IP privada desde otra red no basta.

El puerto UDP de descubrimiento no es necesario para jugar por IP desde Internet. No se realiza apertura automática de puertos ni se instala un servicio en el router.

## Modos

- **Duelo**: dos plazas; gana el equipo que llega a `SCORE_LIMIT`.
- **Arena**: todos contra todos; gana el jugador que llega a `SCORE_LIMIT` bajas.
- **Guerra**: dos equipos. Capturar un punto requiere permanecer cerca; una presencia rival disputa la captura. La ventaja de puntos desgasta tickets rivales. Morir consume un ticket del propio equipo. Gana quien deja al rival sin tickets.

El servidor reinicia la partida nueve segundos después de terminar. Conserva a los jugadores conectados y restablece vida, puntos, estadísticas y energía.

## Diagnóstico

```sh
curl http://127.0.0.1:27015/health
curl http://127.0.0.1:27015/info
docker compose logs --tail 100 server
```

Si `/health` funciona localmente pero otro PC no conecta, comprobar que el puerto TCP es accesible desde ese otro equipo. Si se puede conectar por IP pero no aparece en LAN, comprobar específicamente UDP, el puerto de búsqueda y el encaminamiento de broadcast.

Para detener: `docker compose down`. Las estadísticas de esta versión son de la partida actual y se reinician al apagar el proceso.
