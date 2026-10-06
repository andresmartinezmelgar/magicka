# Arquitectura en C#

## Separación de responsabilidades

`Arcana.Core` no importa Godot ni ASP.NET. Recibe intenciones, avanza a pasos fijos y produce una copia del estado. Se utiliza tanto en el proceso dedicado como en la práctica local.

El cliente envía selección de elemento, inicio/fin de lanzamiento, objetivo de movimiento y apuntado. No envía el daño, la vida o una nueva posición válida. `GameWorld.ApplyMessage` comprueba secuencias y coordenadas; `GameWorld.Step` decide movimiento, colisiones y efectos.

`GameService` administra un `GameWorld` por sala y una cola de entrada por jugador. Los IDs de magos son locales; la clave de sesión de transporte es un GUID para evitar colisiones entre salas. Solo se envía el estado a las sesiones de su sala. El temporizador consume intenciones y ejecuta la simulación bajo un único bloqueo. Los estados se copian en ese bloqueo y se serializan fuera. Cada conexión tiene un único emisor y conserva los dos estados recientes, para que un cliente lento no detenga a los demás. Los eventos recientes forman parte del estado y tienen un identificador para no reproducir el mismo sonido o mensaje dos veces.

## Elementos y formas

`ElementBuffer` conserva tres elementos como máximo. Una tecla incompatible elimina un elemento contrario. El cuarto elemento compatible se ignora. `SpellRecipe` clasifica la selección por prioridad: escudo, tierra, hielo, arcano/vida, electricidad, chorro y fuerza sin elementos.

`GameWorld.Hit` aplica los componentes elementales y sus relaciones con el estado del objetivo. La misma combinación sirve para lanzar de frente, sobre uno mismo o en área, con las reglas específicas de cada estilo.

`SpellBalance` comparte el daño, los multiplicadores, la carga y las magias especiales entre combate y catálogo F1. `Elements.cs` resuelve forma y alcance; `GameWorld.cs` aplica colisiones, capacidades y estados. Los magicks están en `GameWorld.Magick`; las decisiones de bots están en `ThinkBot`.

## Red

El saludo es JSON sobre WebSocket: versión del protocolo, nombre, preferencia de equipo, contraseña de servidor, ID de sala y contraseña de sala. La respuesta contiene el identificador del jugador y el primer estado. Después viajan intenciones del cliente y estados del servidor. Se aceptan como máximo 4096 bytes por mensaje de entrada y 180 mensajes por segundo por conexión; las colas de entrada son acotadas.

El cliente utiliza `ClientWebSocket`, incluido en .NET. El servidor utiliza WebSocket de ASP.NET Core. Se reutiliza el mismo formato JSON en los tres proyectos. La búsqueda LAN es un canal UDP independiente y solo sirve para encontrar la dirección de conexión.

La frecuencia de simulación es 60 Hz; la de envío es 20 Hz. `WizardMotion` interpola entre posiciones confirmadas con 75 ms de margen en red y un paso de simulación en práctica. Los pasos dependen de la distancia recorrida. No extrapola a través de obstáculos; muerte, reaparición, congelación y saltos ajustan la posición inmediatamente. El servidor conserva la intensidad del joystick y limita los vectores diagonales a la velocidad máxima.

## Launcher y distribución

`RoomBrowser` usa HTTP para consultar y crear salas. `RoomLobby` construye las pantallas de selección y creación; `Launcher` administra IP, LAN, práctica y alojamiento. `LocalServerHost` inicia solo el servidor que acompaña al paquete, pasa la configuración en el entorno de ese proceso verifica un identificador único del proceso y guarda sus mensajes en logs JSONL con rotación. El inicio y el cierre se coordinan para cancelar operaciones pendientes. No usa shell para iniciar el servidor. La contraseña no se guarda en disco ni se muestra en la lista de salas.

El cliente exportado incluye Godot y el runtime .NET. El servidor se publica para `win-x64` con `--self-contained true`. `package-windows.ps1` conserva todos los archivos exportados y verifica que existen ambos runtimes. La CI ejecuta ese paquete antes de publicar el ZIP. `ParentLifetimeService` cierra el servidor alojado si desaparece el proceso del juego; un servidor dedicado no recibe esa variable.

## Representación

`ArenaRenderer` construye ruinas, suelo, escudos y hechizos con mallas procedurales. `WizardVisual` representa el mago con un `Sprite3D` orientado hacia la cámara. `WizardSprites` comparte cinco atlas, lee los anclajes y prepara el material que cambia solo la tela por equipo. La dirección, movimiento, carga, lanzamiento y daño del snapshot deciden la pose. `Hud` representa vida, energía, selección, clasificación y minimapa. `SpellAudio` genera breves ondas PCM propias. [Formato, arte y animación de los magos](SPRITES-MAGOS.md).

Los diccionarios de mallas usan el identificador de cada entidad para reutilizar nodos. Al desaparecer una entidad del estado, su nodo se elimina. Los rayos reutilizan un conjunto acotado de segmentos; los eventos se reproducen una sola vez.

