# Arquitectura de cuentas v0.17.0

## Separación
- **Arcana.AccountServer**: servicio ASP.NET Core independiente, Firebase Authentication Google, Firestore, perfiles, inventario, oro y recompensas. Dockerfile en `src/Arcana.AccountServer/Dockerfile`.
- **Arcana.Server**: salas, descubrimiento LAN, simulación y PvP. No expone rutas `/account/*`, no inicia Firebase, no levanta túneles de cuentas.
- **Arcana.Client**: modo IA local sin backend. Para Google utiliza la dirección HTTPS del servicio de cuentas; se puede introducir y guardar desde Mi perfil sin volver a exportar APK.

## Variables del servicio de cuentas
`FIREBASE_PROJECT_ID`, `FIREBASE_API_KEY`, `FIREBASE_AUTH_DOMAIN`, `ARCANA_PUBLIC_URL` (URL HTTPS pública exacta), `GOOGLE_APPLICATION_CREDENTIALS` (en Cloud Run preferir identidad de servicio con ADC), `ACCOUNT_PORT` (8080) y `ARCANA_ACCOUNT_SERVICE_KEY` (clave aleatoria secreta de 32 caracteres o más).

En Firebase Authentication habilitar proveedor Google y autorizar el dominio de `ARCANA_PUBLIC_URL`. Firestore debe estar creado y la identidad del servicio debe disponer de permisos adecuados. La primera autenticación crea o recupera el perfil en Firestore.

## Variables del servidor de juego
`ARCANA_ACCOUNT_SERVICE_URL` = URL HTTPS del servicio de cuentas, y `ARCANA_ACCOUNT_SERVICE_KEY` = exactamente la misma clave secreta que en el servidor de cuentas. **No introducir esta clave en la APK ni en Git.** Sin estas variables el servidor de juego no puede autenticar PvP. Los servicios pueden estar en máquinas y procesos distintos.

El servidor de juego consulta `/internal/session`, `/internal/profile` y `/internal/award` con la clave compartida por HTTPS. Los endpoints internos rechazan peticiones sin clave. Las recompensas se almacenan en un diario y se reintentan si el servicio de cuentas no está disponible.

## Matriz de comportamiento
- Sin servidores ni red: práctica local y perfil previamente guardado.
- Servidor de juego apagado, cuentas operativo: login Google y perfil.
- Servidor de cuentas apagado: no se pueden crear nuevas sesiones ni verificar jugadores PvP.
- Ambos disponibles: Google obligatorio para PvP, con coincidencia exacta de versión del juego.

## Despliegue
Construir imagen desde raíz del repositorio:
```bash
docker build -f src/Arcana.AccountServer/Dockerfile -t arcana-account-server .
```
Desplegarla en Cloud Run u otro hosting HTTPS persistente, configurar las variables de entorno y una identidad con acceso Firestore. Una vez asignado el dominio público, introducir la URL en Mi perfil del juego. No es necesario mantener encendido el servidor de partidas para iniciar sesión.

**Limitación de infraestructura**: sin desplegar la imagen y configurar las credenciales/variables de Firebase en el proveedor cloud no existe aún una URL de autenticación operativa. No publicar credenciales ni generar enlaces de servicio ficticios.
