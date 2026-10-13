# Servicio de cuentas separado (Arcana.AccountServer)

Este ejecutable ASP.NET Core sirve exclusivamente `/account/*` y `/health`. No aloja salas, no usa puertos UDP, no ejecuta la simulación y no depende de que esté encendido el servidor de partidas.

## Ejecutar en un host independiente

```bash
dotnet publish src/Arcana.AccountServer/Arcana.AccountServer.csproj -c Release -o artifacts/account-server
export FIREBASE_PROJECT_ID=tu-proyecto
export FIREBASE_API_KEY=tu-api-key-publica
export FIREBASE_AUTH_DOMAIN=tu-proyecto.firebaseapp.com
export GOOGLE_APPLICATION_CREDENTIALS=/ruta/privada/service-account.json
export ARCANA_PUBLIC_URL=https://cuentas.tu-dominio.com
export ACCOUNT_PORT=8080
dotnet artifacts/account-server/Arcana.AccountServer.dll
```

Poner un proxy HTTPS delante de `ACCOUNT_PORT` y configurar el dominio en Firebase Authentication (dominios autorizados). No incluir credenciales de servicio en APK/EXE ni en Git.

En `src/Arcana.Client/account-server.json` establecer `"accountUrl":"https://cuentas.tu-dominio.com"` además de `projectId` y volver a exportar clientes. Hasta configurar esa URL, el cliente mantiene el descubrimiento antiguo, que **depende del servidor de partidas**.

**Limitación de la primera fase:** los servidores de partidas antiguos siguen alojando endpoints de cuentas y conservan sesiones en memoria propias. Las recompensas y el token PvP todavía requieren una integración segura entre ambos servicios; no se debe retirar el componente de cuentas del servidor de juego ni afirmar independencia completa hasta implementar esa comunicación y migrar los tests.
