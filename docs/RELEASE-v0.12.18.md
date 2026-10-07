# v0.12.18 — Deslizamiento y rodeo de obstáculos

Al mantener las flechas, el joystick o un destino de movimiento, el mago rodea las columnas aunque llegue justo de frente. Las paredes rectas se siguen hacia un extremo que tenga espacio, prefiriendo el próximo. Las paredes oblicuas usan su propia superficie de contacto y las uniones de varios segmentos mantienen el lado de rodeo.

## Movimiento

- Barrido continuo del círculo del mago contra pilares, segmentos de muro y fronteras. Se avanza hasta el primer contacto y se utiliza el desplazamiento restante sobre la superficie.
- Asistencia tangencial de hasta el 65 % de la intención de caminar cuando el choque frontal deja una proyección demasiado pequeña. El presupuesto de movimiento nunca aumenta y la intensidad parcial del joystick se conserva.
- Lado de rodeo estable durante el contacto. La intención lateral del jugador, detenerse o cambiar de dirección permiten salir de la asistencia.
- Margen de 0,006 unidades y hasta cinco contactos por paso para conservar el suelo y las colisiones de esquinas. Un cierre completo del pasillo sigue bloqueando el avance hasta abrir un paso.
- El empuje solo usa el deslizamiento físico; no recibe asistencia de rodeo. Congelarse elimina la intención de caminar. Reaparecer, usar Traslación o atravesar un portal reinicia el estado de contacto.
- Simulación compartida por práctica y servidor, con los mismos controles y protocolo 9.

## Comprobaciones

Quince regresiones nuevas cubren entradas frontales y diagonales, ambos lados del pilar, todos los pilares de los cinco mapas, paredes planas y oblicuas, extremo próximo o cerrado, uniones de segmentos, apertura del pasillo, esquinas, parada, inversión del mando, joystick parcial, congelación, empuje, movimiento al cursor y un muro ambiental que sella el paso. Los objetos aleatorios tienen semilla fija; las pruebas respetan los modificadores de velocidad de los objetos y desactivan los portales al medir el recorrido de los pilares.

- 128 pruebas del núcleo correctas, incluidas las regresiones de combate, bots, navegación y capturas anteriores.
- 20 pruebas de red correctas con servidor protegido y clientes reales.
- Godot 4.5.2 .NET: `sprites-smoke` correcto, con interpolación, marcha, orientación, lanzamiento y entrada a 30/60/120 FPS.
- `maps-smoke` correcto en los cinco mapas, con portales, círculos de captura, objetos y limpieza.
- Compilación C# de núcleo, pruebas y servidor en Release, y cliente Godot en Release y Debug, con Roslyn del SDK .NET 8.0.400, sus referencias y los generadores de Godot 4.5.2.
- `git diff --check` correcto.

La comprobación local usó Roslyn directamente porque MSBuild falla al consultar procesos en el montaje `/proc` de este entorno de ejecución. No se ha modificado la configuración de compilación para sortearlo. Persiste el aviso de nulabilidad anterior de `Main.cs`, línea 98.
