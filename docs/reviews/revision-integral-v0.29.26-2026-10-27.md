# Revisión de Arcana Arena 0.29.26 · 27 de octubre de 2026

Base revisada: `81817e84627e6373006cfd9a7bf50a07bb34d452`. Al iniciar la revisión, `main`, el tag `v0.29.26` y la última Release pública apuntan a este commit. Se revisaron los 40 archivos modificados desde la publicación anterior y los recorridos relacionados del motor, cliente, transporte, servidor, inventario, estadísticas, persistencia y actualizador.

Hay **seis hallazgos confirmados**: uno de prioridad alta, cuatro de prioridad media y uno de prioridad baja. Las reproducciones adicionales usan las clases reales de los binarios locales de `0.29.26`, sin recompilar. La batería existente pasa, pero no cubre estas interacciones. El informe no aplica correcciones al juego.

## Correcciones posteriores para 0.29.27

Los seis hallazgos se han corregido: el transporte conserva la regla de venta, la asistencia mantiene el apuntado y evita campos huérfanos, la conversión por capacidad tiene avisos propios, el total de regeneración incluye el escudo activo y las vistas de reliquias comparten acumulados enumerados con la regla de muerte correspondiente al modo.

La validación local de las correcciones termina con **506 pruebas correctas y 0 fallidas**, incluidas siete regresiones nuevas. También pasan `ELEMENT_TEXT_SMOKE_OK`, `BESTIARY_RELICS_SMOKE_OK` y `DEFENSE_SMOKE_OK` a 640 × 450 en modo móvil. Los apartados siguientes conservan el diagnóstico y las evidencias originales de 0.29.26.

## Hallazgos, por prioridad

| ID | Prioridad | Problema | Consecuencia |
| --- | --- | --- | --- |
| R1 | Alta | El transporte pierde `AutoSellStyle`. | Activar o desactivar la venta automática de Supervivencia falla en red. |
| R2 | Media | La asistencia deja de considerar peligrosos los suelos cuyo autor ya murió. | El personaje puede permanecer sobre un área que sigue dañándolo. |
| R3 | Media | Los paquetes de entrada del cliente cambian la dirección de un ataque automático ya iniciado. | El haz arcano de la asistencia se dirige hacia el cursor del inventario. |
| R4 | Media | La conversión por inventario lleno contradice la confirmación de «Mantener» y anuncia un objeto que no se conserva. | Con 24/24 objetos se sigue convirtiendo botín en oro sin explicar el motivo. |
| R5 | Media | El total de regeneración omite la curación del escudo de Vida. | El desglose puede mostrar 38 vida/s mientras el total omite la regeneración. |
| R6 | Baja | La ventana de reliquias del menú conserva una descripción de muerte que no corresponde a Supervivencia. | Vitalidad dice que desaparece al morir, aunque se conserva. Esa vista tampoco usa el desglose enumerado y acumulado. |

### R1. La regla de venta automática no llega al servidor

En [ArenaConnection.Send, líneas 156–162][r1], el cliente crea una copia del comando antes de introducirlo en la cola de salida. Copia `ItemId`, `Slot` y `Loadout`, pero omite `AutoSellStyle`. Este campo sí existe en el contrato del protocolo y es obligatorio para que [DefenseCommand][r1-authority] acepte tanto la activación como la desactivación.

La interfaz construye correctamente el comando. La pérdida se produce después, en el transporte. El servidor lo rechaza y no envía un resultado específico de la operación; el inventario acaba mostrando el agotamiento del plazo de confirmación de seis segundos. En local se llama directamente a `GameWorld.ApplyMessage`, por lo que este defecto no aparece.

Reproducción sobre las clases reales: generar y entregar una pieza válida, construir el comando con su estilo, llamar a `ArenaConnection.Send`, recoger la copia de su cola y serializarla. El original contiene el estilo; la copia y el JSON no. La autoridad rechaza la copia y acepta el original para el mismo jugador y objeto.

**Corrección propuesta:** conservar `AutoSellStyle` al copiar el mensaje y añadir una regresión que atraviese `ArenaConnection.Send`, la serialización y la autoridad. La prueba actual de reglas de inventario no pasa por ese transporte.

### R2. La asistencia ignora un suelo dañino después de morir su autor

[AssistanceHazard][r2] exige que el propietario del campo siga en `_combatants`. [CleanupDefense][r2-cleanup] retira de esa colección a los enemigos muertos. Sin embargo, los suelos de fuego y frío que dejaron sus proyectiles conservan su duración y [TickWallsAndFields][r2-damage] sigue aplicando sus efectos.

