# Escenarios

| Mapa | Entorno |
|---|---|
| Arena clásica | Claro de bosque, caminos de tierra, árboles de copa redondeada, hierba, flores y cercado de madera. Los obstáculos centrales tienen apariencia de árboles. |
| Ciudadela de los pasillos | Calles adoquinadas y plazas rodeadas de edificios con fachadas, tejados, chimeneas, puertas, ventanas, toldos y cajas. Columnas y faroles ocupan los obstáculos existentes. |
| Coliseo de la ascensión | Ruinas de piedra con un anillo grabado, musgo, pilares cubiertos de vegetación y árboles alrededor. Los catorce objetos aparecen en posiciones aleatorias y cambian de sitio al reaparecer. |
| Santuario de las reliquias | Bosque con menhires, cristales, flores y senderos. Conserva catorce pedestales simétricos con objetos acumulables hasta morir. |

Los escenarios usan mallas y materiales procedurales propios. La iluminación y las superficies cambian por mapa. La hierba y las flores se agrupan con MultiMesh para reducir llamadas de dibujo.

Las casas y los árboles exteriores se colocan fuera de los rectángulos de suelo, dejando libres las rutas jugables. La representación de obstáculos utiliza las posiciones y radios compartidos con el servidor. La geometría, las superficies y las condiciones de victoria se conservan: Ciudadela exige quince segundos de control total en guerra; los demás mapas usan tickets.

`--maps-smoke` comprueba cambios entre los cuatro mapas, escenario, cámara, minimapa y la actualización de posición de objetos. La CI dibuja además cuatro vistas con OpenGL y guarda las capturas en el artefacto `map-review` antes de publicar.
