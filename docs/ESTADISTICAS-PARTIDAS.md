# Estadísticas de balance — 0.29.2

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

Desde 0.29.2, en Perfil aparece **Estadísticas de partidas** para cualquiera de los dos UID indicados por el propietario y definidos en `Main.Statistics.cs`. También funciona con ese perfil guardado sin conexión. La comprobación es local e inmediata. Al cambiar de cuenta o cerrar sesión se cierra el visor.

El visor lee hasta 1000 informes JSON de `user://match-statistics/matches` fuera del hilo de interfaz y permite volver a leerlos con **Actualizar archivos locales**. Ofrece resumen, elementos, monstruos, hechizos/combinaciones, progresión y clases/equipos, con filtros de modo, versión, periodo y partidas interrumpidas. Sus cifras corresponden únicamente a los archivos de ese dispositivo. Si no hay informes, muestra el estado vacío. Mostrar el botón y abrir las gráficas no consulta roles ni informes de Firestore.

Desde 0.29.1, las seis vistas incluyen gráficas nativas adaptadas a escritorio y móvil, además de las tablas numéricas. El resumen compara daño de humanos, bots, monstruos y entorno. Elementos y Hechizos aplican el filtro de origen; las demás vistas comparan todos los orígenes. Las barras separan vida y escudos sobre una escala común que parte de cero. La amenaza de cada especie divide daño causado entre apariciones; no es DPS. La curva de dificultad divide vida inicial total entre apariciones de cada etapa y combina las especies presentes; no representa el escalado aislado de una especie. Clases compara daño y porcentaje de tiempo vivo congelado. Las gráficas usan únicamente los informes cargados y filtrados, sin nuevas solicitudes ni identidades.

La lectura remota preparada en 0.29.0 queda separada del visor local actual. Esta versión no despliega reglas ni concede permisos en Firebase. Para una futura activación remota, las reglas incluyen pruebas de emulador: únicamente una cuenta Google con `statisticsReaders/<uid>.enabled == true` puede consultar la colección. Nadie puede escribir informes ni concederse ese permiso desde el cliente. Un usuario puede leer únicamente su propio documento de permiso; no puede enumerar las cuentas autorizadas. Los perfiles e inventarios conservan sus permisos anteriores.

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

El visor local actual no requiere la activación remota anterior. Los informes y las colas de sincronización mantienen su formato; los UID del botón no se incorporan al contenido anónimo de las partidas.

## Verificación reproducible

`MatchStatisticsTests` comprueba anonimato, daño efectivo y resistencias, escudos, curación, rondas, guardados, reenvíos, interrupciones y corrupción. Las pruebas de red ejercitan la entrega del informe y el relé real. `MatchStatisticsEmulatorTests` prueba transacciones concurrentes y revisiones antiguas. `tests/firebase-rules/verify.cjs` comprueba ambas cuentas, consultas, revocación, denegación anónima, escalación y escrituras rechazadas. `--statistics-smoke` verifica los dos UID exactos y su botón sin sesión de red, otras cuentas, cierre de sesión, carga desde archivos locales, seis vistas con gráficas, filtros numéricos de modo/origen, medias ponderadas y límites horizontales en escritorio y móvil; CI conserva sus capturas.
