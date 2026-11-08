# Mensajes enviados por Arcana Arena

Contrato incluido a partir de la versión 0.29.53. El despliegue del servidor depende de la actualización de cada instalación. Los ejemplos son extractos JSON válidos y abreviados del contrato, no una captura de la sesión del usuario. Se omiten campos no relevantes y credenciales.

El tráfico mostrado por el HUD es el contenido WebSocket del juego. No incluye Firebase, la web, cabeceras TCP/TLS ni actualizaciones del APK. Las acciones habituales se codifican en binario (`0xB1`); los estados grandes se comprimen con Brotli (`0xA1`). Los JSON siguientes representan su contenido lógico legible.

## Una vez al principio de cada conexión

| Mensaje | Sentido | Datos | Ejemplo JSON abreviado |
|---|---|---|---|
| `hello` | Cliente → servidor | Versión de protocolo y juego, alias, equipo, clase, sala; tokens opcionales de cuenta/reanudación y claves si proceden | `{"type":"hello","protocol":46,"gameVersion":"0.29.53","name":"Mago","team":0,"wizardType":"balanced","roomId":"sala-1"}` |
| `welcome` | Servidor → cliente | Identidad del jugador, servidor y sala, estado completo inicial, preparación y token de reconexión | `{"type":"welcome","playerId":1000,"requiresReady":true,"readyPlayerIds":[],"snapshot":{"tick":0,"map":"bastion","survivalMap":{"seed":271,"version":2}}}` |
| `ready` | Cliente → servidor | Confirmación de que han terminado la carga de recursos y la preparación local | `{"type":"ready","sequence":1}` |

«Una vez» significa por conexión. Al reconectar se vuelve a negociar la sesión. Los datos incluidos en `welcome` no son todos exclusivos del principio: muchos vuelven dentro de las bases completas.

## Durante la partida

| Datos/mensaje | Sentido y frecuencia | Ejemplo JSON abreviado | Observación |
|---|---|---|---|
| Entrada del jugador | Cliente → servidor, aproximadamente 30/s; también antes de acciones | `{"type":"input","sequence":120,"moveX":1,"moveY":0,"moveTo":false,"aimX":12,"aimY":8}` | Intenciones de movimiento y apuntado; el cliente no impone su posición ni su daño. Ejes binarios cuantizados a 16 bits; coordenadas de apuntado conservan precisión float. |
| Cambios del mundo: `delta` | Servidor → cliente, aproximadamente 20/s | `{"type":"delta","baseTick":600,"baseId":3,"changes":{"snapshot":{"tick":603,"players":{"$array":{"0":{"position":{"x":12.5,"y":8.2},"energy":80}}}}}}` | Posiciones, orientación, vida, escudo, energía, estados, selección, recargas, bajas y muertes. Solo se incluyen campos que difieren de la base confirmada. |
| Enemigos de Supervivencia | Dentro de los deltas, aproximadamente 20/s | `{"id":1050,"kind":"runner","position":{"x":10,"y":4},"health":120,"maxHealth":200,"stunned":true}` | Ejemplo tras decodificar el bloque binario. Incluye ID, tipo, posición, orientación, vida/escudo y efectos; en el JSON de transporte el bloque `minions` va en base64. No se envía este objeto JSON separado por enemigo. |
| Proyectiles, rayos, áreas y barreras | Dentro del estado, aproximadamente 20/s cuando cambian | `{"fields":[{"id":300,"owner":1000,"position":{"x":10,"y":8},"radius":4,"lifetime":1.5,"kind":"meteor","active":false}]}` | También recetas elementales, trayectorias, tiempos, salud de barreras, eventos y objetos del suelo. |
| Progreso/equipo temporal | Dentro de los deltas cuando difiere de la base; bases completas periódicas | `{"defense":{"defenders":[{"playerId":1000,"level":12,"experience":320,"gold":850,"items":[]}]}}` | Incluye experiencia, oro, mejoras, reliquias, inventario y configuración propios, y bots aliados autorizados. Los datos privados de otros humanos se filtran. |
| Daño por elemento de cada mago | Dentro de los deltas cuando cambia; bases completas periódicas | `{"damage":{"health":[120,0,0,0,50,0,0,0,0],"shield":[25,0,0,0,0,0,0,0,0]}}` | Dos matrices de nueve valores: daño a vida y a escudo. El noveno representa daño sin elemento. |
| Base completa: `keyframe` | Servidor → cliente, tras 40 estados desde la última confirmación, aproximadamente 2 s | `{"type":"keyframe","baselineId":4,"snapshot":{"tick":720,"map":"bastion","survivalMap":{"seed":271,"version":2}}}` | Vuelve a incluir el estado completo filtrado: también nombres, clase, apariencia y equipo, aunque sean estables. Reintenta aproximadamente cada segundo hasta el ACK. |
| Confirmación de base | Cliente → servidor, al recibir un keyframe | `{"type":"baseline_ack","sequence":121,"baselineId":4}` | Permite cambiar de base sin depender de que hayan llegado todos los deltas previos. |
| Ping y respuesta | Ida y vuelta, como máximo una consulta pendiente y una por segundo | `{"type":"ping","sequence":122,"pingId":8}` / `{"type":"pong","pingId":8}` | Si falta respuesta, espera hasta el timeout de 5 s. No incluye inventario ni estadísticas. |
| Rendimiento | Cliente → servidor, aproximadamente cada 2 s activos | `{"type":"defense_performance","sequence":123,"framesPerSecond":58,"frameTime95":21}` | FPS y percentil 95 del tiempo de fotograma; `match_performance` en otros modos. Se detiene en pausas, menús y otras situaciones no representativas. |
| Informe detallado | Servidor → cliente, cada 15 s de simulación y al cerrar/interrumpir | `{"type":"statistics","statistics":{"schemaVersion":1,"runId":"00000000000000000000000000000001","revision":900,"seconds":15,"status":"playing","metrics":[{"group":"total","key":0,"measure":"damage","value":1200}]}}` | Métricas de ataques, elementos, enemigos, clases, curación, absorción y rendimiento. Va comprimido. El cliente pide recuperación si lleva 30 s sin informe y puede pedir el final. |

