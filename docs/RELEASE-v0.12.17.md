# v0.12.17 — Revisión de combate y bots

Revisados el orden de ejecución de acciones, colisiones de hechizos, captura y reaparición del frente, portales, peligros, navegación, decisiones de bots, representación del movimiento y transporte cliente/servidor.

El último paquete de apuntado se aplica antes de lanzar fragmentos, colocar barreras, golpear o soltar una roca. Estas acciones pueden llegar en el mismo lote de comandos que el giro, antes de que avance la simulación.

El origen del hechizo respeta también las paredes y los pilares, además de la frontera. Un mago que se acerca a una pared ya colocada no puede emitir un rayo o un chorro detrás de ella con el extremo del bastón.

Cuando una pared temporal corta la ruta entera, la navegación permite avanzar hasta el lugar transitable más cercano al destino. Los bots pueden acercarse al muro, cargar rocas contra un segmento concreto y abrir un paso antes de que desaparezca. El objetivo se mantiene durante la carga y se descarta al cancelar el hechizo o destruir la pared. Se evita atacar muros que no bloquean el objetivo y comenzar ofensivas contra enemigos ocultos sin línea de visión.

El generador comprueba que cada grupo cabe dentro de los doce segmentos ambientales y del máximo global de cuarenta paredes.

Diez regresiones cubren apuntado inmediato, rayos y chorros junto a paredes reales, rutas parciales sin atravesar obstáculos, aproximación y destrucción de muros por bots antes de treinta segundos, conservación del apuntado durante la carga, línea de visión y límites de segmentos. Los fallos se reprodujeron antes de aplicar sus respectivas correcciones. También se ejecutan las pruebas anteriores del núcleo, la partida en red y las comprobaciones de Godot de sprites, mapas, efectos y combinaciones.

Se mantiene el protocolo 9 y la orientación de marcha de v0.12.16.
