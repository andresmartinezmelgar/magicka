# Arquitectura en C#

## Separación de responsabilidades

`Arcana.Core` no importa Godot ni ASP.NET. Recibe intenciones, avanza a pasos fijos y produce una copia del estado. Se utiliza tanto en el proceso dedicado como en la práctica local.

El cliente envía selección de elemento, inicio/fin de lanzamiento, objetivo de movimiento y apuntado. No envía el daño, la vida o una nueva posición válida. `GameWorld.ApplyMessage` comprueba secuencias y coordenadas; `GameWorld.Step` decide movimiento, colisiones y efectos.

`GameService` administra un `GameWorld` por sala y una cola de entrada por jugador. Los IDs de magos son locales; la clave de sesión de transporte es un GUID para evitar colisiones entre salas. Solo se envía el estado a las sesiones de su sala. El temporizador consume intenciones y ejecuta la simulación bajo un único bloqueo. Los estados se copian en ese bloqueo y se serializan fuera. Cada conexión tiene un único emisor y conserva los dos estados recientes, para que un cliente lento no detenga a los demás. Los eventos recientes forman parte del estado y tienen un identificador para no reproducir el mismo sonido o mensaje dos veces.

## Elementos y formas

`ElementBuffer` conserva tres elementos como máximo. Una tecla incompatible elimina un elemento contrario. El cuarto elemento compatible se ignora. `SpellRecipe` clasifica la selección por prioridad: escudo, tierra, hielo, arcano/vida, electricidad, chorro y fuerza sin elementos.

`GameWorld.Hit` aplica los componentes elementales y sus relaciones con el estado del objetivo. La misma combinación sirve para lanzar de frente, sobre uno mismo o en área, con las reglas específicas de cada estilo.

Los números de daño, alcance, duración y reutilización de esta versión están en `GameWorld.cs` y `Elements.cs`. Son valores de esta implementación. Los magicks están en `GameWorld.Magick`; las decisiones de bots están en `ThinkBot`.

## Red

El saludo es JSON sobre WebSocket: versión del protocolo, nombre, preferencia de equipo, contraseña de servidor, ID de sala y contraseña de sala. La respuesta contiene el identificador del jugador y el primer estado. Después viajan intenciones del cliente y estados del servidor. Se aceptan como máximo 4096 bytes por mensaje de entrada y 180 mensajes por segundo por conexión; las colas de entrada son acotadas.

El cliente utiliza `ClientWebSocket`, incluido en .NET. El servidor utiliza WebSocket de ASP.NET Core. Se reutiliza el mismo formato JSON en los tres proyectos. La búsqueda LAN es un canal UDP independiente y solo sirve para encontrar la dirección de conexión.

La frecuencia de simulación es 60 Hz; la de envío es 20 Hz; el cliente dibuja a la frecuencia de pantalla y acerca las mallas a la última posición recibida. Esa interpolación evita saltos de dibujo, pero añade retraso y no equivale a predicción de movimiento.

## Launcher y distribución

`RoomBrowser` usa HTTP para consultar y crear salas. `RoomLobby` construye las pantallas de selección y creación; `Launcher` administra IP, LAN, práctica y alojamiento. `LocalServerHost` inicia solo el servidor que acompaña al paquete, pasa la configuración en el entorno de ese proceso verifica un identificador único del proceso y guarda sus mensajes en logs JSONL con rotación. El inicio y el cierre se coordinan para cancelar operaciones pendientes. No usa shell para iniciar el servidor. La contraseña no se guarda en disco ni se muestra en la lista de salas.

El cliente exportado incluye Godot y el runtime .NET. El servidor se publica para `win-x64` con `--self-contained true`. `package-windows.ps1` conserva todos los archivos exportados y verifica que existen ambos runtimes. La CI ejecuta ese paquete antes de publicar el ZIP. `ParentLifetimeService` cierra el servidor alojado si desaparece el proceso del juego; un servidor dedicado no recibe esa variable.

## Representación

`ArenaRenderer` construye ruinas, suelo, magos, escudos y hechizos con mallas procedurales. `WizardVisual` recibe el estado del mago. `Hud` representa vida, energía, selección, clasificación y minimapa. `SpellAudio` genera breves ondas PCM propias.

Los diccionarios de mallas usan el identificador de cada entidad para reutilizar nodos. Al desaparecer una entidad del estado, su nodo se elimina. Los rayos reutilizan un conjunto acotado de segmentos; los eventos se reproducen una sola vez.

## Pruebas

El proyecto de pruebas es una aplicación de consola sin paquetes externos. Devuelve código 0 si todo pasa y 1 si hay fallos. Comprueba reglas, estados, serialización, colisiones, reinicio de partidas y un minuto simulado con ocho bots. Con `--server=...` también abre dos clientes reales y comprueba mensajes, descubrimiento LAN y rechazo de saludos inválidos.

## Diagnóstico y rendimiento

`Diagnostics.cs` es independiente de Godot y ASP.NET. Ofrece un registro JSONL con cola acotada, escritura asíncrona, rotación por tamaño, retención y ocultación de secretos. `ProcessDiagnostics` registra excepciones no controladas y tareas no observadas sin cambiar la terminación del runtime. `DiagnosticLoggerProvider` conecta los eventos del servidor y `EngineLogBridge` captura errores/avisos de Godot sin volcar variables del script.

`SnapshotValidation` comprueba la estructura y los límites antes de representar estado remoto. El núcleo copia las entradas y recetas del snapshot; modificar un mensaje o una copia no modifica una partida ya aceptada. `ArenaConnection` serializa envíos y cierre, y espera a que se cancele un saludo pendiente antes de liberar recursos.

Las salas sin humanos se pausan. El servidor reutiliza contenedores temporales y serializa solo el último estado de un lote de pasos. Los bots reutilizan recetas y buscan el rival más cercano sin ordenar listas. El cliente conserva el HUD entre snapshots y reutiliza celdas de clasificación, materiales y colecciones de limpieza; la interpolación y las animaciones continúan en cada fotograma. Las voces de audio se detienen y liberan al cerrar.

## Vida y escudo

`Wizard.ShieldMax` conserva la capacidad aplicada, aunque el daño reduzca `Shield`. El snapshot transmite `ShieldMax` y `ShieldSeconds`; la expiración, muerte y reaparición limpian los valores. El HUD y las barras 3D usan la misma capacidad y diferencian vida, protección y energía. Los campos se añaden con valor predeterminado cero para mantener compatibilidad con snapshots anteriores del protocolo v2.

`ElementIcon` dibuja formas vectoriales sin texturas externas. `Ui.ElementTile` reutiliza símbolo, tecla, nombre y color en launcher y HUD. `VisualFixture` proporciona escenas deterministas para capturas; `--hud-smoke` verifica barras y cifras en seis estados sin conectarse a un servidor.
