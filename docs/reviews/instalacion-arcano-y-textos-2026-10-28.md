# Revisión de instalación, arcano y textos — 28/10/2026

Base estable: `main` / `v0.29.28`, commit `26b5f30fabdda744a83f028504d4a77ba71b96fa`.
Base de los cambios: `pruebas/supervivencia-procedural-autoguardado`, commit `3b3b12b83fb3ffa0d4b144b5349b17e24515ce62`.
Última prueba publicada consultada: `preview-v0.29.33-r626`, commit `6c13c207762bf115ef52f00df12395cf463d5488`.

## Instalación: hallazgos y corrección

El transporte ya reintenta fallos de red, conserva parciales, verifica tamaño y SHA-256 y reutiliza paquetes verificados. Se revisaron también la reconstrucción diferencial, la entrega al instalador Windows y la política de versiones estable/pruebas. El síntoma comunicado ocurre después de descargar, al instalar, por lo que añadir reintentos de descarga no lo resuelve.

En Android había tres transiciones sin esperar a que su actividad estuviera reanudada:

1. `onMainActivityResult` continuaba la instalación inmediatamente después de Ajustes. Android entrega ese resultado antes de `onResume`.
2. El trabajador confirmaba la sesión de PackageInstaller al terminar de copiar la APK, aunque durante ese tiempo el juego hubiera pasado a segundo plano.
3. La actividad de resultado abría la confirmación del sistema directamente desde `onCreate` / `onNewIntent`.

Se introduce `UpdateForegroundGate`: la preparación y la confirmación esperan a `onMainResume`, una sola vez, sin repetir la descarga ni la copia. También se admite la vuelta de Ajustes sin resultado explícito. La actividad de resultado espera a `onPostResume`. Los estados se gestionan en el hilo de la actividad y se descartan continuaciones al fallar.

La sesión se guarda antes de confirmarla; cada callback incluye su identidad. Los resultados tardíos de otra sesión se ignoran antes de modificar el estado o borrar una APK. Al empezar un intento se retira la identidad anterior. Se conserva compatibilidad con los callbacks de APK anteriores mediante `PackageInstaller.EXTRA_SESSION_ID`. Se mantienen las comprobaciones de ruta privada, hash, paquete, versión y firma del sistema, así como la política existente de confirmación de Android.

Los diagnósticos `update.android_status` registran fases, identificador de sesión y estado nativo completo sin sustituir los mensajes visibles por información técnica. Se conserva el error original si Android ya ha retirado una sesión que se intenta abandonar.

Estas carreras son defectos localizados en el código y compatibles con un fallo inicial que desaparece al reintentar. **No se ha reproducido el fallo concreto del dispositivo ni confirmado su causa única**: falta el registro nativo y no se ha indicado expresamente la plataforma. En Windows no se encontró una causa determinista del patrón comunicado mediante esta revisión estática.

Referencias primarias consultadas:

- [Android: orden de onActivityResult y onResume](https://developer.android.com/reference/android/app/Activity#onActivityResult(int,int,android.content.Intent)).
- [Godot 4.5.2: callbacks del plugin](https://github.com/godotengine/godot/blob/4.5.2-stable/platform/android/java/lib/src/org/godotengine/godot/plugin/GodotPlugin.java).
- [Godot 4.5.2: reenvío del ciclo de actividad](https://github.com/godotengine/godot/blob/4.5.2-stable/platform/android/java/lib/src/org/godotengine/godot/Godot.kt).

## Trabajo «revisar arcano y colorear textos»

La revisión del código no ha encontrado requisitos originales pendientes de implementar. Los siguientes cambios están integrados en `main` y en la versión estable 0.29.28; se ha comprobado también que la rama de pruebas los conserva.

| Requisito final | Evidencia de implementación y regresiones existentes |
| --- | --- |
| Arcano recto; sin buscar ni saltar a otro enemigo. Penetración solo mediante su afijo, sin desvío ni pérdida de daño. | `GameWorld.Beams.cs`; `BestiaryAndAffixTests.cs` cubre el primer objetivo, objetivos laterales, límite de penetración y escudos. |
| Reflexión y refracción de escudos. | `TraceBeam` conserva sus rebotes. La fusión entre dos haces sigue siendo una interacción independiente ya existente. |
| Rayo eléctrico ofensivo ignora aliados; Vida permite curarlos. | `GameWorld.Channel` excluye aliados de la selección eléctrica; las cadenas adicionales usan enemigos. Descripción coherente en `SpellCatalogue.cs`. |
| Subir nivel o recalcular equipo no cura sin una fuente. | `RecalculateDefender` limita los HP actuales al nuevo máximo; `DefenseTests.cs` conserva HP y fracciones. |
| Palabras elementales y efectos con colores según contexto; un color principal por combinación. | `ElementText.cs`, `Ui.cs`, `ElementTextLabel.cs`, `ElementTextButton.cs`, títulos de `SpellBook`; `ElementTextTests.cs` y `Main.ElementTextVerification.cs`. Incluye actualización dinámica y escape del marcado externo. |
| Arcano moderno coherente con la paleta elemental. | `ArenaRenderer.Energy.cs` usa el magenta de `Ui.ElementColor(Element.Arcane)` como color principal. |
| Botones Equipar/Desequipar, Marcar/Desmarcar favorito, Vender/Mantener ítems de menor calidad; retirados los marcos de estado solicitados inicialmente. | `EquipmentPanel.Selection.cs`, `EquipmentPanel.Usability.cs`, `EquipmentPanel.AutoSell.cs`; cambios finales posteriores a 0.29.25 integrados. |
| Consultar pantallas en Supervivencia pausa con un humano; con varios humanos se prioriza supervivencia. | `Main.DefenseInterface.cs` y `GameWorld.DefenseAssistance.cs`: escudo, retirada, peligros, curación normal y ataque condicionado a seguridad. |
| Tablas de atributos, objetos y mejoras de partida; desglose enumerado. | `EquipmentPanel.StatTables.cs`, `EquipmentPanel.ProgressStats.cs` y resumen compartido. Los atributos muestran el valor actual y `(Max ...)`. |
| Efectos nuevos para rayo y arcano, fuego + agua como vapor y alternativa de bajo coste. | `ElementChannelVisuals.cs`, `ArenaRenderer.Energy.cs`, `CombatEffects.Flames.cs`, `AdaptiveSpellQuality.cs`: equipos móviles limitados usan el perfil anterior; una caída de rendimiento lo activa y se recuperan los efectos nuevos tras 12 s estables. `AdaptiveSpellQualityTests.cs` cubre ambas decisiones. |
| Afijos de combate, Escudo vital, Espejo protector y ojos con seis míticos. | `CombatAffixes.cs`, `GameWorld.CombatAffixes.cs`, `MythicEyes.cs` y representaciones 2D/3D. Las copias de afijos aplican su rango mayor y los ojos requieren las seis piezas míticas. |

Los afijos posteriores Marca y Eco arcanos están en la rama de pruebas. Eco solo añade su haz cuando se equipa ese afijo; no se ha restaurado el salto automático del arcano normal. No forman parte de la estable 0.29.28.

### Los seis hallazgos posteriores a 0.29.26

| Hallazgo | Estado del código revisado |
| --- | --- |
| La regla de venta automática no llegaba al servidor. | `ArenaConnection.Send` conserva `AutoSellStyle` y la autoridad lo procesa; regresión `TransportRule`. |
| La asistencia ignoraba suelos dañinos tras morir su creador. | Se considera la fuente conservada del efecto; regresión `OrphanGround` para Fuego y Frío. |
| El cursor del inventario desviaba el arcano automático. | La asistencia mantiene su dirección y comprueba aliados en la línea; regresión `AssistedAim`. |
| Inventario lleno anunciaba un objeto recibido aunque se hubiera convertido en oro. | El evento anuncia «inventario lleno · objeto … convertido en … oro»; regresión `FullInventory`. La conversión por capacidad es distinta de la regla de venta automática. |
| El total de regeneración omitía el escudo de Vida. | Se incluye mientras el escudo existe y se retira al romperse, sustituirse o expirar; regresión `Regeneration`. |
| Reliquias con reglas de muerte incorrectas y sin desglose. | El menú y la tienda comparten el desglose por acumulaciones y distinguen el modo; regresión `RelicDescriptions`. |

Las seis regresiones se encuentran en `tests/Arcana.Tests/ReleaseAuditFixTests.cs`. Están integradas en la estable, junto con los ajustes adicionales de 0.29.28. La ejecución anterior [40533670570](https://github.com/andresmartinezmelgar/magicka/actions/runs/40533670570), asociada exactamente al commit de `main` indicado, terminó correctamente. Esa ejecución no valida la corrección nueva del instalador.

## Validación de esta revisión

Comprobaciones ejecutadas sin compilar: `git diff --check`, sintaxis de `scripts/test-android-update-policy.sh`, metadatos con `python3 scripts/version-support.py check .` (`0.29.33` / `29033`), existencia de los archivos y referencias de esta revisión y comprobación de los disparadores de Actions. El push de estos archivos no activa compilación ni publicación.

Se añade `UpdateForegroundGateTest.java` al ejecutor existente de las políticas Android: cubre el arranque antes de reanudar, la vuelta de permisos, la copia terminada en segundo plano, la ejecución única, la cancelación y una continuación registrada durante `resume`.

**Pendiente por `AGENTS.md`:** compilar Java/C#, ejecutar esas regresiones y las de interfaz, exportar APK y probar una actualización real. No se ejecuta `bash scripts/test-android-update-policy.sh`, porque invoca `javac`. No se incrementa versión, modifica un workflow ni crea tag o release.

La prueba física debe incluir permiso ya concedido, concesión por primera vez, denegación, bloquear/volver durante la preparación, cancelación de Android, segundo intento y callbacks de una sesión anterior. La APK instalada actualmente seguirá usando el instalador anterior hasta actualizarla a una compilación que incluya esta corrección.
