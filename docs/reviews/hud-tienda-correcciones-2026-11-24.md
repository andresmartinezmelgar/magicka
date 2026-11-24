# Correcciones del HUD y la tienda

Base: `5e1cf040f82ebf0530cdf735dd7ffd42c6904b4d` (versión publicada 0.29.74).

## Superposiciones

Al terminar una partida, el cierre de la tienda de Supervivencia emitía un cambio incluso estando cerrada. Su manejador volvía a colocar todo el panel por delante del resumen personal, incluidos los seis ítems del HUD.

El cierre repetido ahora no emite un cambio; el panel solo se eleva al abrir la tienda. Además, la visibilidad del equipo y los avisos se sincroniza con los resúmenes y menús en móvil y escritorio. Abrir el resumen los oculta aunque el jugador ya haya muerto. Cerrar el resumen recupera los controles permitidos por la pantalla actual.

El aviso de botín utiliza los límites actuales del bloque de conexión y del estado del jugador. Se actualiza cuando cambian sus dimensiones, reserva espacio bajo ambos si comparten columna y se coloca encima cuando falta altura. El aviso tiene prioridad visual sobre el HUD de base. Su duración queda suspendida mientras lo oculta un menú; se conserva el cierre mediante clic o toque.

## Compras y espacio vertical

La celebración se crea después de que el servidor confirme la compra y de comprobar que sigue activa la misma sesión. Incluye el nombre de la pieza, una tarjeta con el color de calidad y 18 estrellas animadas durante 1,65 segundos. Los controles ignoran los gestos, se reutiliza el buffer de vértices y el efecto se elimina al terminar. Una compra nueva sustituye el efecto anterior.

El área de atributos de cada oferta pasa de 120 a un mínimo de 180 píxeles en móvil y de 168 a un mínimo de 224 en escritorio, adaptándose a la altura disponible. En móviles de menos de 600 píxeles de alto, la cabecera conserva el saldo y Consultar y oculta su decoración. Se reduce el margen vertical y se elimina el mensaje normal redundante del catálogo en móvil; los errores y estados de compra permanecen visibles.

## Comprobaciones

- Análisis sintáctico C# de los 15 archivos de interfaz afectados, incluido el nuevo efecto, y revisión de espacios del diff.
- Preparadas regresiones en las verificaciones de navegación: cierre redundante de tienda con resumen abierto, equipo oculto/restaurado, aviso separado de un bloque de red de tres líneas y conservación del aviso oculto por un resumen.
- Preparadas regresiones en las verificaciones de tienda: celebración tras compra confirmada; ausencia al cargar ofertas, abrir compras desde caché o rechazar una compra; efecto sin captura de gestos y tarjeta dentro del viewport.

No se ha compilado ni ejecutado Godot para estos cambios. Las regresiones están pendientes de ejecución y las comprobaciones de sintaxis no sustituyen la revisión visual. `AGENTS.md` reserva compilaciones y exportaciones para una petición expresa; no se genera una APK ni se modifica la versión publicada en esta tarea.
