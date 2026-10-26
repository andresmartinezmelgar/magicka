# Ojos de poder mítico

Se activan cuando las seis ranuras están ocupadas por objetos míticos: botas, gorro, pecho, arma y los dos anillos. Solo cuenta el equipo seleccionado; los objetos guardados en el inventario no influyen. Quitar una pieza o usar una calidad inferior desactiva el efecto. No añade estadísticas ni exige un conjunto de 2/4 piezas.

Cada pieza aporta un sexto del color y del aspecto. Tres piezas de Vida, dos de Rayo y una de Fuego producen una mezcla dominada por el verde, con destellos eléctricos violetas y lenguas cálidas. El color cambia dentro del mismo campo de energía, con movimiento común y transiciones suaves.

| Familia | Aspecto |
|---|---|
| Vida | Fuego fatuo verde, suave y con respiración lenta |
| Escudo | Llama dorada/amarilla y núcleo compacto |
| Fuego | Lenguas naranjas, núcleo claro y movimiento más rápido |
| Agua | Corriente azul ondulada |
| Frío | Cian y blanco, con puntas de cristal |
| Rayo | Núcleo claro y pequeños quiebros violetas intermitentes |
| Arcano | Magenta, remolinos y trazos rúnicos |
| Tierra | Ámbar/ocre, con pequeñas motas |
| Mixta | Energía blanca azulada |

El efecto aparece en el inventario y en combate. Los ojos se anclan a cada una de las 80 poses de los cinco magos. De frente se muestran dos; de perfil, el visible; de espaldas y al morir, ninguno. Sigue la marcha, el lanzamiento, el desplazamiento vertical del cuerpo y el tamaño del retrato.

En combate, ambos ojos comparten una sola malla por mago. Las mallas de las poses se preparan al cargar los atlas; el material se precalienta durante la carga del mapa. El movimiento lo ejecuta el shader y no crea partículas ni luces dinámicas. Inventario y combate comparten la misma forma y mezcla. Las familias se transmiten en 36 bits junto con las calidades de los anillos, sin identificadores de objetos ni datos privados del inventario.

Protocolo 37. La comprobación visual automatizada cubre cinco magos, poses, orientación, mezcla, inventario, anillos y muerte; se ejecutará en la próxima compilación solicitada.