Reproducción: colocar un suelo de fuego activo bajo el jugador, matar al conjurador y realizar la limpieza de enemigos. La puntuación de peligro cambia de `1` a `0`, aunque al campo le quedan aproximadamente `3,98 s`. El siguiente paso del campo todavía causa daño: `0,38666` puntos en el escenario aislado. Si no hay otra amenaza próxima, esa área ya no activa la retirada.

**Corrección propuesta:** conservar la procedencia necesaria para evaluar el campo durante toda su vida, o evaluar los campos huérfanos con las mismas reglas que usa el daño. Mantener el suelo tras la muerte de su creador es compatible con el combate actual; el problema es que la asistencia lo considera seguro.

### R3. El apuntado de la interfaz sustituye al de la asistencia

La asistencia [fija el objetivo al iniciar la ráfaga][r3], pero durante su recarga retorna antes de actualizar el apuntado. Mientras el inventario está abierto, el cliente sigue enviando paquetes `input`; [ApplyMessage][r3-input] los admite y sustituye `player.Input`, incluidas las coordenadas de apuntado. Después, `UpdateFacing` usa estas coordenadas para resolver el canal que permanece activo.

Reproducción con dos humanos: jugador en `(-8, 0)`, enemigo en `(7, 0)` y aliado en `(-8, 5)`. Con el escudo levantado, la asistencia inicia su ataque mirando hacia `(1, 0)`. Al llegar un paquete normal de la interfaz apuntando hacia el aliado, el siguiente paso cambia la dirección a `(0, 1)` y el haz termina en `(-8, 4,4)`, junto al aliado, en lugar de mantener el objetivo enemigo.

Es un **haz arcano de la asistencia**. No es un fallo del filtro nuevo de objetivos del rayo eléctrico. En Supervivencia el fuego amigo continúa bloqueado, por lo que esta reproducción no daña al aliado; sí invalida la elección de objetivo y la comprobación previa de línea libre.

**Corrección propuesta:** separar el apuntado manual del de la asistencia, o mantener y validar el objetivo asistido en cada paso de canalización. Los paquetes recibidos mientras la interfaz retira el control deben conservar su secuencia sin recuperar el mando del ataque automático.

### R4. «Mantener» no explica la conversión con 24/24 objetos

La confirmación de [SetAutoSell][r4-ui] afirma que se conservará el botín nuevo al desactivar la regla. Sin embargo, [GrantDefenseItem][r4-core] convierte las piezas nuevas en oro cuando se alcanzan 24 objetos, incluso si no hay ninguna regla activa. Después emite el mismo mensaje de «objeto temporal» que utiliza cuando sí guarda una pieza.

Reproducción: llenar los 24 huecos con cero reglas de venta y recoger otra pieza. El inventario sigue en 24, la pieza no se conserva y se reciben `60` monedas, su valor de venta. `AutoSold` permanece en `0`. El aviso emitido es `Inventario: objeto temporal Platinum`, que no indica ni la conversión ni su motivo.

Esta reproducción es compatible con el comportamiento de la captura que mostraba 24/24 objetos. Es un segundo motivo, independiente de R1, por el que puede parecer que desactivar la venta no funciona. La conversión por falta de espacio ya forma parte de las reglas del juego; desactivar un filtro no aumenta la capacidad.

**Corrección propuesta:** explicar la conversión por inventario lleno en la confirmación y en la ayuda de la regla, y emitir un aviso específico con el oro recibido. Usar el nombre traducido de la calidad. Conservar el límite y la economía existentes salvo que se decida cambiar ese comportamiento.

### R5. El total de regeneración omite el escudo de Vida

El estado del mago [publica la regeneración efectiva del escudo][r5-source], incluidos los modificadores de equipo y reliquias. La nueva tabla de procedencia utiliza ese dato correctamente. Sin embargo, [DefenseState][r5-total] calcula `DefensePlayerSnapshot.Regeneration` sumando solo la regeneración de reliquias y el aura propia. [La tabla de totales][r5-ui] utiliza este segundo dato para «Regeneración propia».

Reproducción sin reliquias, auras ni equipo: lanzar Escudo + Vida con la salud incompleta. El desglose recibe `38 vida/s`; el total recibe `0`. Durante un segundo de simulación se recuperan aproximadamente `38,001` puntos, diferencia de redondeo incluida. La fila del total desaparece porque su valor es cero.

