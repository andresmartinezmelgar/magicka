# Registro desde Android con el servidor en el PC

Requiere Arcana Arena 0.16.2 en Windows y Android. El servidor se ejecuta en el PC; Firebase Authentication identifica al jugador y Firestore conserva su perfil, inventario y equipamiento.

## Uso habitual

1. En Windows, abre Arcana Arena y pulsa **Alojar**. Introduce la contraseña del servidor en sus opciones cuando Alojar la solicite. Espera a que aparezca el servidor activo.
2. En el móvil, pulsa **Entrar o registrarse con Google**, elige tu cuenta en el navegador y vuelve al juego. El primer acceso crea el perfil automáticamente. Los siguientes recuperan la misma cuenta.
3. Pulsa **Jugar online** para consultar las salas del servidor. El inventario está en **Inventario y mago**.

No hay que abrir PowerShell, copiar la URL del túnel ni autorizar dominios por cada usuario. El PC y el juego deben permanecer abiertos; con el servidor apagado la app muestra que no está disponible y permite reintentar. El anuncio caduca en dos minutos si deja de renovarse.

## Configuración inicial del PC

En el proyecto `arcana-arena-f2997`, habilita Google en Authentication y utiliza Firestore `(default)`. Publica `firebase/firestore.rules`. El proyecto público que consulta Android está en `src/Arcana.Client/account-server.json`.

En Windows, **Alojar → Configurar Firebase gratis (archivos privados)** abre `user://firebase/`. Allí coloca:

- `server.local.json`: los valores públicos `projectId`, `apiKey`, `authDomain`, `publicUrl` y `"autoPublicHosting": true`. Usa `firebase/server.example.json` como plantilla.
- `service-account.json`: credencial privada del proyecto, solo en el PC del servidor.

La cuenta de servicio necesita acceso administrativo a Firestore y permiso para consultar/actualizar la configuración de Authentication. Si tu organización restringe estos permisos, el servidor muestra el error de arranque.

Al iniciar, el servidor descarga o verifica cloudflared 2026.10.0 mediante SHA256, abre el túnel HTTPS, agrega su dominio autorizado y actualiza la URL de la configuración privada. Comprueba la salud del servidor y del túnel antes de anunciarlo; renueva el anuncio cada minuto. Al detener Alojar se cierran el servidor y su túnel.

Esta configuración es para pruebas con un solo PC activo por proyecto. Los túneles temporales cambian de dirección al reiniciar y pueden estar temporalmente indisponibles. Los dominios anteriores quedan en Authentication y se pueden eliminar cuando ya no se utilizan.

## Datos públicos y privados

`publicAccountServer/current` contiene únicamente `url`, `expiresAt` y `protocol`. Las reglas permiten leer ese documento exacto mientras está vigente; prohíben listar la colección, escribir desde el cliente y leer cuentas o inventarios directamente. El servidor administra los datos privados y verifica los tokens Google.

No distribuyas `service-account.json`, archivos `*-firebase-adminsdk-*.json` ni `server.local.json` en GitHub, la APK o el paquete Windows. El cliente solo incluye el ID público del proyecto.

## Comprobaciones

Las pruebas de `Arcana.Tests` cubren la dirección WSS esperada, caducidad, protocolo incompatible, dominios falsos, credenciales en URL y rutas alternativas. El acceso Google completo debe comprobarse en un móvil con una cuenta real; las pruebas de análisis de URL no sustituyen ese recorrido.

El script `scripts/start-firebase-android-tests.ps1` queda como alternativa manual de diagnóstico; no es necesario para el flujo automático de la versión 0.16.2.

La versión 0.16.2 corrige la lectura de los claims de Firebase Admin al completar Google Login. En Android, una dirección local guardada (127.0.0.1, localhost o ::1) se sustituye automáticamente por el descubrimiento público para iniciar sesión. El estado del acceso aparece junto al botón.

`dotnet run --project tests/Arcana.Account.Tests -- --verified-claims-only` verifica el formato real de los claims sin acceder a Firebase. `account-entry-smoke` prueba el toque sobre el botón y el reemplazo de la dirección local en móvil.
