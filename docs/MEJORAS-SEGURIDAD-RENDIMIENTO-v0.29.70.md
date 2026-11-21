# Seguridad y rendimiento de Arcana Arena 0.29.70

Versión de pruebas de la rama `pruebas/seguridad-rendimiento-integral`. El despliegue en Firebase y Raspberry se realiza por separado.

## Estado de las veinte mejoras

| # | Mejora | Estado y límite |
|---|---|---|
| 1 | Cerrar escrituras anónimas directas de estadísticas | Implementado: Firestore rechaza escrituras de todos los clientes. El invitado envía JSON validado al backend HTTPS con cuotas y diario; no recibe credenciales Admin. Activar primero el backend compatible y actualizar clientes antes de publicar estas reglas. Las cifras locales siguen siendo telemetría no fiable y nunca conceden premios. |
| 2 | Retirar .NET 8 | El juego, el núcleo, ambos servidores y las pruebas se compilan con .NET 10. El SDK es 10.0.404. La verificación local incluye el runtime Android ARM64 y el cierre del proceso Godot al salir de Supervivencia. |
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
| 19 | Compra única sin anuncios | Google Play Billing en Android. El servidor comprueba el producto, el estado y la cuenta, registra el derecho y confirma la compra. Hay restauración y revalidación al consultar o renovar la tienda. La validación con una compra real y su reembolso requiere una pista de Play y permisos de la cuenta del editor. |
| 20 | Guía de enemigos en 640×450 | Retrato y estadísticas en paralelo, altura adaptable y nombre en varias líneas. La prueba móvil del bestiario pasa a 640×450. |

## Comprobaciones realizadas

- Reglas Firestore y dashboard ejecutadas en emulador local: correctas, incluidos intentos de modificar roles/pagos, escrituras de estadísticas, propiedad de objetos y bloqueo tras solicitud de borrado.
- Dashboard JavaScript: 25 tests correctos.
- Actualizador existente: 19 tests Python correctos.
- Despliegue progresivo: 10 pruebas Python correctas de transiciones, candidatos fallidos, reconexiones, recibos y recuperación; no acceden a Docker o nginx reales.
- Verificación de hashes: 2 pruebas correctas de rechazo de archivos corruptos o desconocidos y soporte SHA-512.
- .NET 10 compila los proyectos de juego y cuentas. Las 721 pruebas del núcleo pasan. Godot comprueba el bestiario a 640×450, el cierre de una partida de Defensa y la recuperación del último autoguardado móvil. Las pruebas de cuotas usan los límites predeterminados. Las pruebas de red con más de 32 jugadores en una misma IP usan el aforo de un evento LAN.
- Firestore Emulator comprueba las ventas entre dos instancias, la actualización con el mismo saldo, el borrado recursivo, la conservación de los demás participantes y los recibos después de un borrado. Los resultados exactos del empaquetado y las comprobaciones por plataforma figuran en el flujo asociado a cada versión.

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
- El candidato arranca y verifica su versión antes de poner en retirada el antiguo. Las conexiones WebSocket establecidas sobreviven a la recarga de nginx; X-Arcana-Version conserva el destino de las reconexiones. Las peticiones de salas nuevas van al servidor activo. La cabecera solo se envía para retomar una partida con su token de reconexión.
- Una serie PvP ya iniciada puede terminar sus rondas. La versión en retirada no reinicia Supervivencia ni otra partida tras el resultado. Las conexiones en la pantalla final cuentan hasta que sus clientes salgan; después se espera también a las reservas y recibos pendientes. Una tercera versión se aplaza mientras exista una antigua sin vaciar.
- El estado persiste antes de cambios de ruta o retirada. Los casos ambiguos conservan contenedores para no cortar una partida. No se borran volúmenes; si falla una recuperación o no responde un contenedor, hace falta revisar ese estado antes de continuar.

No hay acceso SSH autorizado/configurado a la Raspberry ni credenciales Admin del proyecto disponibles en esta sesión; no se han aplicado estos pasos.

## Activación de la compra única sin anuncios

1. En Google Play Console, crear para `com.arcana.arena` un producto de compra única con opción de compra permanente, establecer su precio y activarlo para los países elegidos. El juego usa el identificador configurado por el editor. Google Play muestra el importe final antes del cobro.
2. Habilitar Google Play Android Developer API en Google Cloud, invitar la cuenta de servicio del servidor en Play Console y concederle únicamente los permisos necesarios de compras y pedidos de esta aplicación. El servidor usa las credenciales ya montadas con `GOOGLE_APPLICATION_CREDENTIALS`. El móvil nunca contiene la clave.
3. Configurar `ARCANA_PLAY_NO_ADS_PRODUCT_ID` con el identificador exacto en la Raspberry y recrear el servicio de cuentas. El paquete se fija en el servidor. El producto queda desactivado mientras la variable esté vacía.
4. Subir el archivo `ArcanaArena-v0.29.70-GooglePlay-arm64.aab` a una pista interna de Play Console y añadir un probador de licencia. En ese móvil comprobar compra, cancelación, pago pendiente, restauración y reembolso. La APK de GitHub usa el actualizador propio. El AAB incluye targetSdk 36 y delega la actualización en Play. Registrar en Firebase las huellas del certificado de Play App Signing y completar las declaraciones de la consola. Comprobar que la clave de subida registrada coincide con la del AAB. Las futuras compilaciones de este paquete se obtienen activando la opción de Google Play en el flujo de compilación manual.
5. La compra pertenece al UID de Firebase utilizado para pagar. En otro dispositivo se inicia sesión con la misma cuenta del juego y se consulta la tienda. «Restaurar compra» recupera una operación interrumpida. Los recibos viven en `users/{uid}/billing/noAds`, accesible solo al backend. El servidor comprueba las compras al usar la tienda si han pasado doce horas. Durante una interrupción de Google mantiene la última verificación hasta un máximo de veinticuatro horas. Un reembolso retira el derecho comprado y conserva cualquier exención administrativa independiente.

## Comprobaciones que requieren el despliegue

Probar dos versiones en la Raspberry con una partida en curso y una reconexión. Verificar la renovación AdMob con una unidad de prueba y una cuenta de pruebas. Medir FPS, batería y memoria en un Android físico bajo carga. La retención automática y la compactación de datos históricos son optativas.