**Corrección propuesta:** incluir en el total la regeneración efectiva del escudo mientras siga activo, sin volver a aplicar sus modificadores. Mantener diferenciados el origen, la curación continua y los pulsos instantáneos. Probar creación, rotura, sustitución y expiración del escudo.

### R6. El menú de reliquias mantiene textos antiguos

[OpenRelics][r6] utiliza `RelicBuffs.Description` y bloques sin numeración. La descripción general de Vitalidad dice «La capacidad extra desaparece al morir». Esto corresponde a otros modos: en Supervivencia las reliquias sobreviven a la primera muerte. La guía de Supervivencia ya tiene una excepción correcta y la pestaña de reliquias de la tienda usa los totales enumerados, de modo que las vistas discrepan.

Reproducción: recoger una acumulación de Vitalidad, morir una vez y reaparecer. Se conserva la acumulación y la vida máxima sigue siendo `1100`; la descripción que muestra el menú continúa afirmando que la capacidad desaparece.

**Corrección propuesta:** compartir el renderizado de reliquias entre las vistas, utilizar el acumulado real y adaptar las reglas de muerte al modo. Aplicar también la enumeración solicitada en esta ventana.

## Comprobaciones y cobertura

| Área | Evidencia | Resultado |
| --- | --- | --- |
| Motor y regresiones existentes | Ejecución local del ensamblado disponible de `0.29.26`. | **499 correctas, 0 fallidas**. |
| Casos adicionales de esta revisión | Cola y serialización reales del cliente, autoridad del motor y escenarios aislados de simulación mediante reflexión. | Las seis reproducciones anteriores se confirman. No son seis fallos detectados por la batería existente. |
| Rayo eléctrico | Regresiones con fuego amigo activado/desactivado, aliados próximos, estados y cadenas adicionales. | Excluye aliados antes de elegir objetivos. La mezcla con Vida conserva la curación de aliados. |
| Arcano y penetración | Revisión de `TraceBeam` y regresiones de combate existentes. | No busca otro enemigo para rebotar; la penetración conserva la dirección. Continúan la interacción entre haces y las reflexiones de escudos. |
| Pausa y asistencia | Regresiones con un humano y bots, dos humanos y cambios del grupo. | Un humano pausa al consultar pantallas; varios humanos mantienen la simulación. La asistencia no suma tiempo de participación. R2 y R3 quedan fuera de esas pruebas. |
| Inventario | Reglas locales, favoritos, protección de equipados, cambios de calidad y recuperación de ventas. | Las pruebas existentes pasan. R1 afecta al transporte y R4 a la comunicación de la capacidad. |
| Texto elemental | Pruebas de contexto, nombres de efectos, combinaciones, plurales y escape de BBCode. | Sin fallos en los casos cubiertos. Los colores base de los textos usan la misma paleta que los controles de elementos. |
| Interfaz publicada | Lectura de la CI y revisión de capturas de combinaciones, inventario y suma de bonificaciones en la superficie compacta. | Tablas, numeración de procedencias y acciones del inventario legibles. La CI también valida los nombres completos de habilidades móviles. |
| Versiones | `test-version-support.py`. | 6 casos correctos; 2 omitidos por plataforma. |
| Publicaciones de pruebas | `test-preview-release.py`. | 7 correctas. |
| Publicador de parches | `test-delta-publisher.py`. | 6 correctas. |
| Reconstrucción de parches | `test-update-deltas.py`. | 5 correctas. |
| Exclusión de compilaciones concurrentes | `test-build-lock.py`, sin compilar el juego. | Correcta: exclusión, directorios independientes y liberación tras éxito, fallo y terminación. |
| Publicación existente | [Actions 40422644822][ci]. | Compilación C#, pruebas visuales, Firestore, Docker amd64/arm64, Android, Windows y publicación terminados correctamente. |

Los resultados de integración de la CI que incluyen la batería del motor no deben sumarse a las 499 pruebas como si fueran casos independientes. Las capturas y comprobaciones de la publicación corresponden al commit fijado al inicio del informe.

## Persistencia, cuentas y publicación

Se revisaron la separación del inventario temporal y el permanente, las comprobaciones de propiedad de objetos, la venta transaccional con recibos, los reintentos tras respuestas inciertas, la selección atómica del equipo, las preferencias por cuenta, la validación de guardados y la privacidad del inventario en los estados enviados a otros jugadores. No se identificó en esos recorridos otro defecto confirmado que añadir a los anteriores.

Las reglas del repositorio siguen permitiendo crear telemetría anónima de partidas. Es una decisión ya documentada en la revisión del 24 de octubre: esos informes no acreditan que una partida haya ocurrido y deben continuar separados de los premios autoritativos. Esta revisión no comprueba qué reglas están desplegadas en producción.

