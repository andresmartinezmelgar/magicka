# Pruebas de Firebase y Android desde Windows

El servidor del portatil administra Firestore y verifica el login Google. El APK abre el login web en el navegador; no necesita incluir la credencial privada ni registrar otra app Android para este flujo.

## Preparacion

1. Usa el mismo commit/version para servidor y APK. Descarga los paquetes Windows y Android de la misma ejecucion de GitHub Actions, o compilalos desde este checkout.
2. Instala Node.js 20 o posterior y [cloudflared](https://developers.cloudflare.com/tunnel/downloads/). Comprueba `node --version` y `cloudflared --version`.
3. Ejecuta desde la raiz del repositorio:

```powershell
npm install --prefix scripts/firebase-tests
```

4. Sigue [PROGRESSION.md](PROGRESSION.md) para habilitar Google Login y Firestore Standard con las reglas `firebase/firestore.rules`. En la carpeta `%APPDATA%\Godot\app_userdata\Arcana Arena\firebase`, guarda:
   - `server.local.json`: copia de `firebase/server.example.json` con los valores publicos del proyecto.
   - `service-account.json`: credencial privada del mismo proyecto, autorizada para Firebase Auth y Firestore. El helper necesita ademas permiso para consultar y modificar los dominios autorizados de Firebase Authentication. Nunca copies esta clave al repositorio, a la APK ni a los artefactos de CI.

El proyecto usado en la prueba inicial fue `arcana-arena-f2997`, con Firestore Standard en Madrid (`europe-southwest1`). El script obtiene el proyecto de tu archivo privado de configuracion; no tiene un proyecto ni una URL temporal fijados.

## Arranque

Con un paquete Windows compilado en `artifacts/windows`:

```powershell
.\scripts\start-firebase-android-tests.ps1
```

Para compilar solo el servidor, usa el SDK .NET indicado por `global.json`:

```powershell
dotnet publish src/Arcana.Server/Arcana.Server.csproj -c Release -r win-x64 --self-contained true -o artifacts/firebase-server
.\scripts\start-firebase-android-tests.ps1 -ServerPath "$PWD\artifacts\firebase-server\Arcana.Server.exe"
```

Tambien puedes indicar `-CloudflaredPath`, `-PrivateDirectory` y `-Port`. No inicies otro servidor con Alojar mientras este script use el mismo puerto.

El script crea un Quick Tunnel temporal, agrega su dominio a Firebase conservando los existentes, actualiza `publicUrl` en el archivo local y arranca el servidor. Comprueba `/health` local y publico y `/account/config`. Solo muestra «Servidor listo» cuando las comprobaciones pasan. Manten abierta la ventana: Ctrl+C detiene los dos procesos iniciados por el script. Los registros y la direccion quedan en `.tools/firebase-tests/`, excluido de Git.

En Android, usa **Por IP** con la direccion `wss://.../play` mostrada, pulsa **Entrar con Google** y comprueba inventario y equipamiento. Cambia una pieza, cierra el juego e inicia sesion otra vez para comprobar persistencia. Las recompensas requieren PvP elegible; las partidas contra bots no entregan objetos.

El tunel da acceso publico temporal al servidor del portatil. Usa contrasena de sala/servidor para pruebas privadas. Cada arranque genera otro dominio: el script agrega el nuevo; elimina los dominios temporales antiguos desde Authentication cuando termines. No es un alojamiento permanente.

## Estado de validacion

En la preparacion inicial se compilo el servidor 0.16.0, se publicaron las reglas del repositorio y se confirmo una lectura autenticada de Firestore. Se comprobo la sintaxis de PowerShell y JavaScript. El arranque conjunto, OAuth real y la prueba desde Android siguen pendientes de ejecutarse en el portatil: la sesion automatizada no pudo completar la conexion entre tunel y servidor. La configuracion de la cuenta Firebase vive fuera de GitHub y no se despliega al descargar estos archivos.

La herramienta fija Firebase CLI 15.33.0 y usa sus helpers internos para administrar dominios; una actualizacion de esa dependencia debe verificar su compatibilidad antes de cambiarla.
