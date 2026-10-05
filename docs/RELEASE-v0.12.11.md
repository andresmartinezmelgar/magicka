# Arcana Arena v0.12.11

Después de morir y reaparecer, el cliente descarta el destino anterior del ratón y los dedos del joystick. Un clic mantenido no reactiva ese destino. La siguiente orden parte de la nueva posición, incluso si no llegó el snapshot de muerte. Mago y cámara se ajustan a esa posición en todos los modos.

El escudo básico conserva un amarillo más saturado con retícula, superficie visible y borde oscuro. Puntos de captura, minas y otros campos del suelo tienen rellenos más definidos, anillos con contorno y etiquetas legibles. La paleta de PC y móvil y la iluminación más neutra separan los efectos del terreno.

`Compilar-APK.cmd` extrae y comprueba las herramientas Android directamente en `cmdline-tools/latest`, creando cada directorio. Repara instalaciones incompletas y vuelve a descargar ZIP dañados. Conserva la firma local, las preferencias y la APK anterior si la compilación falla.

Los scripts de compilación muestran la consulta conocida de EditorSettings al cerrar Godot .NET como aviso; siguen rechazando otros errores, incluso si Godot devuelve código cero, y verifican las salidas. Referencia del motor: https://github.com/godotengine/godot/issues/84839.

Comprobaciones de reaparición con ratón y controles táctiles, pérdida del estado muerto y cámara en los tres modos. Pruebas de extracción sin directorios en el ZIP, rutas con espacios, reparación y fallos en Windows PowerShell 5.1. Revisión OpenGL del contraste sobre hierba y piedra.

Código y scripts disponibles en `main`. Protocolo 8.
