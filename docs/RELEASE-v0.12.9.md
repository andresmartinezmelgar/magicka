# Arcana Arena v0.12.9

El mago interpola su posición entre muestras confirmadas del servidor. La cámara sigue esa posición visible y los pasos avanzan según la distancia recorrida, también al ir más lento, cargar un hechizo o usar celeridad. Al detenerse vuelve al reposo sin el arrastre del seguimiento exponencial. Congelaciones, reapariciones, teletransportes y pausas prolongadas reinician la interpolación.

La representación deja un margen de 75 ms en red, donde las muestras llegan a 20 Hz, y un paso de simulación en práctica. No extrapola posiciones durante una pérdida de paquetes. La velocidad, las colisiones y las reglas de combate siguen perteneciendo a la simulación.

En móvil, el agua es azul, el frío turquesa, el rayo violeta y el arcano magenta. Vida, escudo, tierra y fuego tienen tonos más definidos. Los botones muestran símbolos elementales y bordes más gruesos sobre un fondo más opaco. La iluminación pierde el tinte amarillo y el terreno baja de brillo; barreras, rayos y partículas conservan más color en sus reflejos. El escritorio conserva su paleta e iluminación.

Se mantienen los scripts `Compilar-Juego.cmd` y `Compilar-APK.cmd` para generar los archivos localmente.

## Comprobaciones

- Movimiento a 30, 60 y 120 FPS con muestras a 20 Hz y fluctuación de llegada de hasta 15 ms.
- Velocidad constante, pasos por distancia, lentitud, celeridad, parada, ausencia de paquetes, giros, reinicio del reloj y reanudación de la práctica.
- Atlas, anclajes, congelación, muerte y reaparición en Godot.
- Contraste de texto superior a 4,5:1 sobre el fondo de referencia de los controles móviles y símbolos en los ocho botones.
- Capturas OpenGL de la interfaz móvil, barreras y rayos.
