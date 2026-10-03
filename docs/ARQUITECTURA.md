# Arquitectura en C#

## Separación de responsabilidades

`Arcana.Core` no importa Godot ni ASP.NET. Recibe intenciones, avanza a pasos fijos y produce una copia del estado. Se utiliza tanto en el proceso dedicado como en la práctica local.

El cliente envía selección de elemento, inicio/fin de lanzamiento, objetivo de movimiento y apuntado. No envía el daño, la vida o una nueva posición válida. `GameWorld.ApplyMessage` comprueba secuencias y coordenadas; `GameWorld.Step` decide movimiento, colisiones y efectos.

`GameService` tiene una cola de entrada por jugador. El temporizador consume intenciones y ejecuta la simulación bajo un único bloqueo. Los estados se copian en ese bloqueo y se serializan fuera. Cada conexión tiene un único emisor y conserva los dos estados recientes, para que un cliente lento no detenga a los demás. Los eventos recientes forman parte del estado y tienen un identificador para no reproducir el mismo sonido o mensaje dos veces.

## Elementos y formas

`ElementBuffer` conserva tres elementos como máximo. Una tecla incompatible elimina un elemento contrario. El cuarto elemento compatible se ignora. `SpellRecipe` clasifica la selección por prioridad: escudo, tierra, hielo, arcano/vida, electricidad, chorro y fuerza sin elementos.

`GameWorld.Hit` aplica los componentes elementales y sus relaciones con el estado del objetivo. La misma combinación sirve para lanzar de frente, sobre uno mismo o en área, con las reglas específicas de cada estilo.

Los números de daño, alcance, duración y reutilización de esta versión están en `GameWorld.cs` y `Elements.cs`. Son valores de esta implementación. Los magicks están en `GameWorld.Magick`; las decisiones de bots están en `ThinkBot`.

## Red

El saludo es JSON sobre WebSocket: versión del protocolo, nombre, preferencia de equipo y contraseña. La respuesta contiene el identificador del jugador y el primer estado. Después viajan intenciones del cliente y estados del servidor. Se aceptan como máximo 4096 bytes por mensaje de entrada y 180 mensajes por segundo por conexión; las colas de entrada son acotadas.

El cliente utiliza `ClientWebSocket`, incluido en .NET. El servidor utiliza WebSocket de ASP.NET Core. Se reutiliza el mismo formato JSON en los tres proyectos. La búsqueda LAN es un canal UDP independiente y solo sirve para encontrar la dirección de conexión.

La frecuencia de simulación es 60 Hz; la de envío es 20 Hz; el cliente dibuja a la frecuencia de pantalla y acerca las mallas a la última posición recibida. Esa interpolación evita saltos de dibujo, pero añade retraso y no equivale a predicción de movimiento.

## Representación

`ArenaRenderer` construye ruinas, suelo, magos, escudos y hechizos con mallas procedurales. `WizardVisual` recibe el estado del mago. `Hud` representa vida, energía, selección, clasificación y minimapa. `SpellAudio` genera breves ondas PCM propias.

Los diccionarios de mallas usan el identificador de cada entidad para reutilizar nodos. Al desaparecer una entidad del estado, su nodo se elimina. Los rayos reutilizan un conjunto acotado de segmentos; los eventos se reproducen una sola vez.

## Pruebas

El proyecto de pruebas es una aplicación de consola sin paquetes externos. Devuelve código 0 si todo pasa y 1 si hay fallos. Comprueba reglas, estados, serialización, colisiones, reinicio de partidas y un minuto simulado con ocho bots. Con `--server=...` también abre dos clientes reales y comprueba mensajes, descubrimiento LAN y rechazo de saludos inválidos.
