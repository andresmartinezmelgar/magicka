# Permisos, tienda diaria y opción sin anuncios

Los UID de las cuentas administradoras no se incluyen en el cliente, el motor ni las pruebas. Cada cuenta Google carga su configuración desde `users/{uid}/configuration/account` al iniciar sesión o actualizar el perfil. Firebase Authentication identifica la cuenta; Cloud Firestore almacena su configuración.

## Configuración privada

| Campo | Tipo | Función |
| --- | --- | --- |
| `isAdmin` | boolean | Administrador del juego: versiones de pruebas, exención de anuncios y control del interruptor global. No concede acceso a estadísticas por sí solo. |
| `previewUpdates` | boolean | Acceso a versiones de pruebas sin convertir la cuenta en administradora. |
| `matchStatistics` | boolean | Consultar y borrar el historial global de estadísticas. Es independiente de `isAdmin`. |
| `adsRemoved` | boolean | Exención de anuncios; permite la renovación extra diaria sin reproducir publicidad. No concede renovaciones ilimitadas. |
| `playAdsValidUntil` | integer | Caducidad Unix del derecho de Google Play revalidado por el servidor. No concede permisos de administrador. |
| `updatedAt` | timestamp, opcional | Fecha de la última administración de permisos. |

Los campos ausentes equivalen a `false`. El propietario solo puede leer su documento concreto, nunca enumerar configuraciones ni cambiar roles, cuotas o compras. Las escrituras requieren Firebase Admin/IAM desde un entorno de confianza. No se incorpora ninguna credencial privada a la APK. Los permisos de la caché sin sesión no habilitan versiones de pruebas, estadísticas ni acciones protegidas.

El cliente carga estos campos tanto mediante la lectura directa de Firestore como mediante el servidor de cuentas. Un cambio se aplica al volver a consultar el perfil; no se necesita recompilar para añadir o revocar cuentas. Si las reglas antiguas impiden leer esta ruta, el inventario sigue cargando con los permisos desactivados.

## Migración de las tres cuentas existentes

1. En la base **existente** del proyecto `arcana-arena-f2997`, ejecutar el servidor con `--account-admins` seguido de las tres cuentas reales, cada una como correo Google o `uid:UID`. Se necesitan credenciales de aplicación de Google con acceso Admin y Firestore, externas al repositorio.
2. El comando valida las tres cuentas Google activas y distintas antes de escribir. En una transacción concede `isAdmin` y `previewUpdates`, copia el permiso anterior de `statisticsReaders/{uid}` y conserva `adsRemoved` y los permisos que ya existan. Repetirlo no borra compras ni revoca otras cuentas.
3. Publicar `firebase/firestore.rules` en esa misma base antes de distribuir el nuevo cliente. No crear otra base ni activar un inicio de sesión distinto.
4. Desplegar el servidor actualizado y distribuir el cliente cuando se solicite su compilación.

La configuración nueva prevalece sobre `statisticsReaders`: poner `matchStatistics: false` revoca el acceso aunque el documento antiguo siga habilitado. Se conserva la ruta antigua para compatibilidad con clientes anteriores. El comando `--statistics-readers` actualiza ambas rutas para las cuentas que administra.

El 22/11/2026 se completó y verificó la migración de las tres cuentas originales en la base de producción: `isAdmin: true`, `previewUpdates: true` y `adsRemoved: false`. Se conservaron los permisos de estadísticas anteriores: dos habilitados y uno deshabilitado. Los UID reales permanecen en Firebase, fuera del código. El comando sigue disponible para administrar o repetir la migración con credenciales de confianza; nunca se ejecuta automáticamente al arrancar.

## Interruptor global de anuncios

El botón de **Mi perfil**, visible solo para administradores con sesión vigente, alterna «Activar anuncios para todos» y «Desactivar anuncios para todos». La desactivación se mantiene hasta que un administrador vuelva a activarlos. No cambia compras, roles ni la cuota diaria.

