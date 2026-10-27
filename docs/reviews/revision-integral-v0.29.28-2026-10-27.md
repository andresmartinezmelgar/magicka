# Revisión integral para Arcana Arena 0.29.28

Base: `0fb72f2c5116d269007deb1bbbc562cfb6065f0e`, posterior a la versión publicada 0.29.27. La petición autoriza corregir, compilar y publicar tag e instaladores. Se han confirmado y corregido seis fallos; también se unifica el color del haz arcano y se prepara el RAR solicitado.

## Alcance

Inventario inicial: 111 archivos C# del motor, 168 del cliente, 23 del servidor, uno del servidor de cuentas y 61 de pruebas. Se revisaron los recorridos de combate, selección elemental, daño y defensas, mejoras, reliquias, auras, pasivas, IA, pausa y asistencia, inventario, transporte, validación de estados, guardado, cuentas y actualización. Se contrastaron las fórmulas del catálogo, bestiario y tablas con la simulación y con las regresiones existentes. La compilación y las pruebas complementan esta revisión; no constituyen una prueba formal de todos los estados posibles.

La comprobación de recursos incluye los 18 archivos gráficos de Assets: 11 PNG, tres WebP y cuatro SVG. Los cinco atlas de magos contienen 80 poses. Se verificaron decodificación, dimensiones, transparencia disponible, anclajes de pies, ojos y lanzamiento, referencias y reutilización. Los anclajes de pies pueden estar en el borde de la celda. Las mallas, partículas, shaders, iconos y animaciones se comprueban además en las escenas de Godot.

## Fallos corregidos

| Hallazgo | Evidencia anterior | Corrección |
| --- | --- | --- |
| Barreras válidas rechazadas en red | El máximo de 2080 HP no contempla el nivel de Supervivencia; equipo y nivel pueden producir 2808 HP. | El validador deriva el máximo de equipo y nivel, conservando el límite de PvP. Prueba de serialización, deserialización y estado privado del destinatario. |
| Rocas con Vida atraviesan aliados heridos | El filtro de proyectiles excluye todos los aliados, incluidas mezclas curativas. | Los proyectiles con Vida pueden impactar y curar aliados. Los ofensivos continúan atravesándolos sin dañarlos. |
| Efectos póstumos pierden bando y potencia | Al retirar al enemigo, su suelo empieza a dañar a otros monstruos y usa potencia base. También afecta a proyectiles y quemaduras. | Una fuente inactiva conserva tipo, bando y potencia mientras tenga efectos. No es combatiente ni objetivo. Sobrevive al guardado y se libera al extinguirse el último efecto. |
| Oro de botín inválido admitido en guardados | Se restaura oro negativo o superior al máximo; recogerlo puede fallar o producir un estado inválido. | La lectura valida el mismo intervalo 0–208 que el estado de red antes de continuar. |
| Escudos de prismas filtran dos veces las mezclas | La presión ya excluye componentes inmunes, pero el daño vuelve a aplicar la fracción elemental. Agua aporta cero. | Los componentes se resuelven una vez, se incorpora la presión especial de Agua y las estadísticas conservan los pesos reales. Casos de Agua, Fuego, Frío, Rayo y Arcano. |
| Arrastre de sprites de magos enemigos | La raíz y `WizardVisual` interpolan simultáneamente; el sprite queda atrás tras teletransportarse. | Un solo interpolador mueve la raíz, las auras y los avisos. Se comprueban cuatro magos a 30/60/120 FPS, con y sin demora de red. |

La reproducción de prismas midió 0 en lugar de 0,05683 HP para Agua y aproximadamente la mitad de lo debido para los otros cuatro elementos. Son cifras de un paso con un haz recién iniciado, no daño por segundo.

## Presentación y distribución

- El haz arcano moderno toma el color de Arcano de la paleta de controles y textos.
- Se incluye el formato solicitado: `+11.33% (Max 21.33)`, sin mínimo y conservando la tabla.
- El RAR de Windows se crea en formato RAR 5 con el archivero oficial fijado por versión y SHA-256. Se comprueban la firma del formato, CRC, extracción y el hash de cada archivo frente al ZIP. Se rechazan entradas incompletas, corruptas o con rutas inseguras. Solo se coloca el archivo final cuando la verificación termina.
- La APK exige firma permanente. Windows mantiene su ZIP y el actualizador conserva sus paquetes y deltas.
- Metadatos de 0.29.28: Android `29028`, Windows `0.29.28.0`, protocolo `38`. La extensión opcional del guardado local no cambia los mensajes de red.

## Evidencias locales

| Comprobación | Resultado |
| --- | --- |
| Batería inicial del motor | 506 correctas, cero fallidas. |
| Primeras reproducciones añadidas | Siete fallos: barrera, roca curativa, dos suelos con y sin continuación y oro inválido. |
| Reproducción adicional de prismas | Cinco casos fallidos antes de corregir el cálculo. |
| Motor corregido | **522 correctas, cero fallidas**, incluidas 16 regresiones nuevas. Las ejecuciones parciales no se suman a esta cifra. |
| Cliente local | Compila sin advertencias de C# tras aclarar la comprobación de los datos de la gráfica. |
| Inventario móvil, 640 × 450 | `EQUIPMENT_SMOKE_OK`: seis ranuras, calidades, guardado al salir, recuperación y aplicación del equipo. |
| Supervivencia móvil, 640 × 450 | `DEFENSE_SMOKE_OK`: compras, ventas, mejoras, pausa, controles, portales, botín y reutilización. |
| Magos enemigos | Fallo reproducido antes de corregirlo; `ENEMY_CASTER_MOTION_OK` tras 24 combinaciones y marcha con auras alineadas. |
| Gráficos | 18 recursos, cinco atlas y 80 poses comprobados. |
| Empaquetador RAR | Cuatro pruebas correctas con el archivero real. El paquete completo vuelve a verificarse al publicar. |
| Metadatos y parches | Comprobación de versión y pruebas Python de versiones, previews, publicación de deltas y reconstrucción correctas. Dos casos de Windows omitidos por plataforma. |

Las pruebas locales de interfaz usan Godot headless: comprueban nodos, transformaciones, controles y recursos; las capturas con renderizado real se validan en la CI con Xvfb. El error conocido de `EditorSettings::_EDITOR_GET` aparece al cerrar la importación del editor, no en las escenas ejecutadas. La política Android requiere `javac`, ausente localmente, y queda cubierta por el trabajo Android de la CI. Los resultados y hashes gráficos están en [evidencias-v0.29.28-2026-10-27.json](evidencias-v0.29.28-2026-10-27.json).

## Publicación y límites

La publicación estable depende de la compilación completa con MSBuild, motor y red, Firestore en emulador, Docker amd64/arm64, escenas visuales con Xvfb y exportación de Android y Windows. El RAR se genera y verifica antes de crear la Release. Los resultados oficiales quedan asociados al commit del tag en GitHub Actions; si repiten la batería local no deben contarse como casos independientes.

No se han modificado cuentas ni datos de producción. No se ha instalado interactivamente el juego en hardware Android, Windows o Raspberry Pi ni se ha probado un inicio real de sesión con Google en esos dispositivos. Las estadísticas anónimas de Firestore continúan separadas de los premios autoritativos, como documentaban las revisiones anteriores.
