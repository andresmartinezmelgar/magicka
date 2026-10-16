# Canales de actualización

El actualizador utiliza el UID de la sesión de Google. Las dos cuentas declaradas en `ReleaseUpdateChannel` consultan las versiones preliminares publicadas desde ramas distintas de `main`. Las otras cuentas y las sesiones cerradas consultan exclusivamente la última Release estable. Una copia del inventario sin sesión no habilita el canal de pruebas. La sesión de Google sirve para elegir el canal incluso si Firestore no ha conseguido cargar el perfil.

Al iniciar sesión, cerrar sesión o cambiar de cuenta se descartan avisos, consultas y descargas pendientes del canal anterior. Se mantiene la elección entre descargar y continuar jugando. La ventana identifica las pruebas por su rama y su número de compilación.

## Publicación

GitHub Actions compila las ramas y publica una preliminar solo después de aprobar código, servidor, reglas, cuentas, capturas y ambos paquetes. Exige la firma Android permanente y comprueba los SHA-256. Prepara el borrador completo antes de hacerlo visible. La preliminar lleva el tag `preview-vVERSION-rNUMERO`, el commit exacto y la rama de origen. No cambia `/releases/latest` ni publica tags `v*`.

El instalador comprueba el repositorio, el tag, la plataforma y el manifiesto de integridad. Las pruebas utilizan paquetes completos para evitar mezclar parches de distintas ramas. A igual versión, el número de Actions permite actualizar una compilación de pruebas y evita ofrecerla repetidamente después de instalarla. No se ofrecen APK con una versión base inferior a la instalada.

## Primera instalación

La antigua Defensa 0.18.0 tenía el actualizador desactivado: necesita instalar manualmente la APK 0.18.1 una vez. A partir de ella, el canal depende de la cuenta autenticada. Las APK mantienen el identificador y el certificado habituales, por lo que sustituyen la instalación anterior. Para jugar online, cliente y servidor deben tener protocolos compatibles; el servidor de Defensa sigue distribuyéndose con el paquete Windows de su rama.
