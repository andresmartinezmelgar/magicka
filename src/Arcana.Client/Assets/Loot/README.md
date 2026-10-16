# Recogidas de Defensa

Atlas SVG propio, de 768 × 256, con seis columnas y dos filas de 128 × 128.
Las siluetas tienen contorno oscuro y luces para distinguirse sobre el suelo y a tamaño móvil.

| Fotograma | Recogida |
| --- | --- |
| 0–3 | Botas, gorro, pechera y bastón, en orden de `EquipmentSlot` |
| 4–10 | Espada, escudo, hoja con flecha, corazón, ala, rayo y cruz, en orden de `BuffKind` |
| 11 | Grabado de ambas caras de la moneda |

`DefenseLootVisual` comparte el atlas y las mallas entre todas las recogidas.
El equipo y las mejoras miran a cámara; el halo del equipo utiliza el color de su calidad.
La moneda tiene canto de malla y dos caras verticales, y gira únicamente sobre Y.
Todos flotan suavemente; ninguna recogida necesita luces, partículas ni procesos independientes.

`--defense-smoke` captura las doce variantes y dos momentos del giro, y comprueba
la visibilidad por propietario y la retirada al desaparecer del snapshot.
