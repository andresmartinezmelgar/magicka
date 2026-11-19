# Permisos, tienda diaria y opción sin anuncios

Los UID de las cuentas administradoras no se incluyen en el cliente, el motor ni las pruebas. Cada cuenta Google carga su configuración desde `users/{uid}/configuration/account` al iniciar sesión o actualizar el perfil. Firebase Authentication identifica la cuenta; Cloud Firestore almacena su configuración.

## Configuración privada

| Campo | Tipo | Función |
| --- | --- | --- |
| `isAdmin` | boolean | Administrador del juego; recibe versiones de pruebas con sesión vigente. |
| `previewUpdates` | boolean | Acceso a versiones de pruebas sin convertir la cuenta en administradora. |
| `matchStatistics` | boolean | Consultar y borrar el historial global de estadísticas. Es independiente de `isAdmin`. |
| `adsRemoved` | boolean | Exención de anuncios; permite la renovación extra diaria sin reproducir publicidad. No concede renovaciones ilimitadas. |
| `updatedAt` | timestamp, opcional | Fecha de la última administración de permisos. |

Los campos ausentes equivalen a `false`. El propietario solo puede leer su documento concreto, nunca enumerar configuraciones ni cambiar roles, cuotas o compras. Las escrituras requieren Firebase Admin/IAM desde un entorno de confianza. No se incorpora ninguna credencial privada a la APK. Los permisos de la caché sin sesión no habilitan versiones de pruebas, estadísticas ni acciones protegidas.

El cliente carga estos campos tanto mediante la lectura directa de Firestore como mediante el servidor de cuentas. Un cambio se aplica al volver a consultar el perfil; no se necesita recompilar para añadir o revocar cuentas. Si las reglas antiguas impiden leer esta ruta, el inventario sigue cargando con los permisos desactivados.

## Migración de las tres cuentas existentes

1. En la base **existente** del proyecto `arcana-arena-f2997`, ejecutar el servidor con `--account-admins` seguido de las tres cuentas reales, cada una como correo Google o `uid:UID`. Se necesitan credenciales de aplicación de Google con acceso Admin y Firestore, externas al repositorio.
2. El comando valida las tres cuentas Google activas y distintas antes de escribir. En una transacción concede `isAdmin` y `previewUpdates`, copia el permiso anterior de `statisticsReaders/{uid}` y conserva `adsRemoved` y los permisos que ya existan. Repetirlo no borra compras ni revoca otras cuentas.
3. Publicar `firebase/firestore.rules` en esa misma base antes de distribuir el nuevo cliente. No crear otra base ni activar un inicio de sesión distinto.
4. Desplegar el servidor actualizado y distribuir el cliente cuando se solicite su compilación.

La configuración nueva prevalece sobre `statisticsReaders`: poner `matchStatistics: false` revoca el acceso aunque el documento antiguo siga habilitado. Se conserva la ruta antigua para compatibilidad con clientes anteriores. El comando `--statistics-readers` actualiza ambas rutas para las cuentas que administra.

Esta revisión prepara la migración; no la ejecuta automáticamente al arrancar ni contiene una lista de UID para hacerlo. La sesión disponible no tiene acceso autenticado al proyecto de producción.

## Estado diario de cada jugador

| Documento | Datos y persistencia |
| --- | --- |
| `users/{uid}/shop/current` | Las seis ofertas, precios, objetos vendidos, revisión y siguiente rotación diaria. |
| `rewardedRefreshAvailableAt` en ese documento | Cuándo vuelve a estar disponible la renovación extra, compartida entre anuncio y exención. |
| `previousRefreshShopId` en ese documento | Catálogo reemplazado por la renovación sin anuncio; permite confirmar un reintento sin volver a sortear. |
| `users/{uid}/shop/rewardedAd` | Ticket y confirmación publicitaria del servidor. |
| `users/{uid}/shopPurchases/{itemId}` | Recibo de compra para no cobrar dos veces un reintento. |
| `admobTransactions/{transactionHash}` | Recibo publicitario global contra reutilización de una recompensa. |

Consultar, cerrar el juego, reinstalarlo, usar otro dispositivo o reiniciar el servidor no restablece estas cuotas. El día cambia a las 00:00 en `Europe/Madrid`, incluidos los cambios de hora. Las escrituras de tienda, recibos y cuotas siguen reservadas al servidor.

`POST /account/shop/refresh` recibe solo el identificador del catálogo. La identidad procede del token autenticado y el servidor lee `adsRemoved` dentro de la transacción. Tres solicitudes simultáneas sobre el mismo catálogo confirman una única renovación. La ruta funciona también en Windows y con AdMob desactivado para cuentas con exención. Las compras y el oro se conservan.

## Pago real y anuncios pendientes

`adsRemoved` representa un derecho concedido por el servidor. Esta revisión no integra una pasarela ni afirma que un jugador haya pagado. Para venderlo mediante Google Play falta implementar Billing con un producto no consumible, validar el token en el servidor con Google Play Developer API, reconocer la compra, guardar el recibo vinculado a la cuenta y tratar restauraciones y reembolsos. Solo una compra validada debe activar el derecho. Nunca aceptar del cliente un booleano que afirme el pago.

La integración recompensada de Android ya incluye consentimiento UMP y validación SSV. Para monetizar faltan la cuenta y aplicación de AdMob, App ID y unidad reales, mensaje de privacidad, política publicada, verificación de la aplicación y configuración del callback y del servidor. Los detalles están en [ADMOB.md](ADMOB.md).

## Validación de esta revisión

- Reglas compiladas y probadas con el emulador local de Firestore: propietario, otras cuentas, proveedor incorrecto, escalación de roles, falsificación de exenciones y cuotas, revocación explícita y compatibilidad del dashboard.
- `AccountAccessTests`: carga remota, revocación, datos ausentes o denegados y tipos incorrectos.
- `ReleaseUpdateChannelTests` y `ReleaseUpdateQueryTests`: permisos remotos y cierre de sesión, sin UID reales.
- `ShopEmulatorTests`: exención remota, administración sin compra, varios dispositivos, reintentos, persistencia, revocación, medianoche, saldo/inventario y ruta HTTP del cliente.
- `--defense-smoke`: casillas cuadradas y silueta, elemento y afijo dentro del margen interior.

La versión 0.29.69 compila el cliente, el servidor y el servicio de cuentas heredado. Se han ejecutado 717 pruebas del motor y comprobaciones de estadísticas y HUD móvil. La publicación exige además las pruebas de integración, la revisión gráfica y la verificación de los paquetes firmados en GitHub Actions. La migración de los permisos en producción se ejecuta por separado con credenciales de Firebase Admin.
