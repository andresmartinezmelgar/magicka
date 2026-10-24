# Revisión integral de Arcana Arena · 24 de octubre de 2026

Rama: `codex/inventory-visual`. Base revisada: `6baba97c37bdd12de9eded41ba58d823841c2d8d`.

Se han revisado cliente Godot, motor, servidor, persistencia local, cuentas, reglas de Firestore, multijugador y actualizaciones. Esta revisión combina lectura de código, regresiones reproducidas antes de corregirlas, pruebas de integración locales y comprobación de capturas reales. No equivale a una garantía de ausencia de errores.

## Hallazgos corregidos

| Gravedad | Problema comprobado | Corrección |
| --- | --- | --- |
| Media | Una bonificación nula provocaba una excepción en el validador. Una caché con objetos inválidos sin equipar podía publicarse como perfil válido. | Se rechazan bonificaciones nulas y se valida el perfil completo antes de publicarlo: objetos, IDs únicos, seis referencias del equipo, esquema, identidad, contadores y bestiario. Una lectura inválida conserva el último perfil válido. |
| Media | A 640 × 450, las pestañas de Supervivencia ocupaban dos filas y la guía obligaba a desplazarse antes de mostrar vida y ataque del primer enemigo. | Las pestañas fluyen según su ancho real. La comprobación geométrica y la captura real muestran ambos valores en la vista inicial. |
| Baja | Supervivencia mostraba cuatro ranuras y asignaba un 25 % a cada una de las seis piezas; la tabla sumaba un 150 %. La descripción de familias tampoco contemplaba la generación del anillo mágico. | El resumen cuenta seis ranuras. Cada posición muestra 16,667 % dentro del equipo y 1 % por baja, con el 6 % de equipo existente. Se explica la generación del anillo mágico por separado. No se cambia el sorteo. |
| Baja | Las reglas permitían crear contadores con `doubleValue: 0` o un esquema con `doubleValue: 1`, aunque el contrato requiere enteros. | Se exige `is int` en oro, victorias, derrotas, número de objetos y versión. Se prueba con REST para conservar el tipo decimal, que el SDK JavaScript normalizaría a entero. |
| Baja | En el inventario temporal compacto se cortaba el texto del saldo y la capacidad. | El texto puede ocupar varias líneas en el panel del mago. |

Las dos regresiones nuevas del inventario fallaban sobre la base: una por excepción y otra por aceptar la caché inválida. Después de la corrección pasan. La prueba REST del contador decimal devolvía HTTP 200 antes del cambio y HTTP 403 después; la cabecera válida con enteros sigue admitida.

## Navegación e inventario

- Se mantiene la salida visible de Supervivencia y del inventario temporal: volver a tienda o continuar la partida.
- La prueba de navegación recorre las pantallas y sus diálogos, comprueba clics reales y el comportamiento de Escape/Atrás, incluido guardar, reanudar y descartar.
- Se comprueba que cerrar la espera del actualizador conserva la partida y no inicia la instalación descartada.
- El inventario conserva el desglose visible por pieza, calidad, bonificación y penalización, junto al total efectivo y sus límites.
- Los halos siguen asociados a las piezas equipadas y a su calidad y mejora. La selección y previsualización no escriben el equipo guardado.
- La restauración se prueba en un proceso nuevo. También se verifican favoritos, equipos guardados, ventas múltiples, pérdida de respuesta y recuperación sin repetir la venta.
- La consulta de la guía mantiene la pausa. Cerrar la tienda recupera combate y controles.

## Validación realizada sobre los cambios

| Área | Resultado |
| --- | --- |
| Solución .NET 8, Release; cliente Godot 4.5.2 .NET, Debug | Compilación correcta. |
| Motor y regresiones | 448 correctas, 0 fallidas. |
| Motor y servidor TCP/UDP real | 471 correctas, 0 fallidas; esta ejecución incluye las 448 anteriores. |
| Serie PvP y sala | 449 correctas, 0 fallidas; incluye la batería base. |
| Ciclo LAN independiente | Credenciales, validación, 36 creaciones y reciclado concurrente correctos. |
| Cuentas y transacciones con Firestore local | 31 correctas, 0 fallidas; incluye acceso HTTP, equipo, anillos, premios, tienda, concurrencia y diario. |
| Reglas de Firestore, emulador local | `ACCOUNT_HEADER_TYPES_OK`, `RING_RULES_OK`, `MATCH_STATISTICS_RULES_OK`, `FIRESTORE_RULES_OK`. |
| Premios pendientes con Firebase caído | `REWARD_OUTBOX_OK`: reinicio, respuesta duplicada, recibo corrupto y límite de entrega. |
| Inventario y restauración en otro proceso | `EQUIPMENT_SMOKE_OK` y `EQUIPMENT_RESTORE_SMOKE_OK`. |
| Usabilidad y controles táctiles | `USABILITY_SMOKE_OK`; incluye 24 configuraciones táctiles, modo zurdo y margen del minimapa. |
| Navegación móvil | `NAVIGATION_SMOKE_OK`, con vistas de 640 × 450, 960 × 450 y 1440 × 900. |
| Supervivencia compacta | `DEFENSE_SMOKE_OK` a 640 × 450: pausa, equipo, mejora, ventas, botín, portales y limpieza final. |
| Capturas con Xvfb y OpenGL real | Revisadas guía, tienda e inventario temporal a 640 × 450. |
| Guardado de Supervivencia | `SURVIVAL_SAVE_SMOKE_OK`: identidad, aliados, cancelación y consumo único del punto guardado. |
| Estadísticas privadas | `MATCH_STATISTICS_SMOKE_OK` a 640 × 450: permisos, paginación, escalas y desglose del daño. |
| Bestiario y reliquias | `BESTIARY_RELICS_SMOKE_OK` a 640 × 450. |
| Actualizador, escritorio y móvil | Elección previa a la descarga, identidad del canal y salida de la espera correctas. |
| Práctica y combinaciones | `PRACTICE_SMOKE_OK`; `COMBINATIONS_SMOKE_OK`, 120 combinaciones y 10 escenas. |
| Publicaciones preliminares | 7 pruebas Python correctas. |
| Publicador de parches | 6 pruebas Python correctas. |
| Reconstrucción de parches | 5 pruebas Python correctas y reconstrucción C# de una APK generada por Python, byte a byte. |
| Soporte de versiones | 8 casos Python, 2 omitidos por plataforma; sin fallos. |

