# Arcana Arena v0.12.14

- Puntos de control con los seis círculos mágicos aportados: fuego, hielo, naturaleza, arcano, solar y cristal. Texturas transparentes, colores originales y variantes por mapa.
- Eliminados el disco oscuro, el borde de contraste y el cristal central de los puntos. El anillo luminoso indica el progreso real de azul o rojo; las disputas pulsan en brillo sin cambiar el porcentaje y la etiqueta conserva equipo y estado.
- Compilación Android en Windows con consola heredada para responder a cada licencia. Una consulta final al mismo SDK confirma que todas están aceptadas; se detiene antes de instalar paquetes o exportar si quedan pendientes, aunque `sdkmanager` devuelva cero.
- `-AcceptAndroidLicenses` sigue siendo una opción explícita. No se aceptan términos nuevos durante la consulta de comprobación.
- Regresiones de captura, neutralización, disputa y limpieza en Godot; aceptación y comprobación de licencias con rutas que contienen espacios en Windows PowerShell 5.1.

Solo se actualiza el código y sus recursos en `main`. La publicación de una Release requiere ejecutar expresamente el flujo de publicación.
