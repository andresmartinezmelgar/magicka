# Android: conservar la partida pausada al cambiar de aplicación

Rama `pruebas/supervivencia-procedural-autoguardado`, sobre `337390df25aaee84c4e80eda9e651068fa0de1c8`. Sin cambio de versión ni publicación.

## Comportamiento

- Al cargar o activar una partida se solicita la retención de su proceso mientras la actividad está reanudada. No se inicia un servicio nuevo desde segundo plano.
- Al pulsar Inicio, bloquear la pantalla o abrir otra aplicación se conserva la misma partida. El cliente deja de procesar fotogramas y física, suelta los controles y mantiene la pausa al regresar; no vuelve al launcher ni reinicia el mundo. La retención cubre también la carga y una reconexión ya iniciada.
- `ArcanaSessionService` vive en el mismo proceso que Godot y aumenta su prioridad mediante un servicio en primer plano. No tiene bucles, temporizadores, trabajo de red propio ni WakeLock. No mantiene encendida la pantalla ni simula la partida local en el fondo.
- La notificación de baja prioridad identifica la sesión abierta y la pausa local. Tocarla vuelve a la actividad existente. No produce sonido ni vibración. Al volver al menú, salir de la app o cerrar su tarea, se retiran el servicio y el aviso. `START_NOT_STICKY` evita reinicios automáticos después de un cierre.
- El juego conserva el autoguardado de Supervivencia como respaldo. El servicio reduce los cierres por prioridad de proceso; no impide un cierre forzado, una parada desde Android, un reinicio del teléfono o falta crítica de recursos.
- En multijugador conserva el cliente; la partida compartida sigue las reglas del servidor. La notificación online no afirma que el mundo remoto esté en pausa.

## Integración Android

Se registra el singleton Godot `ArcanaSession` y un servicio privado `specialUse`, con su descripción de uso y los permisos `FOREGROUND_SERVICE` y `FOREGROUND_SERVICE_SPECIAL_USE`. Se declara `POST_NOTIFICATIONS` sin abrir una solicitud de permiso al iniciar la partida. El servicio siempre proporciona su notificación; en Android 13 o posterior, su aparición en el panel depende del permiso del usuario y Android sigue mostrando el servicio en su gestor de aplicaciones activas.

Los fallos de arranque se registran mediante `android.session.failed`; no cierran el juego ni deshabilitan la pausa o el guardado. Solo se intenta recuperar la retención al volver a primer plano. La gestión nativa usa el hilo de la actividad y no altera la instalación ni los permisos de actualización.

Referencias oficiales:

- [Tipos de servicio en primer plano y specialUse](https://developer.android.com/develop/background-work/services/fgs/service-types).
- [Inicio de servicios y restricciones de segundo plano](https://developer.android.com/develop/background-work/services/fgs/launch).
- [Notificaciones de servicios y permiso en Android 13](https://developer.android.com/develop/ui/compose/notifications/notification-permission).

## Validación

Se amplía el smoke de guardado para comprobar retención durante la carga, identidad del mundo y tick al minimizar, pausa al regresar y liberación al salir al menú. Se añade `SessionRetentionPolicyTest`, incorporado al trabajo Android del workflow manual, para comprobar arranque solo en primer plano, ausencia de duplicados, conservación durante la pausa, salida y recuperación tras errores.

Comprobaciones sin compilar: integridad del diff, sintaxis shell, XML del manifiesto, registro del singleton, permisos y tipo de servicio, integración con carga/activación/salida, referencia del test en el workflow y metadatos de versión. El workflow continúa siendo manual y no se modifica el activador de publicaciones.

Por `AGENTS.md`, quedan pendientes la compilación Java/C#, las regresiones ejecutables y la prueba física Android 8, 13 y 14/15: cambiar de app, bloquear/desbloquear, volver por notificación o Recientes, salir al menú y cerrar la tarea. Comprobar también notificaciones permitidas/denegadas, pausa previa, carga interrumpida y conexión online. No se genera APK ni tag en este cambio.
