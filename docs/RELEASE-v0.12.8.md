# Arcana Arena v0.12.8

Escudos y barreras muestran la intensidad de la combinación. Una piedra y escudo conservan el muro actual; dos piedras y escudo forman una pared con tres hiladas, mayor grosor y contrafuertes. Las superficies de fuego, agua, hielo y electricidad se adaptan. El escudo personal añade una banda con dos elementos y dos con tres, incluidos los repetidos; se atenúan al recibir daño. Los hechizos de un elemento conservan su aspecto.

En todos contra todos, cada entrada, reaparición y reinicio distribuye a los magos por posiciones aleatorias de los cuatro mapas. Se evita aparecer sobre muros, obstáculos, jugadores y campos peligrosos cuando hay espacio. Durante un segundo el mago queda protegido contra daño, estados y empujes; una envoltura dorada y el texto INMUNE muestran la protección.

Duelo y guerra conservan las bases por equipo. Capacidad de escudos, vida de muros, daño y alcance mantienen sus valores. El protocolo 8 incorpora campos opcionales para la intensidad del escudo y la invulnerabilidad; los clientes anteriores pueden conectar, pero no dibujan estas señales. Las apariciones nuevas necesitan un servidor v0.12.8.

El movimiento y el contraste en móvil se han evaluado y quedan pendientes de decisión. El código conserva los scripts `Compilar-Juego.cmd` y `Compilar-APK.cmd` para generar los archivos localmente.

## Comprobaciones

- Distribución y separación en los cuatro mapas, cambios de semilla y bases de los modos por equipos.
- Un segundo exacto de protección al entrar y reaparecer, con bloqueo de daño, estados y empujes.
- Replicación de cargas repetidas y rechazo de datos de protección inválidos.
- Volumen de las mallas reforzadas, bandas de escudo y desaparición del halo en Godot.
- Vista OpenGL `shield-strength`, junto con las escenas existentes de barreras y meteorito.