## Pruebas

El proyecto de pruebas es una aplicación de consola sin paquetes externos. Devuelve código 0 si todo pasa y 1 si hay fallos. Comprueba reglas, estados, serialización, colisiones, reinicio de partidas y un minuto simulado con ocho bots. Con `--server=...` también abre dos clientes reales y comprueba mensajes, descubrimiento LAN y rechazo de saludos inválidos.

## Diagnóstico y rendimiento

`Diagnostics.cs` es independiente de Godot y ASP.NET. Ofrece un registro JSONL con cola acotada, escritura asíncrona, rotación por tamaño, retención y ocultación de secretos. `ProcessDiagnostics` registra excepciones no controladas y tareas no observadas sin cambiar la terminación del runtime. `DiagnosticLoggerProvider` conecta los eventos del servidor y `EngineLogBridge` captura errores/avisos de Godot sin volcar variables del script.

`SnapshotValidation` comprueba la estructura y los límites antes de representar estado remoto. El núcleo copia las entradas y recetas del snapshot; modificar un mensaje o una copia no modifica una partida ya aceptada. `ArenaConnection` serializa envíos y cierre, y espera a que se cancele un saludo pendiente antes de liberar recursos.

Las salas sin humanos se pausan. El servidor reutiliza contenedores temporales y serializa solo el último estado de un lote de pasos. Los bots reutilizan recetas y buscan el rival más cercano sin ordenar listas. El cliente conserva el HUD entre snapshots y reutiliza celdas de clasificación, materiales y colecciones de limpieza; la interpolación y las animaciones continúan en cada fotograma. Las voces de audio se detienen y liberan al cerrar.

## Vida y escudo

`Wizard.ShieldMax` conserva la capacidad aplicada, aunque el daño reduzca `Shield`. El snapshot transmite `ShieldMax`, `ShieldSeconds` y `RegenerationRate`; la expiración, muerte y reaparición limpian los valores. El HUD y las barras 3D usan la misma capacidad y diferencian vida, protección y energía. Las versiones v0.10.0 y v0.11.0 comparten protocolo 8, mapas, objetos y elementos preparados comunes.

## Mapas y control total

`WorldOptions.Map`, `CreateRoomRequest.Map`, `RoomInfo.Map` y `WorldSnapshot.Map` identifican la geometría común. `ArenaMap.Get` devuelve suelo, contorno, pilares, puntos y apariciones. Ciudadela consta de tres salas de 22×64 y dos pasillos de 22×32: exactamente cuatro veces el suelo original. Los límites forman segmentos que detienen cuerpos, rayos, rocas y traslación.

Las rutas A* se construyen sobre el mismo suelo y se reutilizan durante el desplazamiento; nunca atraviesan una esquina del contorno. El cliente reconstruye la geometría al cambiar de mapa, adapta la cámara y dibuja el minimapa con escala uniforme.

En guerra de Ciudadela, `GameWorld` suma ticks de control total sin disputa. Al perder o disputar un punto, borra el contador. A los 900 ticks consecutivos termina la partida; tickets y muertes no generan otra vía de victoria. El snapshot transmite `HoldingTeam` y `HoldSeconds` para que el HUD represente el tiempo autoritativo. El reinicio limpia ambos valores.

`ElementIcon` dibuja formas vectoriales sin texturas externas. `Ui.ElementTile` reutiliza símbolo, tecla, nombre y color en launcher y HUD. `VisualFixture` proporciona escenas deterministas para capturas; `--hud-smoke` verifica barras y cifras en seis estados sin conectarse a un servidor.

## Afinidades, objetivos y efectos

`WizardTypes` define cinco tipos y multiplicadores de 1,25. `GameWorld.ElementDamage` aplica el bonus del atacante, la debilidad del defensor y la guarda a cada componente. `BurnMultiplier` conserva la potencia inicial de la quemadura incluso si el atacante se desconecta. El tipo es inmutable en `Wizard`; se valida en el saludo y viaja en `WizardSnapshot`. `WorldSnapshot` replica `ScoreLimit` y `StartingTickets`. `MatchRules` proporciona objetivos compartidos por HUD, salas y ayuda.

El servidor emite `shield_hit`, `shield_break` y `hurt`; limita los impactos continuos a uno por mago cada 0,14 s, y siempre emite la rotura. `CombatEffects` mantiene pools de hasta 320 partículas y 24 ondas, con mallas y materiales compartidos y un presupuesto de 64 partículas nuevas por fotograma. Procesa eventos por ID, crea estelas y partículas de estado a intervalos y limpia los efectos al salir. `SpellVisualStrength` gradúa el detalle por cantidad de elementos, incluidos los repetidos, con brillo contenido y partículas cortas. `--effects-smoke` comprueba doce eventos, los tres niveles de detalle, cuatro formas de canal, radio de proyectil, el canal frente a la siguiente selección, saturación, reutilización y limpieza. [Escala de efectos y capturas](EFECTOS.md).

