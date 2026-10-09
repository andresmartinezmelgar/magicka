# v0.12.23 — Equipos y carga previa

En la ventana de salas se puede elegir **Azul**, **Rojo** o **Automático**, tanto antes de crear como antes de entrar. La elección explícita se respeta; Automático usa el equipo menos poblado. En un duelo los humanos deben ocupar equipos opuestos. Si el rival es un bot, cambia de lado para permitir que el creador elija el suyo.

La partida muestra una barra mientras construye el mapa, carga los cinco atlas de magos, prepara los sonidos y reserva las partículas y los anillos. También dibuja las variantes de rayos, conos, proyectiles, barreras, minas, suelos, meteoros y protecciones detrás de la pantalla de carga. En escritorio y móvil cada trabajo espera a que el motor termine de dibujar un fotograma antes de avanzar. Esto prepara los recursos y sus variantes gráficas para OpenGL, donde cargar solamente la textura o mantener una malla invisible no basta para evitar la compilación durante el primer uso. [Documentación de Godot sobre estos tirones](https://docs.godotengine.org/en/4.5/tutorials/rendering/jitter_stutter.html).

Los controles y la simulación local permanecen detenidos durante la carga. Una sala nueva detiene también su reloj y sus bots hasta recibir la confirmación de todos sus clientes conectados. Cancelar desconecta al jugador y libera esa espera. Si la partida ya está iniciada, los demás continúan jugando y el jugador que entra queda protegido y sin acciones hasta terminar. El cliente comprueba la confirmación del servidor antes de retirar la barra. Las cargas tienen un límite de 120 segundos y permiten volver al menú con Cancelar, Escape o Atrás.

El protocolo pasa de **9 a 10**: actualizar el cliente y el servidor juntos. `VERSION`, C# y los presets Windows/Android usan **0.12.23**; Android usa el código **12023**.

Validación local:

- Compilación del núcleo, servidor y pruebas; cliente Godot .NET en Debug y Release.
- 148 pruebas de reglas, estado y transporte; 21 pruebas contra el servidor real, incluida la espera conjunta, los equipos elegidos, la cancelación y la entrada tardía protegida.
- Nueve comprobaciones Godot sin pantalla: práctica, móvil, seis mapas, laboratorio, HUD, efectos, sprites, combinaciones y carga.
- Carga completa y cancelación con OpenGL en interfaz de escritorio y móvil; creación desde el formulario en Rojo y conexión de un segundo cliente en Azul, comprobando que el reloj no avance antes de confirmar ambos.
- Capturas reales de la barra y del formulario; sincronización de versiones y seis pruebas de los scripts de versión, con dos comprobaciones PowerShell omitidas porque no está instalado aquí.

La comprobación gráfica local usa Mesa/llvmpipe. No se ha medido el arranque en dos teléfonos físicos ni se ha generado aquí una APK o un ejecutable Windows. CI incluye las nuevas comprobaciones y genera las capturas en `loading-review`.