La Release pública conserva su APK Android arm64 de `114165543` bytes y el digest publicado `305f9b706af650dfae88460f46463ed833b26355be755d57ae32df354d7c4f2f`. Se verificaron los metadatos de publicación y el resultado de su CI; no se realizó una instalación nueva en un teléfono.

## Orden recomendado de corrección

1. Restaurar el campo de venta automática en el transporte y cubrir el recorrido completo.
2. Corregir la detección de campos huérfanos y el control del apuntado asistido.
3. Explicar la conversión por capacidad y corregir su aviso.
4. Unificar el total de regeneración y las vistas de reliquias.

## Límites y estado del trabajo

No se recompiló el juego, no se ejecutaron nuevos flujos de compilación y no se generaron APK, tags o releases. Se siguió la restricción de `AGENTS.md` y se utilizaron binarios existentes, pruebas Python y evidencias de la publicación ya terminada. No se modificaron archivos del juego, cuentas ni datos de producción.

Las reproducciones de transporte aíslan la cola real de `ArenaConnection` y la autoridad del motor; no constituyen una nueva sesión de red completa entre dispositivos. Las pruebas de interfaz y de plataformas citadas proceden de la CI publicada. No se probaron hardware Android, Windows o Raspberry Pi físicos ni un acceso interactivo real a Google.

Los valores de las reproducciones están guardados en [evidencias-v0.29.26-2026-10-27.json](evidencias-v0.29.26-2026-10-27.json). Esta revisión no garantiza ausencia de errores en los recorridos no ejercitados.

[r1]: https://github.com/andresmartinezmelgar/magicka/blob/81817e84627e6373006cfd9a7bf50a07bb34d452/src/Arcana.Core/ArenaConnection.cs#L156
[r1-authority]: https://github.com/andresmartinezmelgar/magicka/blob/81817e84627e6373006cfd9a7bf50a07bb34d452/src/Arcana.Core/GameWorld.DefenseEconomy.cs#L38
[r2]: https://github.com/andresmartinezmelgar/magicka/blob/81817e84627e6373006cfd9a7bf50a07bb34d452/src/Arcana.Core/GameWorld.DefenseAssistance.cs#L114
[r2-cleanup]: https://github.com/andresmartinezmelgar/magicka/blob/81817e84627e6373006cfd9a7bf50a07bb34d452/src/Arcana.Core/GameWorld.Defense.cs#L444
[r2-damage]: https://github.com/andresmartinezmelgar/magicka/blob/81817e84627e6373006cfd9a7bf50a07bb34d452/src/Arcana.Core/GameWorld.cs#L822
[r3]: https://github.com/andresmartinezmelgar/magicka/blob/81817e84627e6373006cfd9a7bf50a07bb34d452/src/Arcana.Core/GameWorld.DefenseAssistance.cs#L47
[r3-input]: https://github.com/andresmartinezmelgar/magicka/blob/81817e84627e6373006cfd9a7bf50a07bb34d452/src/Arcana.Core/GameWorld.cs#L157
[r4-ui]: https://github.com/andresmartinezmelgar/magicka/blob/81817e84627e6373006cfd9a7bf50a07bb34d452/src/Arcana.Client/Scripts/EquipmentPanel.AutoSell.cs#L48
[r4-core]: https://github.com/andresmartinezmelgar/magicka/blob/81817e84627e6373006cfd9a7bf50a07bb34d452/src/Arcana.Core/GameWorld.DefenseEconomy.cs#L170
[r5-source]: https://github.com/andresmartinezmelgar/magicka/blob/81817e84627e6373006cfd9a7bf50a07bb34d452/src/Arcana.Core/GameWorld.Snapshot.cs#L26
[r5-total]: https://github.com/andresmartinezmelgar/magicka/blob/81817e84627e6373006cfd9a7bf50a07bb34d452/src/Arcana.Core/GameWorld.DefenseEconomy.cs#L271
[r5-ui]: https://github.com/andresmartinezmelgar/magicka/blob/81817e84627e6373006cfd9a7bf50a07bb34d452/src/Arcana.Client/Scripts/DefensePanel.cs#L327
[r6]: https://github.com/andresmartinezmelgar/magicka/blob/81817e84627e6373006cfd9a7bf50a07bb34d452/src/Arcana.Client/Scripts/Main.Relics.cs#L30
[ci]: https://github.com/andresmartinezmelgar/magicka/actions/runs/40422644822
