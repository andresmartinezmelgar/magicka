# Mejoras de seguridad y rendimiento — preparación 0.29.70

Estado del código a 20 de noviembre de 2026. Rama `pruebas/seguridad-rendimiento-integral`, basada en `1e009fb613f8608b43ccb87542feeb99049c5d52` (0.29.69). Esta preparación no es una versión publicada. No se ha compilado, creado APK, ejecutado CI ni modificado Firebase o la Raspberry.

## Estado de las veinte mejoras

«Implementado» significa incorporado al código y preparado para revisión; la validación que requiere compilar sigue pendiente. No implica despliegue ni una mejora de FPS medida.

| # | Mejora | Estado y límite |
|---|---|---|
| 1 | Cerrar escrituras anónimas directas de estadísticas | Implementado: Firestore rechaza escrituras de todos los clientes. El invitado envía JSON validado al backend HTTPS con cuotas y diario; no recibe credenciales Admin. Activar primero el backend compatible y actualizar clientes antes de publicar estas reglas. Las cifras locales siguen siendo telemetría no fiable y nunca conceden premios. |
| 2 | Retirar .NET 8 | Parcial: servidores, contenedores y pruebas de cuentas pasan a .NET 10; SDK fijado a 10.0.404. Cliente, núcleo y pruebas de núcleo siguen en net8.0 por la compatibilidad de Godot 4.5.2 y los fallos de cierre previamente registrados con runtime 10. Falta migrar el cliente a una combinación de motor/runtime comprobada. |
| 3 | Cuotas HTTP por IP y cuenta | Implementado: máximo 64 peticiones HTTP simultáneas; cuotas por IP de transporte o proxy exacto confiable y por UID verificado. Variantes de mayúsculas y barra final comparten cuota. Las ventanas se acotan a 4096 claves hash en memoria. WebSocket mantiene sus límites específicos. |
| 4 | Proteger la creación de salas | Implementado: Google obligatorio si el servidor tiene cuentas activas; seis creaciones/minuto/IP, dos generaciones simultáneas y generación fuera del bloqueo de simulación. LAN sin Firebase conserva invitados. |
| 5 | Evitar que un cliente altere la dificultad con sus FPS | Implementado: el servidor usa su tiempo de simulación para limitar enemigos; los FPS remitidos solo se observan. El ajuste local se conserva en partidas locales. |
| 6 | Dos contenedores y retirada progresiva | Implementado en código: candidato sano antes del cambio de nginx; bloqueo de nuevas partidas; reconexión por versión; retirada solo sin conexiones, reservas ni diarios pendientes; recuperación de cambios interrumpidos. Requiere la instalación descrita más abajo y validación real en Raspberry. |
| 7 | Cola persistente de estadísticas | Implementado: máximo 128 informes por proceso (límite configurable en constructor hasta 512), coalescencia por partida, escritura atómica con fsync, recuperación al iniciar y borrado después de confirmación. Un informe fallido no bloquea los demás. Una cola llena rechaza nuevos informes de partida; no crece sin límite. |
| 8 | Sacar los diarios del hilo de simulación | Implementado: recompensas y bestiario se guardan con tareas fuera de la simulación. El hito solo se marca guardado cuando termina su escritura; se esperan escritores al cerrar. |
| 9 | Reducir lecturas de inventario tras cada venta | Implementado: caché de diez segundos y 128 cuentas, validada con UpdateTime de Firestore. Una venta actualiza la copia local usando su recibo del mismo commit. Otra instancia o un cambio de cabecera invalida la caché, aunque el saldo coincida. Las ventas automáticas siguen siendo secuenciales para volver a comprobar favoritos y Míticos. |
| 10 | Reducir trabajo y copias de los deltas JSON | Implementado: se evita comparar árboles completos antes de recorrerlos otra vez y se fusiona sobre una copia privada de la base sin clonar cada subárbol. Falta comparar tiempos y asignaciones en una compilación. |
| 11 | Reutilizar la selección visible del renderizador | Implementado: reutilización entre renders de la misma instantánea y ventana de cámara. Una instantánea o cámara nueva invalida la copia. No elimina todo el filtrado ni todas las asignaciones. |
| 12 | Autoguardado fuera del hilo principal | Implementado: captura del mundo en Godot; validación, respaldo y escritura en una tarea; un escritor a la vez. El guardado manual espera el anterior para mantener el orden. La muerte terminal descarta el archivo después del escritor pendiente. Falta medir los tirones en Android. |
| 13 | Límites de recursos Docker | Implementado en Compose: 768 MiB, 2 CPU, 256 PID, logs de 10 MiB × 3 y cierre de 30 s; CPU y RAM ajustables mediante entorno. Requiere desplegar y dimensionar dos instancias con RAM real de la Raspberry. |
| 14 | Consultas del dashboard y un registro por partida | Implementado: límite superior e inferior de día y documento canónico runId. Se incluye compactación administrativa de revisiones antiguas con simulación previa y lotes acotados. No se ha ejecutado sobre datos reales. Sigue el máximo de 500 informes por consulta pública. |
| 15 | Retención de telemetría | Implementado, desactivado por defecto: servicio opcional borra datos con más de 30 días de matchStatistics y dashboardTelemetry, hasta 100 documentos por colección y hora. No borra recibos, oro ni inventario. Se añade expiresAt para TTL opcional, sin habilitarlo. |
| 16 | Eliminación de cuenta y datos | Implementado: botón en Perfil, confirmación explícita, Google autenticado en los últimos cinco minutos, solicitud persistente e idempotente y reintentos por cuenta. Borra perfil y subcolecciones, rol legacy y telemetría del propietario, anonimiza su UID en archivos de partidas antiguas y elimina el usuario de Auth. Requiere permisos IAM de Firebase Auth y Firestore. No se ha borrado ninguna cuenta real. |
| 17 | Fijar acciones y verificar herramientas | Implementado: acciones existentes por SHA y Dependabot; SHA-256/SHA-512 de Godot, plantillas, Android CLI, Gradle, SDK .NET y JDK Temurin 17.0.21+9 antes de extraer nuevas descargas. Herramientas ya instaladas o proporcionadas manualmente no se vuelven a descargar. Falta ejecutar los scripts de Windows en Windows. |
| 18 | Enfriamiento de claves AdMob | Implementado: un refresco por claves desconocidas cada cinco minutos, también tras fallos de red; claves válidas en caché doce horas. Claves temporalmente ausentes o descargas fallidas generan 503 reintentable; no conceden recompensas. |
| 19 | Compra real de la opción sin anuncios | No implementada: faltan producto, precio y publicación de Google Play, cuenta comercial, acceso a Play Developer API y credenciales de verificación. No se ha inventado una compra ni habilitado un cobro. adsRemoved sigue siendo una configuración protegida que solo cambia Admin/backend. |
| 20 | Guía de enemigos en 640×450 | Implementado: retrato y estadísticas en paralelo, retrato reducido en pantallas bajas y nombre con salto de línea; prueba 640×450 añadida al flujo manual. Falta ejecutarla sobre la nueva compilación y revisar capturas Android. |