El mapa procedural se reconstruye con semilla y versión; no se envían todas las paredes como geometría del mapa. Las barreras creadas por hechizos sí forman parte del estado dinámico.

Las bases completas explican por qué ciertos campos estables no se pueden clasificar como «solo al inicio». El delta no los repite continuamente, pero el keyframe sí los vuelve a transportar. Se conservan para reconstruir el estado tras pérdidas. Quitar campos de esos keyframes exige cambiar también su reconstrucción y comprobar reconexión y pérdida de paquetes.

## En respuesta a una acción

| Acción | Cuándo | Ejemplo lógico |
|---|---|---|
| Seleccionar elemento | Al pulsar un elemento | `{"type":"element","sequence":124,"element":"fire"}` |
| Lanzar, mantener o soltar | Al interactuar con los controles | `{"type":"cast","sequence":125}` / `{"type":"release","sequence":126}` |
| Magia especial | Al lanzarla | `{"type":"magick","sequence":128,"slot":2}` |
| Inspección de un mago | Al abrir la ficha correspondiente | `{"type":"inspect","sequence":129,"inspectionRequestId":7,"inspectPlayerId":1001}` |

El doble toque sobre Meteoro busca el grupo al pulsarlo; no añade una tarea periódica ni un mensaje nuevo. Envía un `input` con el punto elegido seguido del mismo `magick` de slot 2. El servidor conserva las comprobaciones de alcance, suelo, energía, campos y recarga. El primer toque mantiene el apuntado manual; dos toques dentro de 350 ms activan el objetivo automático. También se admite doble clic en la etiqueta Meteoro del HUD de escritorio.

## Firebase y la web, por separado

| Canal | Frecuencia | Ejemplo abreviado |
|---|---|---|
| Telemetría del cliente autorizado → Firebase | Aproximadamente 15 s de tiempo real, con cierres y reintentos | `{"runId":"00000000000000000000000000000001","revision":900,"players":[{"id":1000,"name":"Mago","kills":15,"deaths":0}],"ping":{"samples":10,"sumMs":750,"minMs":60,"maxMs":100}}` |
| Navegador → API pública del dashboard | Cada 15 s solo con pestaña visible; histórico y directo separados | `{"schemaVersion":1,"start":"2026-10-10","end":"2026-11-08","partial":false,"stale":false,"reports":[],"telemetry":[]}` |
| Cuentas, tienda y recompensas | Al iniciar sesión, abrir/refrescar datos o realizar una operación; reintentos según operación | `{"itemId":"objeto-1"}` |

Los listeners Firebase del servidor son compartidos por todos los visitantes de la web. La API devuelve 304 sin JSON si el ETag no cambia. La telemetría con nombres y ping sigue dependiendo de los permisos de escritura existentes; ver la web no exige iniciar sesión. Los tokens, UID y sobres privados de Firestore no se publican en esa API.

El bestiario y las recompensas tienen sus propios guardados/reintentos; no viajan dentro de los snapshots de combate. El WebSocket además utiliza pequeñas tramas de mantenimiento, sin JSON de aplicación: 10 s en el cliente y 15 s en el servidor.

Fuentes: `Protocol.cs`, `SnapshotStream.cs`, `SnapshotDelta.cs`, `MinionDeltaCodec.cs`, `GameWorld.Snapshot.cs`, `DefenseVisibility` en `Defense.cs`, `ArenaConnection.cs`, `ArenaConnection.Latency.cs`, `GameService*.cs`, `Main.cs`, `Main.Defense.cs`, `Main.Statistics.cs`, `Main.Dashboard.cs` y `public-data.mjs`.
