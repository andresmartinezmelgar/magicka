# Dashboard público, tráfico y avisos de objetos

Estado: cambios de código preparados; sin compilación, APK, tag ni despliegue en la Raspberry en esta revisión.

## Qué indica la captura

El HUD marca 944,1 KiB enviados y 27.376,1 KiB recibidos; la partida dura 07:09. Si el contador de conexión abarca esos mismos 429 segundos, son aproximadamente **63,8 KiB/s de recepción media**, equivalentes a 224 MiB por hora (235 MB decimales). Los 34,1 KiB/s visibles son una muestra reciente, no la media de toda la sesión.

`ArenaConnection` cuenta bytes del contenido WebSocket, después de comprimir en el emisor. No incluye cabeceras TCP/TLS, las peticiones REST a Firebase ni las consultas de otra página web. Por tanto, la captura corresponde al canal del juego. Sin una traza no se puede atribuir un porcentaje exacto a cada tipo de mensaje.

## Auditoría del código

| Flujo | Comportamiento anterior | Decisión |
|---|---|---|
| Estado del juego | 20 snapshots/s; deltas independientes y enemigos binarios; Brotli rápido | Mantener cadencia, precisión y recuperación; usar Brotli calidad 3 y un buffer reutilizable |
| Estadísticas de la partida | Informe JSON completo cada 15 s desde servidor, además de petición del cliente cada 15 s | Comprimir el informe y eliminar la petición redundante; recuperar tras 30 s sin informe y al cerrar |
| Bases confirmadas | Keyframe completo cada 40 estados, reintento hasta confirmar | Conservar: permite recuperar pérdidas sin encadenar deltas |
| Inventario temporal | Solo inventario propio y bots aliados autorizados; cambios por delta | Mantener restricciones y funcionalidad de equipo |
| Mapa procedural | Se replica semilla y versión, no toda la geometría | Ya es compacto |
| Dashboard | SDK de Firebase y dos suscripciones por navegador autenticado | Dos listeners compartidos en servidor; GET públicos saneados, sin SDK ni cuenta en el navegador |
| Histórico web | Mezclado con la actividad | Respuestas separadas: histórico/cierres y directo; ETag estable evita reenviar el mes con cada señal |

La recuperación sigue utilizando el protocolo 46: el prefijo Brotli `0xA1`, JSON, bases y ACK no cambian. Los datos de combate se conservan. El nuevo identificador opcional del destinatario de un aviso permite mostrar botín solo a quien lo recoge.

## Estudio reproducible de compresión

Comando ejecutado, sin compilar el juego:

```bash
python3 scripts/study-network-compression.py --output docs/benchmarks/network-2026-11-08.json
```

La prueba fabrica 120 deltas por escenario con los anchos de campos de enemigos del códec actual y los envuelve en JSON/base64. Usa Brotli nativo, ventana 22 y mediana de tres pasadas. **Son datos sintéticos en Linux x86_64; no son una grabación del móvil, una ejecución de .NET ni una medición de Raspberry.**

| Enemigos | Calidad 1, bytes | Calidad 3, bytes | Ahorro |
|---|---:|---:|---:|
| 56 | 169.522 | 160.896 | 5,09 % |
| 89 | 247.932 | 235.110 | 5,17 % |
| 192 | 493.141 | 467.296 | 5,24 % |

Calidad 3 tarda aproximadamente 0,04–0,12 ms por trama en este entorno. Calidad 4 ofrece tamaño similar o algo peor y tarda más; se elige calidad 3. Los tiempos exactos dependen del equipo y quedan registrados en el JSON.

Un informe sintético con 900 métricas pasa de 65.174 a 8.232 bytes, **87,4 % menos en ese informe**. Además, se evita solicitar una segunda copia periódica. Estas cifras no representan el ahorro del tráfico total: la simulación continúa enviando posiciones, hechizos y estado de enemigos.

## Lectura pública de estadísticas

- `GET /api/dashboard/history`: informes y actuaciones finalizadas del último mes UTC.
- `GET /api/dashboard/live`: señales activas recientes; caducan a los 90 s.
- Se admiten las mismas rutas bajo `/analytics/dashboard` para un proxy existente.
- Se mantienen cerradas las reglas de Firestore. Solo se leen `matchStatistics` y `dashboardTelemetry` mediante Firebase Admin.
- Se deserializan contratos validados y se publica una lista cerrada de campos. No se devuelven documentos Firestore, IDs de documento, `ownerUid`, perfiles, inventarios ni credenciales. Un alias con formato de correo se sustituye por «Mago».
- Dos listeners compartidos por proceso, ventana de 30 días y máximos de 500 documentos históricos y 250 de telemetría, con un documento adicional para detectar el límite. Histórico acotado a unos 4 MiB y directo a 1 MiB, con presupuestos separados; los recortes y registros inválidos se indican como cobertura parcial.
- JSON y gzip se conservan en memoria. Si el ETag no cambia, la respuesta es 304 sin cuerpo. El histórico no cambia por cada heartbeat activo.
- El navegador consulta cada 15 s, sin credenciales; detiene consultas al ocultarse y cancela respuestas de una fuente anterior.
- Ante fallos se mantiene la última copia válida, identificada como antigua. El estado inicial devuelve 503 y permite reintentar.

Los datos de ping y actuaciones siguen procediendo del canal de telemetría de clientes autorizados que ya existía. Quitar el login de la web no concede permisos de escritura ni inventa partidas o muestras de ping. Si no hay telemetría disponible, esas vistas aparecen vacías.

## Oportunidades pendientes de medir con el juego real

1. Desglosar bytes de deltas, keyframes e informes en una partida equivalente de siete minutos. No extrapolar el 5 % sintético a todo el consumo del teléfono.
2. Evaluar enviar el desglose de daño de jugadores a menor frecuencia que sus posiciones, conservando el informe final exacto. El cambio requiere comprobar la actualización de tablas y paneles.
3. Medir cuánto ocupan las bases completas con inventarios grandes y ajustar su representación sin perder recuperación.
4. Evaluar interés por distancia para enemigos fuera de pantalla. Requiere mantener el total de enemigos y las señales de jefes; no basta con borrar enemigos del snapshot.
5. Medir framing binario frente a base64 después de Brotli. El crecimiento de base64 sin comprimir no equivale a ahorro real en la red.

## Verificación y límites

Ejecutadas: 17 pruebas JavaScript del modelo y transporte público; comprobación sintáctica de módulos; estudio sintético de compresión; revisión de diferencias y sintaxis C# sin compilar.

Añadidas para la siguiente compilación autorizada: regresiones de compresión y estadísticas, prueba HTTP con emulador de Firestore para anonimato/ETag/gzip, y comprobación del aviso lateral y su caducidad en el smoke móvil.

Pendientes: compilación y pruebas C#/Godot, medición ARM64, partida real y despliegue en la Raspberry. Las instrucciones del repositorio reservan compilaciones y publicaciones para una petición expresa.

Referencia del listener compartido: [API oficial de Google.Cloud.Firestore Query](https://docs.cloud.google.com/dotnet/docs/reference/Google.Cloud.Firestore/4.5.0/Google.Cloud.Firestore.Query).
