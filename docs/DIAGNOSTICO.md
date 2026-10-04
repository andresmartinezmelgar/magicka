# Diagnóstico de errores

## Exportar desde el juego

En el launcher pulsa **Diagnóstico**. En una partida pulsa **ESC → Exportar diagnóstico**. El juego crea un ZIP y abre su carpeta. Incluye los archivos `client-*.jsonl`, `host-*.jsonl`, `server-*.jsonl` disponibles y `report.json`. Al comunicar un bug, describe qué estabas haciendo, la hora aproximada y adjunta ese ZIP.

Los datos se guardan en:

| Ejecución | Carpeta de logs |
|---|---|
| Cliente y servidor iniciado desde el launcher Windows | `%APPDATA%\Godot\app_userdata\Arcana Arena\logs` |
| Cliente Linux | `~/.local/share/godot/app_userdata/Arcana Arena/logs` |
| Servidor dedicado | Datos locales del usuario / `ArcanaArena/logs`, o `ARCANA_LOG_DIRECTORY` |
| Docker / Compose | `/app/logs`, volumen `server-logs` |

Los informes se guardan en la carpeta `reports` junto a `logs`. Abrir el diagnóstico no inicia un servidor. Reiniciar el servidor no trunca sus archivos anteriores.

## Leer un registro

Cada línea es un objeto JSON independiente con `time` UTC, `level`, `version`, `component`, `session`, `process`, `code`, `message`, `context` y `exception`. La sesión identifica el proceso; `connection` y `room` permiten seguir un jugador. La sala y su tick son locales; el tick de las métricas del servidor es global y continúa incluso si todas las salas están pausadas.

| Evento | Qué comprobar |
|---|---|
| `network.connect_failed`, `http.request_failed` | Dirección, operación y excepción; puertos o servidor inaccesible |
| `connection.rejected` | Versión, contraseña, sala desaparecida o aforo |
| `connection.invalid_message`, `room.invalid_json` | Tipo de error, traza y petición o conexión |
| `network.receive_failed`, `connection.transport_failed` | Corte de transporte o estado remoto inválido |
| `client.stale_snapshot` | Último tick y segundos sin nuevo estado |
| `host.start_failed`, `host.unexpected_exit` | Puerto, identidad del proceso y código de salida |
| `simulation.lag`, `server.metrics` | Retraso, duración máxima del lote, salas activas y memoria |
| `engine.error`, `process.unhandled`, `task.unobserved` | Archivo/línea del script o traza de la excepción |
| `logs.dropped` | Cola saturada; cantidad de registros descartados sin bloquear la partida |

Las métricas se registran cada 30 segundos. `ignoredCommands` agrupa intenciones rechazadas por las reglas, incluidas acciones que no se pueden ejecutar estando muerto o congelado; no implica por sí solo un bug.

## Tamaño y privacidad

Cada componente conserva cinco archivos de aproximadamente 2 MiB. Una entrada individual grande puede superar el umbral si se configura un tamaño inferior al suyo. Los nombres incluyen sesión y número de parte; los procesos no escriben en el mismo archivo. La cola está limitada a 2048 mensajes y nunca espera a escribir en disco desde el hilo de juego. Si la cola se llena, se contabilizan pérdidas; si el disco falla, el juego continúa y el informe incluye `logError`.

Las contraseñas registradas por el cliente/servidor y los campos sensibles se sustituyen por `[REDACTED]`. No se vuelcan mensajes de red completos, variables de scripts, archivos de configuración ni variables de entorno. Los logs pueden incluir nombres de salas, rutas locales y la dirección de conexión: revisa el ZIP antes de compartirlo públicamente.

## Servidor dedicado y Docker

`ARCANA_LOG_LEVEL` admite `Trace`, `Debug`, `Information` (predeterminado), `Warning`, `Error` y `Critical`. `ARCANA_LOG_DIRECTORY` elige la carpeta del servidor dedicado. Compose ya configura `/app/logs` y monta un volumen; `docker compose down` conserva ese volumen.

```sh
# Consola del servidor
docker compose logs --tail 100 server
# Copiar los archivos persistentes al equipo
container=$(docker compose ps -q server)
docker cp "$container:/app/logs" ./arcana-diagnostic-logs
```

Los logs anteriores se retiran al superar la retención. Exporta el informe después de reproducir el fallo. El ZIP del cliente incluye su servidor alojado; los logs de un servidor remoto se extraen en ese servidor.

## Comprobaciones automáticas

Las pruebas ejercitan JSON concurrente, secretos, rotación, disco no escribible, exportaciones repetidas, copias de estado y cierres de conexión. CI también ejecuta el juego con `--diagnostics-smoke`: genera un aviso controlado, exporta un ZIP y verifica que contiene ese aviso y `report.json`. Este marcador es una prueba, no un error de la partida.
