# Tienda, pagos y publicidad 0.29.72

La tienda separa equipamiento diario, oro y pases sin anuncios. El menú tiene tres áreas, tarjetas adaptables e ilustraciones vectoriales ajustadas al cuadrado. No se muestran precios monetarios inventados ni se abren cobros si Google Play no devuelve un precio disponible.

## Catálogo de producción

| Producto | ID Google Play | Modalidad | Plan base |
| --- | --- | --- | --- |
| 1.000 de oro | `arcana_gold_1000` | Compra única consumible | - |
| 5.500 de oro | `arcana_gold_5500` | Compra única consumible | - |
| 15.000 de oro | `arcana_gold_15000` | Compra única consumible | - |
| 50.000 de oro | `arcana_gold_50000` | Compra única consumible | - |
| Un mes sin anuncios | `arcana_no_ads_pass` | Suscripción de prepago, sin renovación automática | `month-prepaid`, duración 1 mes |
| Seis meses sin anuncios | `arcana_no_ads_pass` | Suscripción de prepago, sin renovación automática | `six-months-prepaid`, duración 6 meses |
| Sin anuncios para siempre | `arcana_no_ads_forever` | Compra única no consumible | - |

Los precios se fijan en Play Console y se consultan en el dispositivo. El ID de la compra permanente admite el valor existente de `ARCANA_PLAY_NO_ADS_PRODUCT_ID`; no cambiar un ID comercial ya vendido. Los otros ID y las cantidades están definidos en el servidor y no los elige el teléfono. No configurar renovaciones automáticas ni ofertas promocionales para estos planes base; el cliente comprueba el período y que la fase sea no recurrente. La interfaz no vende otro pase mientras el derecho sin anuncios esté vigente y permite gestionarlo en Google Play.

## Activación manual

1. Preparar la cuenta de editor y el perfil de pagos en Play Console. Completar los requisitos que indique la cuenta y la ficha `com.arcana.arena`.
2. Subir el AAB firmado a una pista interna y registrar el certificado de Play App Signing en Firebase para el acceso Google. La APK de GitHub conserva su propio actualizador; el AAB de Play excluye el instalador externo y usa targetSdk 36.
3. Crear y activar los productos anteriores, sus precios, países y planes de prepago. Mantener desactivada la extensión de pases para el primer lanzamiento.
4. Activar Google Play Developer API e invitar a la cuenta de servicio del servidor en Play Console con acceso a esta aplicación y los permisos de datos financieros y gestión de pedidos/suscripciones. Las credenciales permanecen en el servidor, fuera del repositorio y de la APK.
5. Añadir `ARCANA_PLAY_SHOP_ENABLED=true` y `ARCANA_PLAY_NO_ADS_PRODUCT_ID=arcana_no_ads_forever` al entorno que realmente carga el contenedor. El archivo Compose del repositorio propaga ambas opciones. Conservar la conexión Firebase que ya funciona.
6. Actualizar el servidor mediante su despliegue por generaciones y probar con verificadores de licencia y medios de pago de prueba de Google Play. Registrar una compra de cada tipo, un pago pendiente, cancelación, restauración, reintentos y otro dispositivo.
7. Mantener los anuncios apagados hasta crear la aplicación y unidad recompensada en AdMob, el mensaje de privacidad, el sitio y app-ads.txt, y validar el callback SSV existente.

## Validación y persistencia

`POST /account/shop/play/purchase` recibe solo `productId` y `purchaseToken`; la identidad procede de la sesión Google. El servidor consulta Google Play Developer API y comprueba propietario, producto y estado. Los pagos pendientes no entregan oro ni exenciones.

El oro se suma en una transacción con el recibo global `playPurchases/{sha256DelToken}`. Repetir o concurrir con ese recibo no suma otra vez. El servidor consume la compra después de guardarla; si falla la respuesta, restaurar puede completar el consumo sin duplicar oro. Un recibo consumido desconocido no se canjea. Al borrar la cuenta se anonimiza el UID del recibo, conservando el hash para impedir reutilización.

Los pases se guardan en `users/{uid}/billing/noAdsPass`. Google determina la caducidad real. `configuration/account.playPassExpiresAt` muestra esa fecha y `playPassValidUntil` limita la confianza en la última comprobación a 24 horas. La tienda revalida a partir de las doce horas. La compra permanente mantiene su recibo y comprobación anteriores. Restaurar un pase no vuelve a sumar meses; restaura la caducidad autorizada por Google.

`isAdmin`, `adsRemoved`, `playAdsValidUntil` y `playPassValidUntil` son derechos independientes. Una compra no concede administración ni acceso al historial global. La pausa global no modifica compras ni la renovación extra de una vez al día. Mientras todos tengan la publicidad pausada, no se ofrecen nuevos pases. Las reglas actuales ya deniegan al cliente escrituras de saldo, recibos, roles y vencimientos; no se necesitan reglas nuevas.

## AdMob

El editor configura `ARCANA_ADMOB_APP_ID` como variable de GitHub y recompila con el App ID real. En el servidor: `ARCANA_ADMOB_ENABLED=true`, el mismo App ID y `ARCANA_ADMOB_REWARDED_UNIT_ID`. La unidad recompensada usa SSV en `https://familiamelgar.duckdns.org/account/shop/ad/ssv`; la comprobación de configuración admite `arcana_ssv_probe` como User ID y Custom data. Después de validar consentimiento, cierre, recompensa y exclusiones en dispositivos de prueba, el administrador activa el interruptor en Mi perfil.

## Límites que requieren gestión del editor

- No se han creado cuentas comerciales, aceptado contratos, fijado precios ni activado ventas o anuncios reales.
- Las notificaciones RTDN y la conciliación automática de compras de oro reembolsadas después de su entrega no están implementadas. Los pedidos y ajustes de soporte se gestionan manualmente; no hay retirada automática del oro ya gastado.
- La revocación de pases y compra permanente se detecta al revalidar; la última autorización caduca en un máximo de 24 horas. Una implementación RTDN permitiría reaccionar antes.
- La validación final de Play Billing, pagos pendientes, restauraciones, reembolsos y consentimiento UMP requiere una aplicación configurada y un dispositivo Android. Las pruebas automáticas no cobran ni reproducen publicidad real.

Fuentes oficiales: [Play Billing](https://developer.android.com/google/play/billing/integrate), [prepago](https://support.google.com/googleplay/android-developer/answer/140504), [pruebas](https://developer.android.com/google/play/billing/test), [API de compras](https://developers.google.com/android-publisher/getting_started), [privacidad UMP](https://developers.google.com/admob/android/privacy), [SSV](https://support.google.com/admob/answer/9603226).
