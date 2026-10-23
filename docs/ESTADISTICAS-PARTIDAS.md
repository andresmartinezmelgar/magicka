# Estadísticas de balance — 0.29.12

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

## Almacenamiento y envío al cierre

Desde 0.29.9 las estadísticas tienen como almacenamiento persistente exclusivamente Firestore, en la base Standard existente `(default)` de `arcana-arena-f2997` (europe-southwest1). Android y Windows envían directamente por HTTPS, sin relé ni servidor de cuentas. El contenido del envío no incluye ID token, cookies ni UID y funciona también sin iniciar sesión con Google.

Durante el combate los contadores se acumulan en memoria. Al terminar, abandonar, reiniciar o desconectar una partida se captura el informe cerrado y comienza el primer envío inmediatamente. Guardar supervivencia sin salir solo crea el punto de continuación del juego; no publica estadísticas de una partida en curso. Guardar y salir cierra el tramo como abandonado. El servidor transmite sus capturas por la conexión de juego y solo encola informes cerrados para la ruta de compatibilidad.

`MatchStatisticsOutbox` mantiene los pendientes exclusivamente en memoria. Un fallo de red, cuota o permisos permite reintentar cada 30 segundos mientras la aplicación permanece abierta. La confirmación borra solo la revisión enviada, nunca una revisión nueva que haya llegado durante el envío. Al cerrar o perder el proceso se pierden los pendientes sin confirmar; no hay recuperación desde disco. No se crean `match-statistics/matches` ni `match-statistics/pending`, y se elimina `ARCANA_MATCH_STATISTICS_DIR`. Los archivos históricos de versiones anteriores no se leen, modifican ni suben automáticamente.

Los clientes crean documentos inmutables en `matchStatistics/<runId>-<revision>-<status>`, con diez campos: `schemaVersion`, `runId`, `revision`, `status`, `day`, `mode`, `map`, `gameVersion`, `complete` y el JSON `payload`. El ID determinista confirma los reintentos con `ALREADY_EXISTS`. Una revisión tardía tiene otro documento y no sobrescribe el final. Los documentos antiguos del servidor en `matchStatistics/<runId>` siguen siendo legibles. Al agregar, agrupar siempre por `runId` y conservar la revisión más reciente según `MatchStatisticsReport.IsNewer`, que prioriza el resultado completo.

Las reglas publicadas admiten la creación anónima de estas estadísticas con cabecera, tipos, ID y tamaño acotados. Solo permiten repetir un documento idéntico, sin cambios ni borrados. El JSON de un informe remoto se valida al leerlo; los documentos corruptos o todavía en curso se omiten sin bloquear las páginas siguientes. Las cifras son telemetría no autoritativa para balance y no conceden premios ni modifican perfiles o inventario.

Desde 0.29.10 el punto local de supervivencia conserva solo el estado necesario para continuar la partida y omite el informe `Statistics` y sus métricas. Los puntos antiguos con ese campo siguen abriendo, pero su telemetría no se restaura ni se vuelve a guardar. Al activar una continuación se abre siempre un tramo con identificador nuevo, contadores nuevos y `continuation: true`. Así un punto anterior a una partida ya terminada no vuelve a contar el mismo acumulado y no hace falta consultar un historial local para detectar rebobinados. Los tramos miden lo jugado efectivamente en cada sesión. El visor agrupa las revisiones de cada identificador antes de calcular cifras.

Las verificaciones visuales, perfiles de rendimiento y modos de prueba desactivan el envío de producción. El visor de QA recibe páginas simuladas en memoria; no escribe informes ni consulta Firestore real.

## Anonimato del contenido

El contrato acepta exclusivamente campos cerrados, enumeraciones, números, una versión, un día UTC y un GUID aleatorio por partida. Rechaza campos extra, también dentro de las métricas. No incluye nombre, correo, UID Firebase, ID de jugador, ID de sala/servidor, ID de dispositivo, token, IP ni hora exacta de inicio. Tampoco deriva el GUID de una cuenta ni reutiliza los identificadores de recompensas o bestiario.

Esto describe el contenido de los informes. Los proveedores de red y alojamiento pueden procesar metadatos de conexión, como en cualquier petición HTTPS; el juego no los añade a las estadísticas. Los registros operativos de autenticación y diagnóstico existentes son independientes de esta colección.

## Visor y autorización

Desde 0.29.2, en Perfil aparece **Estadísticas de partidas** para cualquiera de los dos UID indicados por el propietario y definidos en `Main.Statistics.cs`. También funciona con ese perfil guardado sin conexión. La comprobación es local e inmediata. Al cambiar de cuenta o cerrar sesión se cierra el visor.

Desde 0.29.9 el visor consulta directamente Firestore con la sesión Google del usuario y comprueba su permiso en `statisticsReaders/<uid>.enabled`. Las dos cuentas indicadas conservan el botón en Perfil; para obtener los informes globales necesitan una sesión válida y el permiso remoto habilitado. La lectura funciona sin servidor de cuentas ni partidas.

Se consultan páginas de diez documentos, hasta diez páginas por acción. **Cargar más partidas** amplía la muestra hasta un máximo de 1000 documentos; **Actualizar desde Firebase** reinicia la consulta. Se omiten informes inválidos y se agrupan revisiones por partida antes de sumar. El texto de la pantalla indica si quedan páginas y si se alcanzó el límite. Los filtros de modo, versión, periodo, origen y partidas interrumpidas se aplican a la muestra cargada. Si falla la conexión se conserva la muestra ya consultada en la pantalla, exclusivamente en memoria; al cambiar de cuenta o cerrar sesión se cierra el visor.

