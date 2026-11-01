# Audio adaptativo

La composición original **Resonancia del santuario** acompaña los menús y la
partida. Tres capas Ogg sincronizadas de 40 segundos mantienen el mismo tempo
y armonía. Las transiciones cambian el volumen gradualmente y conservan el
compás. Los recursos funcionan sin conexión.

| Situación | Respuesta |
| --- | --- |
| Menú y carga | Cuerdas, arpa y campanas suaves. |
| Rivales cercanos y lanzamiento de hechizos | Entra la percusión y el ostinato. Las ráfagas mantienen la intensidad brevemente. |
| Vida inferior al 42 % | Aumenta gradualmente la capa de peligro; alcanza el máximo al 10 %. |
| Coloso u oráculo a menos de 22 m | Capa de peligro elevada. No se activa por jefes lejanos. |
| Teletransporte | Efectos diferentes para salida y llegada, aunque el desplazamiento aleje el punto de salida. La música baja brevemente para destacar el efecto. |
| Pausa, espera, muerte o final | Se retiran las capas de combate y peligro. |
| Final de partida | Acorde de victoria o derrota una sola vez al recibir la transición. Todos contra todos usa el ID del ganador. |
| Android en segundo plano | Música pausada conservando el compás; efectos detenidos. |

## Efectos

Se precargan 32 sonidos originales durante la pantalla de carga. No se sintetiza
audio al recibir un evento en combate. Hay ocho voces de efectos y cuatro
reservadas para teletransporte, muerte, rotura de escudo y final de partida.
Las repeticiones tienen un límite de frecuencia y los efectos se atenúan con
la distancia hasta un radio de escucha de 18 m.

Cada mezcla conserva hasta tres elementos distintos; las cargas repetidas no
duplican voces. Lanzamiento, impacto y pulso tienen duración y envolvente propias.

| Elemento | Timbre |
| --- | --- |
| Agua | Burbujeo y ruido suave. |
| Vida | Campanas consonantes. |
| Escudo | Resonancia metálica. |
| Frío | Cristales agudos. |
| Rayo | Chasquido eléctrico. |
| Arcano | Oscilación y armónicos mágicos. |
| Tierra | Golpe grave y vibración. |
| Fuego | Ruido de llama y crepitación. |

También se cubren barreras, minas, salpicaduras, rebotes, refracciones,
explosiones, ruptura/impacto de escudo, cadenas pasivas y explosiones arcanas.
Los eventos sin elementos ya no se convierten implícitamente en Agua.

## Ajustes y coste

**Mi perfil → Sonido** y **Pausa → Sonido** contienen controles independientes
de música y efectos. Cero los silencia. Se aplican inmediatamente y se guardan
en este dispositivo al soltar el control, cambiar el foco o cerrar la pantalla.
La configuración se conserva sin cuenta Google. La música consulta las amenazas
a 5 Hz y suaviza la mezcla cada fotograma, sin crear arreglos por fotograma.
No se inicializa audio en modo headless ni en los subescenarios del libro de hechizos.

## Verificación

- `python3 scripts/check-soundtrack.py`: hashes, decodificación Vorbis,
  duración exacta común, estéreo, niveles y continuidad del bucle.
- `AdaptiveAudioTests`: aliados/rivales, distancia, jefes, vida, pausas,
  portales sin elementos, mezclas, precarga y ganador por equipo/jugador.
- Compilación, ejecución de las regresiones C# y escucha en Android/Windows:
  pendientes de petición expresa, conforme a `AGENTS.md`.

Referencia de sincronización de Godot 4.5:
https://docs.godotengine.org/en/4.5/classes/class_audiostreamsynchronized.html
