# Estadísticas de balance — 0.29.0

Duelo, Arena, Guerra y Supervivencia generan un informe acumulado por ronda, tanto en local con IA como en red. El laboratorio también usa el motor instrumentado; se distingue por su mapa. Cada reinicio empieza otro identificador. El servidor recoge las partidas en red y envía el informe a los clientes; todos utilizan el mismo identificador aleatorio y las revisiones se sustituyen, no se suman.

## Datos registrados

| Dimensión | Contenido |
|---|---|
| Partida | Versión del contrato y del juego, día UTC, modo, mapa, local/red, duración activa, máximos de humanos/bots, dificultad, marcador, equipo ganador y estado de cierre |
| Combate | Daño efectivo a vida, daño absorbido por escudos, exceso de daño, impactos, lanzamientos, bajas, muertes, daño propio y fuego amigo |
| Elementos y ataques | Aportación de cada elemento, combinación y forma de ataque; humanos, bots y monstruos separados; quemadura, auras, meteoritos, minas, pasivas y bombardeos |
| Monstruos | Apariciones, vida inicial acumulada, muertes, daño causado/recibido, escudos absorbidos y desglose del daño recibido por elemento |
| Apoyo y estados | Curación efectiva, escudo concedido/reparado, reanimaciones y segundos vivo, mojado, ardiendo o congelado por clase y equipo |
| Progresión | Apariciones y vida inicial por dificultad, oro ganado/gastado, subidas de nivel, objetos recogidos por calidad y reliquias por tipo |
| Objetivos y movimiento | Capturas y distancia recorrida por clase y equipo |
| Rendimiento | Muestras de población enemiga, coste de simulación, FPS y p95 de fotograma comunicados; ventanas por debajo de 30 FPS |

El daño se mide como la diferencia real de vida/escudo después de aplicar el impacto. Un impacto letal no suma vida que ya no tenía el objetivo. Las mezclas de `Hit` reparten el daño según sus componentes después de resistencias; el prisma solo recibe la parte vulnerable. Los poderes de daño fijo reparten su aportación entre sus elementos declarados. El índice 8 representa daño físico. Curación y escudo cuentan únicamente el aumento efectivo. Los muros destructibles se contabilizan aparte como daño de escudo cuando se golpean, sin muertes de monstruo.

Los usos de un haz se cuentan al iniciarlo; sus impactos se acumulan durante la canalización. Las auras continuas no cuentan un lanzamiento cada fotograma. Las medias de FPS/p95 agregan muestras de clientes y no identifican hardware. El informe tiene como máximo 2048 métricas y 240 KiB; `droppedBuckets` permite detectar partidas que agotaron el detalle disponible. Se reserva espacio para los totales.

Las clases y equipos son grupos agregados: no representan jugadores identificables. Una baja de monstruo suicida figura como muerte de ese tipo; no concede una baja al jugador. Los informes son telemetría para balance y no autorizan premios ni cambios de inventario. Los datos locales enviados por un cliente no son una prueba autoritativa de sus acciones.

## Archivos y sincronización

El cliente usa `user://match-statistics/matches/<runId>.json` y `user://match-statistics/pending/<runId>.json`. `user://` es la carpeta de datos del juego que utiliza Godot en cada dispositivo. El servidor usa `ARCANA_MATCH_STATISTICS_DIR`; por defecto es `match-statistics` junto a la carpeta `logs`. En Docker se guarda en `/app/data/match-statistics`, dentro del volumen persistente existente.

Se captura cada 15 segundos activos aproximadamente, al terminar, al guardar supervivencia y al salir. La cola se escribe con renombrado atómico y vaciado a disco antes de actualizar el historial. El envío se hace fuera de la simulación. Al arrancar se recuperan las escrituras y los informes que seguían abiertos se marcan como interrumpidos. Los archivos inválidos se conservan con extensión `.invalid-…` y no bloquean los demás.

El cliente descubre el relé oficial a través del anuncio público de Firestore y envía `POST /analytics/matches` por HTTPS, sin autenticación, cookies ni redirecciones. El relé confirma con 202 después de guardar su propia cola en disco. El cliente conserva su historial y retira solo la revisión confirmada de su cola. El servidor reintenta la escritura Firebase mediante el SDK Admin o su servicio de cuentas, conservando el informe hasta recibir confirmación. Si la red, el relé o Firebase están caídos, la cola continúa pendiente. No basta con que Firebase esté disponible si el relé oficial sigue apagado.