Desde 0.29.5, Elementos muestra una gráfica de barras verticales con los ocho elementos en X y puntos acumulados en Y. Vida representa curación efectiva registrada, sin sobrecuración; Escudo representa absorción real, sin contar capacidad concedida o reparada que nunca recibe daño. Los demás elementos suman daño a vida y a escudos, repartido por la contribución real de las mezclas. El daño físico queda en la tabla, fuera de los ocho elementos. La curación incluye las fuentes de soporte registradas, como las habilidades pasivas; no se atribuye toda ella a un hechizo específico de Vida.

Desde 0.29.6, Monstruos muestra dos gráficas verticales separadas: **Daño realizado por enemigo** (rojo) y **Daño recibido por enemigo** (azul). Cada gráfica tiene su propia escala desde cero, de modo que una cantidad de daño recibido mucho mayor no aplana las barras de daño realizado. La dirección se refiere al monstruo: realizado es el que inflige; recibido, el que le infligen. Ambas suman el daño real a vida y escudos, mantienen las mismas especies en el mismo orden, incluidas las de valor cero, y permiten desplazamiento horizontal en móvil. Las alturas solo se comparan dentro de cada gráfica, usando los valores de su propio eje Y.

Desde 0.29.12, cada daño registra también su elemento y su procedencia, separados para humanos, bots, monstruos y entorno: **ataque directo**, **área de hechizo** (barreras, novas, minas, meteoritos y bombardeos), **aura pasiva** (fuego o rayo) y **quemadura**. No se duplican los totales: se usa exactamente el mismo reparto efectivo por elemento, vida y escudo. En Elementos, el selector de desglose empieza en Fuego y permite consultar cualquier elemento ofensivo o daño físico. Las cifras históricas sin esta dimensión se muestran como **Sin desglose**, nunca como daño directo supuesto. El envío sigue siendo directo a Firestore al cerrar la partida, con el mismo contrato exterior y las reglas existentes.

La escala se puede elegir en **Automática**, **Lineal** o **Logarítmica**. Automática usa una transformación logarítmica cuando el mayor valor positivo es al menos veinte veces el menor; los ceros permanecen en cero. Los ejes muestran valores reales mediante la transformación inversa. En barras de vida/escudo se transforma la longitud total y se conserva dentro de ella la proporción de ambos componentes. El tamaño logarítmico facilita leer valores pequeños; las proporciones reales se consultan en sus cifras y porcentajes. La curva de dificultad también respeta el selector. El tiempo congelado conserva una escala lineal de porcentajes del tiempo vivo.

Bajo las barras verticales se muestran los valores numéricos sin abreviar y el porcentaje sobre la suma de esa gráfica. Las barras horizontales incluyen sus cifras y porcentaje sobre el total de las filas mostradas; por tanto, un top diez usa como denominador ese top diez. Los porcentajes muy pequeños se indican como «<0,01 %» junto a su cantidad real. Vida y Escudo siguen representando curación y absorción, respectivamente: el porcentaje de Elementos describe la actividad representada, no exclusivamente el daño.

Humanos y bots se agrupan por defecto en un único origen. Elementos y Hechizos permiten además filtrar humanos, bots, monstruos o todos los orígenes. El resumen mantiene monstruos y entorno separados y Clases suma humanos y bots por clase y equipo antes de calcular el porcentaje de tiempo vivo congelado. La agrupación es de consulta: no reescribe los JSON, no duplica el grupo total y conserva la compatibilidad con los informes existentes.

Las seis vistas conservan las tablas numéricas. La amenaza media de cada especie divide daño causado entre apariciones; no es DPS. La curva de dificultad divide vida inicial total entre apariciones de cada etapa y combina las especies presentes; no representa el escalado aislado de una especie. Las gráficas usan únicamente los informes cargados y filtrados, sin nuevas solicitudes ni identidades.

Las reglas permiten leer la colección únicamente a cuentas Google con `statisticsReaders/<uid>.enabled == true`. El cliente no puede concederse este permiso ni enumerar las cuentas autorizadas. El administrador habilita los dos UID indicados en esos documentos de permiso. Los UID y correos no se incorporan a los informes anónimos.

Publicar las reglas y la exclusión de índices del cuerpo JSON en la base existente:

```sh
npx firebase-tools deploy --only firestore:rules,firestore:indexes --project arcana-arena-f2997
```

Esta función no crea otra base ni requiere habilitar Firebase Anonymous Authentication. Las consultas usan la API de documentos y ordenan por `day`; `payload` no necesita indexación.

## Verificación reproducible

`MatchStatisticsTests` comprueba anonimato, daño efectivo y resistencias, escudos, curación, rondas, guardados, reintentos en memoria, revisiones concurrentes y documentos corruptos. Las pruebas de red ejercitan la entrega del informe y el relé real. `MatchStatisticsEmulatorTests` prueba transacciones concurrentes y revisiones antiguas. `tests/firebase-rules/verify.cjs` comprueba ambas cuentas, consultas, revocación, denegación anónima, escalación y escrituras rechazadas. `--statistics-smoke` verifica los dos UID exactos y su botón sin sesión de red, otras cuentas, cierre de sesión, consulta paginada simulada sin archivos ni red de producción, seis vistas con gráficas, barras verticales, daño por especie en ambas direcciones con dos gráficas y escalas independientes incluso con valores muy desproporcionados, curación/absorción efectivas, agrupación humano+bot, categorías a cero, filtros numéricos de modo/origen, medias ponderadas y límites horizontales en escritorio y móvil; CI conserva sus capturas.
