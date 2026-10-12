# Arcana Arena 0.13.1

## Equipación y estadísticas

Las cuatro posiciones tienen flechas izquierda/derecha. Recorren los objetos de esa posición, permiten seleccionar **Sin equipo** y pasan del final al inicio. Pulsar una pieza muestra sus atributos individuales con valor actual, mínimo y máximo. Los botones de las otras posiciones mantienen sus elecciones.

La tabla de equipo muestra **Piezas**, **Conjunto**, **Suma** y **Aplicado** para los once atributos. La suma conserva tanto los bonos positivos como las penalizaciones. El valor aplicado utiliza los mismos límites que la simulación. Los conjuntos Míticos siguen dando un bono con dos o tres piezas y uno mayor con cuatro; el mayor sustituye al anterior.

Cada cambio se guarda en la cuenta autenticada. Para práctica sin conexión se guarda inmediatamente en el perfil local, antes de volver al launcher. El perfil se recupera al arrancar y se aplica en las siguientes partidas contra IA y en el laboratorio. Morir, reaparecer o reiniciar la ronda conserva el equipo. Cerrar sesión elimina la caché de la cuenta en el dispositivo incluso si falla la petición de cierre al servidor.

Se han retirado los adornos de equipación añadidos al mago. Los atlas, colores de equipo, animaciones y orientación permanecen como estaban. Las calidades y familias se indican en el selector mediante texto.

## Firebase y servidor

No se ha creado ninguna cuenta ni proyecto Firebase; el repositorio no incluye usuarios ni contraseñas de Google. La integración sigue desactivada hasta que el propietario configure su proyecto. El alojamiento y la configuración del servidor no se han modificado. La estructura de Firestore y los IDs de objetos se conservan entre versiones.

## Validación local

- Compilación de Core, Server, pruebas generales y pruebas de cuentas en Release, y cliente en Debug y Release, con .NET 8.0.400 y Godot 4.5.2 .NET. Se utilizó el compilador oficial Roslyn y los generadores de Godot porque el arranque de MSBuild en este contenedor falla al consultar los datos del proceso. Los scripts de compilación del usuario mantienen su flujo habitual.
- 169 pruebas de reglas, combate, equipo, serialización, transporte y diagnóstico: sin fallos.
- 13 pruebas con el emulador oficial Firestore y servidor real local: sin fallos. Incluyen recuperar las cuatro posiciones desde otra instancia y una nueva sesión, intervalos intactos y propiedad validada.
- Godot: flechas reales en las cuatro posiciones y cinco calidades, vuelta circular y posición vacía, valores individuales y totales, caché, dos partidas contra IA y sprites sin adornos. Ejecutado con interfaz de escritorio y móvil.
- Nuevo proceso de Godot: recupera al arrancar el gorro elegido previamente y aplica todo el equipo a otra partida contra IA. El script usa un perfil temporal aislado para que la prueba no altere otros escenarios.
- Pruebas existentes de sprites, dirección, movimiento, reaparición y laboratorio de práctica: sin fallos.
- Revisión visual con OpenGL de la pantalla de equipo a 1440×900 y de su disposición móvil a 1600×720. No sustituye una prueba en un teléfono físico.
- Metadatos y YAML de CI comprobados: versión 0.13.1, Android 13001 y protocolo 12. Pruebas del soporte de versiones: seis correctas y dos omitidas por falta de PowerShell y por la limitación local de la CLI .NET.

Esta validación no crea una APK, ejecutable Windows ni cuenta Firebase de producción. Las exportaciones y la publicación utilizan el flujo de GitHub Actions del repositorio.