No deben sumarse los totales de motor y red como si fueran pruebas distintas.

La ejecución de CI de la base también terminó correctamente: [Actions 40090701686](https://github.com/andresmartinezmelgar/magicka/actions/runs/40090701686). Incluye distribución Windows, DPAPI y recuperación del actualizador, APK Android, pruebas visuales, cuentas y Docker amd64/arm64. Esa ejecución corresponde a la base indicada, no sustituye a la CI que se lance para este cambio.

### Rendimiento medido

El escenario de prueba utiliza 4 defensores, 192 enemigos, 96 objetos temporales y 1.200 ticks:

- Paso de simulación: 2,114 ms de media.
- Preparación de un fotograma por destinatario: 2,610 ms de media.
- Fotograma transmitido: 17.447 bytes; estimación de 340,76 KiB/s a 20 Hz según el medidor existente.
- Reutilización de sectores, enemigos y botín al atravesar portales comprobada.

Son medidas de este entorno de pruebas, con otros procesos de validación en ejecución. No predicen el rendimiento de un teléfono o una Raspberry Pi concretos, ni representan la latencia total de una partida.

## Revisión de seguridad de Firestore

Se revisaron propiedad por UID, creación frente a actualización, tipos y campos permitidos, límites, equipo de otro propietario, venta y mejora, privilegios de lectura de estadísticas e inmutabilidad de informes. Los clientes no pueden concederse oro, crear objetos, alterar resultados ni otorgarse el rol de lector. Las pruebas usan proyectos `demo-` y el emulador local.

El siguiente resultado describe las reglas corregidas en esta rama:

```json
{
  "score": 4,
  "summary": "Los casos revisados de propiedad, roles, tipos e inmutabilidad pasan. Se corrigió el bypass de tipo decimal en la cabecera. Permanece el riesgo de abuso de la entrada anónima de estadísticas permitida por el diseño actual.",
  "findings": [
    {
      "check": "Validación de tipos de la cabecera de cuenta",
      "severity": "low",
      "issue": "Corregido: las comparaciones con cero o uno admitían valores double en campos definidos como enteros.",
      "recommendation": "Mantener las restricciones is int y las pruebas REST que distinguen integerValue de doubleValue. Desplegar las reglas revisadas mediante el procedimiento del proyecto."
    },
    {
      "check": "Abuso de creación anónima de estadísticas",
      "severity": "medium",
      "issue": "El diseño permite crear informes anónimos acotados e inmutables. Un cliente modificado puede inventar informes o consumir cuota creando muchos documentos; la validación de forma no acredita que la partida haya ocurrido.",
      "recommendation": "Mantener esos informes separados de premios y decisiones autoritativas. Si se necesita limitar abuso o garantizar procedencia, introducir una entrada de servidor con control de frecuencia y medidas de atestación, conservando explícitamente el requisito de juego anónimo."
    }
  ]
}
```

No se ha cambiado la política de informes anónimos ni se ha desplegado Firebase en producción. El endurecimiento de reglas está guardado y probado en el repositorio; requiere su despliegue para proteger la base real. Tampoco modifica documentos ya existentes.

## Límites de la revisión

- La revisión local no realiza un acceso Google interactivo con una cuenta real ni prueba una instalación en hardware Android, Windows o Raspberry Pi físico.
- Los resultados de Windows y Android citados arriba proceden de la CI de la base. Las comprobaciones locales de interfaz móvil usan simulación táctil y viewports reales.
- El acceso y las transacciones se prueban con verificadores controlados y Firebase local; no se han enviado escrituras a servicios de producción.
- No se modifican las fórmulas de premios, las probabilidades reales ni la compatibilidad de versiones de recompensas.

## Repetir las comprobaciones principales

Con .NET 8, Godot 4.5.2 .NET, Node y Java disponibles:

```bash
dotnet build ArcanaArena.sln -c Release -m:1
dotnet tests/Arcana.Tests/bin/Release/net8.0/Arcana.Tests.dll
bash scripts/test-network.sh
RUNNER_TEMP="$(mktemp -d)" bash scripts/test-series-lobby.sh
bash scripts/test-accounts.sh
bash scripts/test-client-smoke.sh equipment-smoke /ruta/a/godot
bash scripts/test-client-smoke.sh usability-smoke /ruta/a/godot
```

Para la guía compacta, compilar primero el cliente en Debug y ejecutar Godot con `--headless --audio-driver Dummy --path src/Arcana.Client -- --defense-smoke --mobile --quick-item-actions --smoke-viewport=640x450`. Las reglas pueden verificarse por separado con Firebase CLI `emulators:exec`, el proyecto `demo-arcana-arena`, `firebase.json` y `node tests/firebase-rules/verify.cjs`.