`Compilar-Juego.cmd` ejecuta `auto-compile.ps1` en Windows PowerShell 5.1. Las herramientas se almacenan en `.tools/`; `package-windows.ps1` construye `artifacts/windows`. `Publish-GamePackage` valida el paquete, copia a una carpeta temporal y sustituye `Juego/` mediante movimientos, con restauración si falla el movimiento. La prueba `test-build-output.ps1` verifica reemplazo, segunda publicación, rutas con espacios y preservación del juego ante un paquete incompleto.

## Interacciones y catálogo

`GameWorld.Beams` resuelve los canales rectos después del movimiento. Recorta el alcance por obstáculos, barreras y personajes; busca encuentros entre segmentos y conserva aportaciones separadas para los tramos combinados. Cada canal se une una vez por paso, cada salida tiene hasta dos rebotes y el snapshot conserva hasta 64 tramos. El renderer utiliza hasta 512 segmentos reutilizados para franjas de color y cadenas eléctricas.

`GameWorld.Effects` controla colocación y destinatarios de minas y splash de rocas. Los campos persistentes reutilizan el contenedor acotado de 40 entidades y conservan solo los elementos secundarios. `FieldSnapshot` separa radio del efecto y radio de activación; `GameEvent.Radius` permite representar el alcance real.

`SpellCatalogue` enumera 120 selecciones válidas de hasta tres elementos, sin permutaciones ni pares incompatibles. `SpellDemonstration` repite intenciones sobre un `GameWorld` aislado. `SpellBook` muestra ese combate en un `SubViewport` con mundo 3D propio, y procesa pasos solo mientras su pestaña está visible. No comparte la conexión ni la simulación de la partida. Las demos se reinician limpiando nodos y eventos.

`CapturePointVisual` utiliza un shader por punto; el relleno angular y el color proceden del progreso firmado. Interpola el progreso sin crear materiales por fotograma, muestra disputa y porcentaje; `MiniMap` utiliza el mismo estado.

## Vida y reaparición en Ascensión

`ArenaDefinition` define el bonus por baja y calcula la espera por modo y muertes acumuladas. `Wizard.MaxHealth` es capacidad individual; cada baja de otro mago añade 10 solo en Ascensión. `Damage` actualiza la capacidad incluso en bajas póstumas, pero no revive al autor. Curaciones, minas, regeneración y `Respawn` respetan ese máximo. `ResetMatch` vuelve a `Wizard.BaseMaxHealth` y borra muertes, reiniciando también la espera. El snapshot ya transmite vida máxima y cuenta atrás; protocolo 6 permite distinguir el nuevo mapa en todos los clientes.

## Reliquias, elementos preparados y cifras de F1

`ArenaDefinition.ItemSpawns` define catorce posiciones fijas para Santuario. Ascensión usa `CreatePickups` y `FindPickupPosition` con un generador independiente: semilla aleatoria por mundo, opcionalmente reproducible mediante `WorldOptions.PickupSeed`. Excluye bordes, pilares, puntos de control, apariciones, otros objetos, magos vivos y muros activos. `TickPickups` recoloca los objetos aleatorios al terminar su espera de doce segundos. `Wizard.BuffStacks` conserva siete contadores; `RelicBuffs` calcula sus efectos. `Damage` limpia los contadores al morir y retira solo la vida temporal de Vitalidad, preservando el bonus de bajas. `ResetMatch` reinicia y recoloca los objetos. El snapshot copia sus posiciones y `RelicVisual.Update` mueve el nodo existente, sin un sorteo en el cliente.

`CastingElements` copia la receta de `Charging` o `Channel` cuando la selección ya se consumió. `DisplayElements` da prioridad a la selección nueva y permite mostrar tres casillas de color/tecla sobre cada personaje y en el HUD. Los eventos de lanzamientos instantáneos mantienen las casillas brevemente durante su pose.

`SpellBalance` comparte constantes y daño por componente entre simulación y catálogo. Calcula unidades de impacto, segundo, carga, fragmento y mina, curación, quemadura y objetivo mojado sin aplicar afinidades o mejoras. F1 muestra sus cifras con la descripción de condiciones base. El HUD de partida escala a 0,75 y ajusta su tamaño lógico al viewport para conservar anclajes; el panel de vida/escudo compensa ese factor para recuperar sus dimensiones y márgenes originales.

`ArenaRenderer.Scenery` construye superficies e iluminación según `ArenaTheme`: bosque, ciudad, ruinas y santuario. Edificios y árboles exteriores se sitúan fuera de los rectángulos de suelo. La vegetación baja utiliza MultiMesh con colores por instancia. Los obstáculos centrales reutilizan radios y posiciones de `ArenaDefinition`; solo cambia su representación. La CI verifica tanto el estado de los nodos como cuatro capturas OpenGL.
