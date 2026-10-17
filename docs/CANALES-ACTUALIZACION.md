# Canales de actualización

El actualizador utiliza el UID de la sesión de Google. Las dos cuentas declaradas en `ReleaseUpdateChannel` consultan las versiones preliminares publicadas desde ramas distintas de `main` y las estables, eligiendo la versión más reciente. A igual versión, prefieren la compilación de pruebas más reciente. Las otras cuentas y las sesiones cerradas consultan exclusivamente la última Release estable. Una copia del inventario sin sesión no habilita el canal de pruebas. La sesión de Google sirve para elegir el canal incluso si Firestore no ha conseguido cargar el perfil.

Desde 0.20.0, **Perfil → Buscar actualización** permite consultar manualmente, ver la versión instalada y distinguir falta de novedades de un error de conexión. Comparte la consulta en curso con la comprobación automática, respeta el canal de la cuenta y permite volver a mostrar un aviso descartado. Consultar no inicia la descarga.

Al iniciar sesión, cerrar sesión o cambiar de cuenta se descartan avisos, consultas y descargas pendientes del canal anterior. Se mantiene la elección entre descargar y continuar jugando. La ventana identifica las pruebas por su rama y su número de compilación.

## Publicación

GitHub Actions compila las ramas y publica una preliminar solo después de aprobar código, servidor, reglas, cuentas, capturas y ambos paquetes. Exige la firma Android permanente y comprueba los SHA-256. Prepara el borrador completo antes de hacerlo visible. La preliminar lleva el tag `preview-vVERSION-rNUMERO`, el commit exacto y la rama de origen. No cambia `/releases/latest` ni publica tags `v*`.

Se adjuntan deltas Android y Windows desde las tres versiones anteriores disponibles de la misma rama, usando la última compilación verificable de cada versión. No se usan bases estables ni de otras ramas. La APK reconstruida debe coincidir byte a byte con la APK firmada de destino; cada parche tiene su SHA-256 tanto en el manifiesto de su plataforma como en el combinado. Si no hay una base íntegra o el parche no reduce al menos un 15 % la descarga, se conserva el paquete completo. Los informes `DELTAS-*.json` detallan las bases, los tamaños y los bytes reutilizados.

El instalador comprueba el repositorio, el tag, la plataforma y el manifiesto de integridad. Por ahora el actualizador del canal de pruebas sigue descargando paquetes completos; los deltas se adjuntan a la publicación. A igual versión, el número de Actions permite actualizar una compilación de pruebas y evita ofrecerla repetidamente después de instalarla. No se ofrecen APK con una versión base inferior a la instalada.

## Primera instalación

La antigua Defensa 0.18.0 tenía el actualizador desactivado: necesita instalar manualmente una APK 0.18.1 o posterior una vez. A partir de ella, el canal depende de la cuenta autenticada. Las APK mantienen el identificador y el certificado habituales, por lo que sustituyen la instalación anterior. Desde 0.20.0 Supervivencia forma parte de `main`; su servidor se incluye en el paquete Windows estable. Para jugar online, cliente y servidor deben tener protocolos compatibles y actualizarse juntos.