## Comprobaciones realizadas

- Reglas Firestore y dashboard ejecutadas en emulador local: correctas, incluidos intentos de modificar roles/pagos, escrituras de estadísticas, propiedad de objetos y bloqueo tras solicitud de borrado.
- Dashboard JavaScript: 25 tests correctos.
- Actualizador existente: 19 tests Python correctos.
- Despliegue progresivo: 10 pruebas Python correctas de transiciones, candidatos fallidos, reconexiones, recibos y recuperación; no acceden a Docker o nginx reales.
- Verificación de hashes: 2 pruebas correctas de rechazo de archivos corruptos o desconocidos y soporte SHA-512.
- Análisis sintáctico C# de 48 archivos mediante Roslyn, sin errores y sin generar ensamblados, comprobación de YAML/XML y diferencias de Git. El análisis sintáctico no sustituye la resolución de tipos ni la compilación.

Se incorporan pruebas C# de diario, prioridad de estadísticas del servidor, deltas, coherencia de caché entre instancias, HTTP, borrado y AdMob. No se han ejecutado porque necesitan compilar y AGENTS.md reserva esa acción a una petición expresa. Tampoco se han medido FPS, batería, memoria o latencias de esta preparación.

## Activación de Firebase

1. Después de compilar y verificar, desplegar el backend compatible con /analytics/matches y actualizar clientes. Comprobar que la cuenta de servicio tiene acceso Admin a Firestore. Los clientes antiguos que escriben estadísticas directamente dejan de hacerlo al aplicar las reglas nuevas; necesitan actualizarse.
2. Publicar firebase/firestore.rules y conservar los índices del repositorio. Ningún UID real de administrador aparece en estas reglas o en el cliente. Mantener los tres roles existentes en users/{uid}/configuration/account.
3. La eliminación de cuenta necesita que la cuenta de servicio pueda deshabilitar y eliminar usuarios de Firebase Auth, revocar sesiones y borrar datos de Firestore. En el servicio legacy separado, estas funciones se ejecutan allí; en el servidor integrado, en el mismo proceso.
4. La solicitud accountDeletions/{uid} revoca acceso inmediato según reglas y backend. Se conserva su UID, estado y fechas como marca mínima para que un recibo pendiente no recree la cuenta. Los nuevos archivos globales de recompensas ya omiten UID; las estadísticas anónimas no tienen vínculo al propietario.
5. Para retención automática, configurar ARCANA_STATISTICS_CLEANUP_ENABLED=true únicamente tras decidir conservar 30 días. El valor predeterminado false no borra datos históricos. No se configura TTL ni un servicio de pago automáticamente.
6. Compactar registros antiguos primero en simulación, desde un contenedor ya actualizado y conectado al proyecto correcto:

```sh
docker compose exec server dotnet Arcana.Server.dll --statistics-maintenance
```

Con --apply se escribe el documento canónico antes de borrar la revisión antigua. Un informe local nunca sustituye uno autoritativo. Cada ejecución revisa como máximo 10000 documentos; si alcanza el límite imprime --after ID para continuar. El modo de simulación no escribe en Firebase. Este procedimiento no se ha ejecutado sobre producción.

## Activación de la Raspberry y nginx

El contenedor 0.29.69 no tiene los endpoints de ciclo de vida. Hace falta una primera instalación compatible en una ventana sin partidas. El actualizador anterior se mantiene disponible si no se configura rolling_proxy. El modo progresivo no está activo por guardar este código.

- Generar una clave aleatoria de al menos 32 caracteres, guardarla en un archivo 0600 del servidor y configurar el mismo valor en ARCANA_DEPLOYMENT_KEY del primer contenedor. No subirla al repositorio.
- Mantener GOOGLE_APPLICATION_CREDENTIALS y la configuración Firebase como montajes de solo lectura. /app/data debe ser un volumen Docker propio de cada versión. Los bind mounts de diarios y los volúmenes externos compartidos se rechazan.
- Incluir deploy/nginx/arcana-routing.conf en el bloque http y deploy/nginx/arcana-locations.conf en el server HTTPS existente, conservando dominio, certificados y puertos. Inicializar /var/lib/arcana-server-updater/upstream.conf con default 127.0.0.1:27015; y restringir las escrituras a root. Verificar nginx -t antes de recargar.
- Confiar solo en la IP exacta del proxy mediante ARCANA_TRUSTED_PROXY_IP en el primer contenedor. Las instancias nuevas detectan el gateway IPv4 de Docker y publican únicamente 127.0.0.1:28015 o 127.0.0.1:28017. No abrir esos puertos en el router.
- El proxy sustituye X-Forwarded-For por la IP de transporte; /internal/ responde 404 desde el dominio público. El actualizador consulta /internal/lifecycle y /internal/drain únicamente por loopback con la clave.
- Instalar el actualizador con --rolling-proxy RUTA_MAPA --deployment-key-file RUTA_CLAVE junto a su --checkout y --compose-file habituales. Los archivos deben existir antes de la instalación. Se mantiene el temporizador de 30 minutos.
- El candidato arranca y verifica su versión antes de poner en retirada el antiguo. Las conexiones WebSocket establecidas sobreviven a la recarga de nginx; X-Arcana-Version conserva el destino de las reconexiones. Los clientes nuevos deben tener una versión distinta en VERSION.
- Una serie PvP ya iniciada puede terminar sus rondas. La versión en retirada no reinicia Supervivencia ni otra partida tras el resultado. Las conexiones en la pantalla final cuentan hasta que sus clientes salgan; después se espera también a las reservas y recibos pendientes. Una tercera versión se aplaza mientras exista una antigua sin vaciar.
- El estado persiste antes de cambios de ruta o retirada. Los casos ambiguos conservan contenedores para no cortar una partida. No se borran volúmenes; si falla una recuperación o no responde un contenedor, hace falta revisar ese estado antes de continuar.

No hay acceso SSH autorizado/configurado a la Raspberry ni credenciales Admin del proyecto disponibles en esta sesión; no se han aplicado estos pasos.

## Compra real sin anuncios

Quedan por crear el producto de compra única y su precio en Google Play, integrar Billing en Android, validar cada purchaseToken en backend mediante Play Developer API, confirmar la compra, restaurar compras y procesar revocaciones/reembolsos antes de mantener adsRemoved. Se necesita acceso al proyecto y distribución de prueba de Play para validar la compra de extremo a extremo. El interruptor administrativo actual no demuestra un pago.

## Validación antes de publicar

Tras autorización expresa de compilación: ejecutar las suites de núcleo y cuentas con .NET 10/8 según sus proyectos, las reglas en emulador, las pruebas de red y reconexión y los smoke tests gráficos (incluido 640×450). Comprobar servidor Linux ARM64, scripts Windows, cierre de Godot, autoguardado Android al cambiar de aplicación, estado de anuncios tras fallos de red, recuperación de diarios y prueba real de nginx con dos partidas/versiones. Solo después generar los artefactos o una release.
