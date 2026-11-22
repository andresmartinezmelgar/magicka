# Refactorización de simulación, red y representación

Revisión sobre `cea7ba0805f0f5b53d35620652573923bd054154`. El objetivo de esta pasada es eliminar trabajo y colecciones temporales repetidos en las rutas de partida. No hay una medición de FPS, latencia o capacidad de usuarios de esta revisión.

## Cambios

| Ruta | Trabajo eliminado o reutilizado | Condición de corrección |
| --- | --- | --- |
| Difusión PvP | Preparación JSON compartida por estado de sala; antes se hacía para cada destinatario | Cada conexión conserva su emisor, base confirmada, ACK y keyframe. La preparación se mantiene inmutable durante su difusión |
| Supervivencia online | Usa la misma interfaz de preparación, después de filtrar por jugador | Nunca se comparte una vista privada entre jugadores |
| Recepción de deltas | Árbol JSON y diccionario base de enemigos preparados por base, con un máximo de dos bases conservadas | Cada fusión recibe una copia privada; se admiten tanto deltas JSON completos como el códec de enemigos |
| Códec de enemigos | Reutilización del diccionario al decodificar varios deltas | Los arrays de elementos de la base, la entrada y los resultados quedan aislados; los arrays vacíos se comparten de forma segura |
| Simulación de Supervivencia | Lista reutilizable de bajas y un recorrido directo para comprobar pausa y fin de partida | Se mantiene el orden de retirada, el retardo de prismas, los efectos pendientes y las vidas |
| Planificación de bots | Listas reutilizables para bots, humanos y amenazas, y conjuntos para destinos ya cubiertos y curación | Se conserva la cadencia, orden por ID, prioridades, distancias y desempates. Las listas liberan sus referencias al terminar cada planificación |
| Representación de Supervivencia | Reutilización de los arrays visibles cuando no cambia la selección; uso del array original cuando está entero en vista | Se recalculan las pertenencias al mover la vista. Un snapshot nuevo siempre actualiza tick y tiempo, aunque coincidan las entidades |
| Limpieza visual | Listas de retirada reutilizables para botín y auras; no se repite la consulta geométrica de enemigos visibles | Se conservan los límites de visibilidad, la propiedad del botín y las condiciones de retirada de auras |

La preparación compartida reduce de N a una las conversiones del estado público PvP a árbol JSON por envío a N destinatarios. Es una propiedad del flujo de código, no una medición de aceleración. Los bytes del protocolo y las cadencias se conservan; este cambio no reduce por sí mismo la cantidad de información transmitida.

Las cachés de recepción retienen hasta dos bases con sus representaciones preparadas. Se cambia memoria temporal y trabajo repetido por memoria retenida acotada. Las vistas filtradas comparten únicamente arrays de snapshots tratados como inmutables, nunca buffers que se sobrescriban después.

## Validación

- Revisión del diff y análisis sintáctico de los diez archivos C# modificados, sin compilar.
- Preparadas tres regresiones de red: comparación byte a byte entre preparación individual y compartida para tres destinatarios con ACK y keyframes perdidos; aislamiento de resultados y cambio entre códecs; protección de los elementos de la base frente a modificaciones de entrada o salida.
- Preparada una regresión del cliente, integrada en la comprobación existente de magos enemigos: cambio de zona para jugadores, proyectiles, paredes, áreas y haces; conservación de vistas anteriores; actualización del reloj; reutilización de la vista completa.
- Las regresiones nuevas y existentes, las mediciones de rendimiento y la revisión gráfica siguen pendientes de la próxima compilación autorizada. `AGENTS.md` reserva las compilaciones y publicaciones para una petición expresa.

## Comprobaciones de la próxima compilación

1. Compilar `ArcanaArena.sln` en Release y ejecutar `tests/Arcana.Tests`, incluyendo las tres regresiones nuevas y las existentes de horda, separación espacial, continuidad, red y reconexión.
2. Ejecutar las simulaciones visuales existentes de Supervivencia en escritorio y móvil. Comprobar `SURVIVAL_VISIBILITY_CACHE_OK`, auras, botín, enemigos y el movimiento al cruzar sectores.
3. Comparar `--profile-protocol` y `--profile-survival` entre la base y el candidato con el mismo equipo, configuración, semilla y carga. Medir asignaciones, CPU y p95, además del resultado visual y la exactitud de los estados.
4. Medir varias salas PvP y Supervivencia simultáneas antes de afirmar capacidad de usuarios o dimensionar el nuevo VPS. Las cifras de los planes de alojamiento no demuestran capacidad del juego.

No se han modificado anuncios, pagos, precios, reglas de Firebase, balance, frecuencia de simulación, versión de protocolo ni metadatos de publicación. Esta revisión no crea APK, tags ni releases.
