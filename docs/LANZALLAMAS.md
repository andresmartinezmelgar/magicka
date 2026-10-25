# Lanzallamas

El chorro de fuego usa una emisión de pequeñas lenguas animadas en el espacio
del escenario. Cada una avanza hacia el extremo del ataque, se ensancha,
se curva y se desvanece. Las llamas ya emitidas conservan su trayectoria al
girar el mago; al soltar el ataque dejan de nacer y las restantes se apagan.

La malla y el shader son propios. Los vídeos de otros juegos se usaron como
referencia visual; no se incluyen imágenes, vídeos ni texturas de esos juegos.

## Integración

- Solo sustituye el aspecto de `SpellShape.Spray` con fuego sin agua ni frío.
  El agua, el frío, el vapor, el rayo y el arcano tienen sus propios efectos
  y una alternativa de menor coste; véase [Efectos adaptativos](EFECTOS-ADAPTATIVOS.md).
- `atlases.json` define los veinte puntos del báculo: cinco magos y cuatro
  orientaciones. El origen se transforma junto al sprite y a su anclaje de pies.
  Un emisor sin sprite de mago utiliza el origen recibido en el estado de combate.
- Cada lengua tiene tres láminas cruzadas para conservar volumen desde la
  cámara del juego. La apertura procede de `SpellBalance.ChannelConeDot` y
  los extremos se limitan al alcance recibido del servidor, con margen para
  el tamaño y la disipación de las llamas.
- Se usa un único lote de instancias y un material compartido por sistema de
  efectos. Las llamas forman parte del límite existente de 320 partículas y
  64 emisiones por fotograma; no constituyen un presupuesto adicional.
- Perfil nuevo: 45 emisiones por segundo y por autor, con tres partículas por
  carga en cada emisión. Perfil anterior: 30 emisiones por segundo, conservando
  el lanzallamas aprobado en 0.29.18, sin recuperar el antiguo cono visible.
- Máximo de 192 partículas elementales activas y 32 nuevas por fotograma,
  compartido con hielo, agua y vapor; 128 en el perfil anterior del fuego.
  `DetailScale` reduce ambas cifras. Los emisores se atienden por turnos.
- No cambia la simulación, los daños, los estados, las colisiones, el protocolo
  ni los límites de alcance. El efecto se prepara durante la carga y se limpia
  al salir o cambiar de mapa.

## Validación

La comprobación visual de efectos incluye cargas 1/2/3, persistencia al girar,
corte y caducidad, límites de alcance visual, reutilización de objetos, 128
emisores simultáneos, detalle reducido y separación de las familias de agua,
frío y vapor. Comprueba también los veinte anclajes y la eliminación del cono
visible para fuego.

La compilación y la ejecución de estas comprobaciones en Godot se realizan
con la publicación manual de pruebas solicitada por el usuario. Las lecturas
de buffers gráficos se verifican con viewport real en escritorio y móvil;
en modo headless se comprueban cantidades, límites, caducidad y limpieza.
La revisión manual debe comprobar el resultado en las cuatro orientaciones,
con movimiento, en PvP, Prácticas y Supervivencia, y que el chorro no tape al
mago ni atraviese el escenario.
