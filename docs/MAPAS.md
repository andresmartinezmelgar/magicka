# Escenarios

| Mapa | Entorno |
|---|---|
| Arena clásica | Claro de bosque, caminos de tierra, árboles de copa redondeada, hierba, flores y cercado de madera. Los obstáculos centrales tienen apariencia de árboles. |
| Ciudadela de los pasillos | Calles adoquinadas y plazas rodeadas de edificios con fachadas, tejados, chimeneas, puertas, ventanas, toldos y cajas. Columnas y faroles ocupan los obstáculos existentes. |
| Coliseo de la ascensión | Ruinas de piedra con un anillo grabado, musgo, pilares cubiertos de vegetación y árboles alrededor. Los catorce objetos aparecen en posiciones aleatorias y cambian de sitio al reaparecer. |
| Santuario de las reliquias | Bosque con menhires, cristales, flores y senderos. Conserva catorce pedestales simétricos con objetos acumulables hasta morir. |
| Frente de conquista | Pasillo de ruinas de 480 unidades, con ocho tramos desplazados, pasos estrechos, cinco círculos de captura y tres pares de portales. Meteoros aleatorios avisados y muros de piedra destructibles. |

Los escenarios usan mallas y materiales procedurales propios. La iluminación y las superficies cambian por mapa. La hierba y las flores se agrupan con MultiMesh para reducir llamadas de dibujo.

Las casas y los árboles exteriores se colocan fuera de los rectángulos de suelo, dejando libres las rutas jugables. La representación de obstáculos utiliza las posiciones y radios compartidos con el servidor. Ciudadela exige quince segundos de control total; Frente de conquista exige los cinco puntos en orden y mueve la reaparición con el terreno; los otros mapas usan tickets en Guerra.

`--maps-smoke` comprueba cambios entre los cinco mapas, escenario, cámara, minimapa, portales, círculos sin etiquetas y la actualización de posición de objetos. La CI dibuja además las cinco vistas con OpenGL y guarda las capturas en el artefacto `map-review` antes de publicar.
