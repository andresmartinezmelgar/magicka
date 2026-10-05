# Efectos por número de cargas

La intensidad se calcula con los elementos del hechizo enviado por el servidor. Cada elemento ocupa una carga, incluido repetir el mismo elemento. Por ejemplo, S, SS y SSS producen los tres niveles de detalle. El tiempo de mantener una roca cargando conserva su efecto en la simulación; el número de elementos determina estos detalles visuales.

| Representación | 1 carga | 2 cargas | 3 cargas |
|---|---|---|---|
| Lanzamiento | 4 partículas breves | 7 partículas | 11 partículas |
| Impacto | 6 partículas | 11 partículas | 17 partículas |
| Estela de proyectil | 1 partícula por emisión | 2 partículas y un aro fino | 4 partículas y dos aros finos |
| Canalización | 1 partícula por emisión | 2 partículas | 3 partículas |
| Rayo eléctrico | Trazo principal | Una ramificación corta | Tres ramificaciones cortas |
| Spray y fuerza | 4 trazos interiores | 7 trazos | 11 trazos |
| Rayo continuo | Núcleo y envolvente tenue | Dos hebras y un acento móvil | Tres hebras y cuatro acentos móviles |
| Nova y explosión | Una onda breve | Más partículas con una onda | Mayor densidad y una segunda onda interior |
| Muros y áreas | Superficie simple | Marcas discretas | Más marcas discretas |

El color principal sigue identificando el hechizo. El detalle aumenta por densidad, hebras y formas compactas; la emisión luminosa se mantiene moderada. Las partículas de los lanzamientos duran entre 0,28 y 0,48 segundos. Las ondas terminan en menos de 0,6 segundos. Golpes continuos, muerte y rotura de escudo usan respuestas breves independientes del número de elementos.

El límite por renderer es de **320 partículas**, **24 ondas** y **64 partículas nuevas por fotograma**, con reutilización de nodos. Los rayos conservan un máximo de 512 segmentos. Las emisiones de estela se espacian 0,065 segundos. Los radios de impacto, daño, curación y alcance pertenecen al servidor.

Mientras se mantiene un canal o una roca, su efecto usa `CastingElements` aunque se estén preparando otros elementos. La previsualización F1 aplica el mismo renderer.

`--effects-smoke` comprueba la progresión real de partículas y segmentos de las cuatro formas de canal, los elementos repetidos, los aros de proyectil, su radio original, el hechizo activo frente a la siguiente selección, saturación, caducidad y reutilización. `--preview-strength=1`, `2` o `3` genera escenas equivalentes para revisar las capturas de OpenGL en el artefacto `effect-review`.

En v0.12.5 el láser ofensivo también gana grosor y opacidad según su tiempo de canalización, del 30 al 150 % de daño original en 2 s. El número de elementos sigue determinando los detalles; la carga por tiempo no añade segmentos ni luces. La barra inferior se llena del 0 al 100 %. `--preview-spells=beam-charge` muestra cinco etapas equivalentes para revisar la progresión en OpenGL.

## Barreras combinadas y meteorito (v0.12.6)

Las barreras usan la receta del servidor para superponer llamas, agua descendente con espuma, arcos eléctricos, hielo o vapor. Los muros de tierra conservan la piedra bajo la superficie elemental. Las mezclas muestran ambos componentes; agua/frío se representa como hielo y agua/fuego como vapor. Se aplica al muro frontal y a cada segmento del anillo. La burbuja del escudo personal conserva su diseño.

El meteorito usa una roca irregular con grietas calientes, estela y brasas. La caída se sincroniza con los 1,4 s de aviso y el círculo representa el radio real de 4 m. La explosión añade 30 partículas de fragmentos, brasas y humo, dos ondas y un destello de 0,2 s. Usa los pools y presupuestos existentes, también al acumularse meteoritos y barreras.

En v0.12.7 la piedra forma dos hiladas de bloques irregulares, con juntas alternadas y caras rugosas. Las llamas tienen puntas y turbulencia ascendente; las cascadas muestran una cortina continua, reflejos descendentes y espuma con ondas en la base. Los arcos eléctricos se ramifican y producen chispas breves sobre la superficie. La mezcla conserva el volumen de la piedra y los componentes compatibles de la receta.

La roca del meteorito tiene 80 facetas, zonas de basalto y grietas que pulsan. Su estela combina una envolvente irregular y un núcleo caliente, orientados en sentido contrario al descenso, con brasas y humo. El radio, el aviso, el instante de impacto y los límites de efectos siguen definidos por los valores existentes.

`--preview-spells=shield-elements` y `--preview-spells=meteor` muestran estas escenas para capturas OpenGL. `--effects-smoke` recorre las 18 recetas que crean barreras, su desaparición y la caída, radio, impacto y saturación del meteorito. `--mobile-smoke` crea piedra con fuego y lanza el meteorito mediante los controles táctiles.
