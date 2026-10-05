# Arcana Arena v0.12.10

Traslación (tecla o botón 2) cancela el destino del clic anterior antes de enviar la magia. El cliente deja de reenviarlo después del salto y mantener pulsado el ratón no lo reactiva. El mago permanece en el punto de llegada hasta recibir una nueva orden de movimiento.

La navegación descarta la ruta calculada desde la posición previa al salto. Un nuevo clic o el joystick continúa desde la llegada. Celeridad y las otras magias conservan su tratamiento del movimiento.

Las comprobaciones Godot reproducen la tecla 2 y el botón táctil, el destino previo, un clic mantenido durante dos segundos, la parada y una nueva orden desde la llegada. El protocolo sigue siendo el 8.

El código de `main` y los scripts `Compilar-Juego.cmd` y `Compilar-APK.cmd` permiten generar los archivos localmente.