| Cuenta | Anuncios activados | Anuncios desactivados | Cambiar el interruptor |
| --- | --- | --- | --- |
| Administrador | Sin anuncios | Sin anuncios | Sí |
| Derecho sin anuncios vigente | Sin anuncios | Sin anuncios | No |
| Jugador ordinario | Anuncio para la renovación extra | Renovación extra sin anuncio | No |

La configuración reside en `gameConfig/advertising`, con `enabled` boolean y `revision` integer. El servidor añade `updatedBy` y `updatedAt` al guardar. El documento está protegido por las reglas existentes: ni siquiera el SDK de un administrador puede leerlo o escribirlo directamente. `GET /account/advertising` devuelve únicamente el estado y la revisión a una cuenta autenticada; `POST` comprueba el rol actual dentro de la misma transacción y rechaza revisiones antiguas con HTTP 409. Una compra sin anuncios no permite administrar; un rol revocado devuelve HTTP 403 sin cerrar la sesión Google.

El estado inicial de producción se guardó y verificó el 22/11/2026: `enabled: false`, `revision: 1`. No hace falta publicar reglas nuevas. Para usar el botón falta compilar y distribuir este cliente y desplegar su servidor; la APK estable 0.29.71 no incluye este cambio.

La tienda consulta un estado pequeño cada minuto mientras está abierta y vuelve a consultar el catálogo antes de iniciar publicidad. El servidor relee la política y la exención en las transacciones de renovación e inicio del anuncio. Desactivar no cierra un anuncio ya abierto: su confirmación y la renovación directa comparten la misma cuota, de una vez al día. Reactivar el interruptor requiere además la configuración real de AdMob; no instala ni configura monetización.

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

`POST /account/shop/refresh` recibe solo el identificador del catálogo. La identidad procede del token autenticado y el servidor lee la política global y la exención dentro de la transacción. Tres solicitudes simultáneas sobre el mismo catálogo confirman una única renovación. La ruta funciona también en Windows, para administradores, derechos sin anuncios vigentes o con el interruptor global desactivado. Las compras y el oro se conservan.

## Pago real y anuncios pendientes

`adsRemoved` representa un derecho concedido por el servidor. La integración existente de Google Play Billing valida el producto, la cuenta y el recibo en el servidor, reconoce la compra y permite restauración y revalidación. La configuración comercial y la validación con una compra real y un reembolso siguen requiriendo la cuenta del editor. Solo una compra validada debe activar el derecho; nunca se acepta del cliente un booleano que afirme el pago. Ser administrador exime por su rol y no registra una compra ficticia.

La integración recompensada de Android ya incluye consentimiento UMP y validación SSV. Para monetizar faltan la cuenta y aplicación de AdMob, App ID y unidad reales, mensaje de privacidad, política publicada, verificación de la aplicación y configuración del callback y del servidor. Los detalles están en [ADMOB.md](ADMOB.md).

## Validación de esta revisión

- Reglas compiladas y probadas con el emulador local de Firestore: propietario, otras cuentas, proveedor incorrecto, escalación de roles, falsificación de exenciones y cuotas, revocación explícita y compatibilidad del dashboard.
- `AccountAccessTests`: carga remota, revocación, datos ausentes o denegados y tipos incorrectos.
- `ReleaseUpdateChannelTests` y `ReleaseUpdateQueryTests`: permisos remotos y cierre de sesión, sin UID reales.
- `ShopEmulatorTests`: exención remota, administración sin compra, varios dispositivos, reintentos, persistencia, revocación, medianoche, saldo/inventario y ruta HTTP del cliente.
- `--defense-smoke`: casillas cuadradas y silueta, elemento y afijo dentro del margen interior.

La versión 0.29.69 compila el cliente, el servidor y el servicio de cuentas heredado. Se han ejecutado 717 pruebas del motor y comprobaciones de estadísticas y HUD móvil. La publicación exige además las pruebas de integración, la revisión gráfica y la verificación de los paquetes firmados en GitHub Actions. La migración de los permisos en producción se ejecuta por separado con credenciales de Firebase Admin.