Una transacción en `matchStatistics/<runId>` conserva la revisión más reciente y nunca sustituye un resultado final por uno parcial. Dos clientes de la misma sala y el servidor no generan tres partidas en el visor. No se guarda un identificador de transporte junto al informe.

Guardar supervivencia conserva el acumulado dentro del archivo local. Continuar normalmente mantiene el identificador; recuperar un guardado anterior a un informe ya archivado o continuar tras cambiar de versión abre un tramo nuevo, con contadores nuevos y `continuation: true`. Así no se sobrescribe el desenlace anterior ni se mezclan dos versiones de balance.

Las verificaciones visuales, perfiles de rendimiento y modos de prueba desactivan el archivo/envío de telemetría de producción.

## Anonimato del contenido

El contrato acepta exclusivamente campos cerrados, enumeraciones, números, una versión, un día UTC y un GUID aleatorio por partida. Rechaza campos extra, también dentro de las métricas. No incluye nombre, correo, UID Firebase, ID de jugador, ID de sala/servidor, ID de dispositivo, token, IP ni hora exacta de inicio. Tampoco deriva el GUID de una cuenta ni reutiliza los identificadores de recompensas o bestiario.

Esto describe el contenido de los informes. Los proveedores de red y alojamiento pueden procesar metadatos de conexión, como en cualquier petición HTTPS; el juego no los añade a las estadísticas. Los registros operativos de autenticación y diagnóstico existentes son independientes de esta colección.

## Visor y autorización

En Perfil aparece **Estadísticas de partidas** únicamente después de comprobar el permiso de la cuenta Google actual. El visor ofrece resumen, elementos, monstruos, hechizos/combinaciones, progresión y clases/equipos, con filtros de modo, versión, periodo y partidas interrumpidas. Carga páginas de 10 hasta 1000 informes y muestra explícitamente cuántos ha cargado; sus filtros y cifras se refieren a esa muestra cargada, no a un agregado completo de toda la base.

Las reglas nuevas son un prototipo restrictivo con pruebas de emulador: únicamente una cuenta Google con `statisticsReaders/<uid>.enabled == true` puede consultar la colección. Nadie puede escribir informes ni concederse ese permiso desde el cliente. Un usuario puede leer únicamente su propio documento de permiso; no puede enumerar las cuentas autorizadas. Los perfiles e inventarios conservan sus permisos anteriores.

Para autorizar exactamente dos cuentas existentes, con credenciales Admin del proyecto:

```sh
FIREBASE_PROJECT_ID=arcana-arena-f2997 dotnet src/Arcana.Server/bin/Release/net8.0/Arcana.Server.dll --statistics-readers 'CORREO_GOOGLE_1' 'CORREO_GOOGLE_2'
```

También admite `uid:UID`. Comprueba que ambas cuentas sean Google, estén activas y sean distintas; revoca los demás documentos de `statisticsReaders` y concede ambas autorizaciones en un único lote. Los correos no se incorporan al informe ni al documento de permiso.

Publicar las reglas y la exclusión de índices del cuerpo JSON en la base existente `(default)`:

```sh
npx firebase-tools deploy --only firestore:rules,firestore:indexes --project arcana-arena-f2997
```

Antes de desplegar, comprobar la edición y configuración de la base existente con `firestore:databases:list` y `firestore:databases:get '(default)'`; esta función no crea otra base. Las consultas usan la API de documentos existente y ordenan por `day`; `payload` no necesita indexación.

Los dos correos/UID reales y una sesión administrativa no estaban disponibles en el entorno de implementación. La activación de permisos y el despliegue real se deben completar con esos datos; el visor falla cerrado hasta entonces.

## Verificación reproducible

`MatchStatisticsTests` comprueba anonimato, daño efectivo y resistencias, escudos, curación, rondas, guardados, reenvíos, interrupciones y corrupción. Las pruebas de red ejercitan la entrega del informe y el relé real. `MatchStatisticsEmulatorTests` prueba transacciones concurrentes y revisiones antiguas. `tests/firebase-rules/verify.cjs` comprueba ambas cuentas, consultas, revocación, denegación anónima, escalación y escrituras rechazadas. `--statistics-smoke` verifica seis vistas, filtros, permisos y límites horizontales en escritorio y móvil; CI conserva sus capturas.
