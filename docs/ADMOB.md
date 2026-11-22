# AdMob y renovación diaria de ofertas

Android integra Google Mobile Ads 25.5.0 y UMP 4.0.0. El servidor permanece con publicidad desactivada hasta configurar una aplicación y unidad reales. El App ID de ejemplo de Google permite compilar; no monetiza ni activa renovaciones simuladas.

## Configuración de producción

- Crear la aplicación `com.arcana.arena` y una unidad **Recompensado** en AdMob.
- Crear el mensaje de privacidad y publicar la política correspondiente. Declarar público objetivo y tratamiento aplicable.
- Configurar y verificar SSV en la unidad: `https://familiamelgar.duckdns.org/account/shop/ad/ssv`. El proxy debe enrutar `/account/shop/ad/ssv` al servidor.
- Activar primero la configuración del servidor. En la prueba de URL de AdMob usar `arcana_ssv_probe` tanto en User ID como en Custom data. Una comprobación firmada válida recibe HTTP 200 sin generar ofertas ni consumir la cuota.
- En GitHub Actions, crear la variable `ARCANA_ADMOB_APP_ID` y volver a compilar la APK.
- En el servidor configurar `ARCANA_ADMOB_ENABLED=true`, `ARCANA_ADMOB_APP_ID` y `ARCANA_ADMOB_REWARDED_UNIT_ID`. El App ID tiene que coincidir con el manifiesto de la APK.
- Vincular la aplicación con una tienda admitida, verificar app-ads.txt cuando AdMob lo exija, completar preparación de la aplicación y pagos.

El AAB de Google Play usa targetSdk 36 y envía las actualizaciones a la ficha de la tienda. El proceso de compilación excluye los permisos, la actividad y el plugin del instalador externo. La APK de GitHub conserva su propio actualizador y targetSdk 35. Registrar también el certificado de Play App Signing en Firebase/Google para el login.

## Flujo

1. GET `/account/shop` devuelve configuración publicitaria solo cuando el servidor está activado con IDs reales. Windows y APKs con otro App ID mantienen el botón desactivado.
2. POST `/account/shop/ad/start`, autenticado, crea un ticket aleatorio por cuenta. Comprueba catálogo y límite. Caduca a los treinta minutos.
3. Android obtiene consentimiento antes de inicializar publicidad y presenta el anuncio a petición del jugador. Envía cuenta en user_id y ticket en custom_data.
4. Google llama a GET `/account/shop/ad/ssv`. Se comprueba ECDSA SHA-256 DER sobre la consulta original, clave de Google (caché de doce horas), unidad, fecha e identidad.
5. Firestore sustituye las seis ofertas y guarda conjuntamente el ticket completado, disponibilidad del siguiente día y recibo global de la transacción. Las compras y el inventario no se modifican.
6. POST `/account/shop/ad/complete`, autenticado, consulta el resultado. No concede premios por afirmaciones del cliente o por una cuenta atrás. Reintentos del mismo callback no generan otro catálogo.

El día termina a medianoche en Europe/Madrid. Un ticket sin confirmación SSV no consume la renovación. La rotación diaria ordinaria de la tienda se conserva.

Las reglas existentes impiden escrituras de clientes en los documentos de tienda y en la colección de recibos. No son necesarios índices nuevos.

## Cuentas con opción sin anuncios

La configuración privada `users/{uid}/configuration/account` se lee en el servidor. El administrador queda exento por `isAdmin`; una compra o derecho vigente exime por `adsRemoved` o `playAdsValidUntil`. Son permisos independientes: comprar no convierte al jugador en administrador y ser administrador no registra una compra. La tienda utiliza `POST /account/shop/refresh` sin mostrar publicidad, también en Windows. Comparte el mismo límite de una renovación extra al día con la vía recompensada; cambiar de dispositivo o reinstalar no restablece el límite.

El interruptor global `gameConfig/advertising.enabled` se cambia desde **Mi perfil** por un administrador. Desactivarlo permite a los demás jugadores la misma renovación diaria sin publicidad; reactivarlo siempre respeta las exenciones. Se conserva la configuración real de AdMob para volver a usarla al reactivar. Los clientes no escriben directamente este documento. Ver [CONFIGURACION-CUENTAS.md](CONFIGURACION-CUENTAS.md#interruptor-global-de-anuncios).

Android integra Google Play Billing 8.3.0. El servidor comprueba el producto y la cuenta, confirma el pago y revalida los recibos. El editor configura el producto y su precio, los permisos del servidor y los probadores en Play Console. Ver [CONFIGURACION-CUENTAS.md](CONFIGURACION-CUENTAS.md).

## Verificación

- `AdMobRewardVerifierTests`: firma válida, datos alterados, unidad incorrecta, fecha caducada y parámetros duplicados, sin consultar servicios reales.
- `ShopEmulatorTests`: confirmación obligatoria, persistencia, concurrencia, idempotencia, cambio de día y conservación de compras/saldo.
- Las pruebas visuales mantienen desactivada la publicidad sin configuración; no muestran anuncios reales en CI.
- Validar carga, consentimiento, cierre y callback real con dispositivos de prueba después de configurar la cuenta, antes de tráfico de producción.

## Fuentes oficiales

- https://developers.google.com/admob/android/quick-start
- https://developers.google.com/admob/android/privacy
- https://developers.google.com/admob/android/rewarded
- https://developers.google.com/admob/android/ssv
- https://support.google.com/admob/answer/10564477
- https://support.google.com/admob/answer/14538460
- https://support.google.com/googleplay/android-developer/answer/11926878
- https://support.google.com/googleplay/android-developer/answer/12085295

La compra y restauración mediante Google Play se configuran en la [guía de la versión 0.29.70](MEJORAS-SEGURIDAD-RENDIMIENTO-v0.29.70.md#activación-de-la-compra-única-sin-anuncios).
