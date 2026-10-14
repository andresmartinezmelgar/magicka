# Prueba de recompensas PvP con un servidor Windows y Firebase

La versión de Arcana Arena de la rama de pruebas usa **`arcana-arena-f2997`** en cliente Windows, plugin Android y Firestore. Si tu proyecto real es distinto (por ejemplo `arcana-arena-511518`), no copies su clave de servicio sin adaptar también los clientes: deben ser **el mismo proyecto**.

## Preparar Firebase
1. Firebase Console → Authentication → Sign-in method → habilitar Google.
2. Firestore Database → crear la base `(default)`. Publicar el fichero `firebase/firestore.rules` desde Firebase CLI autorizado, o revisar y publicar su contenido en la pestaña Reglas. **Nunca** abrir reglas universales.
3. Firebase Console → Configuración del proyecto → Cuentas de servicio → Firebase Admin SDK → Generar clave privada. Descargar el JSON en tu PC y **no subirlo a GitHub**.
4. La cuenta de servicio necesita acceso a Firestore (por ejemplo Cloud Datastore User) y permisos de Firebase Authentication Admin para añadir el dominio temporal a los dominios autorizados de Authentication. En Google Cloud IAM se pueden revisar los permisos de la cuenta de servicio.

## Configurar el juego en Windows
La carpeta Godot `user://` habitual es `%APPDATA%\Godot\app_userdata\Arcana Arena`. Cierra el juego y abre esa carpeta con Win+R o Explorador.

Crea una subcarpeta `firebase` con estos archivos:
- `service-account.json`: clave privada descargada, con ese nombre exacto.
- `server.local.json`: archivo de configuración de texto UTF-8:

```json
{
  "projectId": "arcana-arena-f2997",
  "apiKey": "API_KEY_PUBLICA_DEL_PROYECTO",
  "authDomain": "arcana-arena-f2997.firebaseapp.com",
  "publicUrl": "http://127.0.0.1:27015",
  "autoPublicHosting": true
}
```

Sustituye `apiKey` por la API key del proyecto en Firebase → Configuración del proyecto (no confundir con la **clave privada** Admin, que pertenece exclusivamente a `service-account.json`). La `publicUrl` local inicial es intencional: cuando se inicie el servidor, el túnel automático la sustituirá por una URL HTTPS temporal.

**Importante**: no configurar un puerto distinto de `27015` en el launcher salvo que se cambie `publicUrl` al mismo puerto.

## Prueba de conexión y recompensas
1. Usa el paquete **Windows completo**, incluido `server/`, de esta rama. Reinicia el juego y pulsa Alojar → Iniciar servidor y ver salas. Con `autoPublicHosting:true`, el servidor descarga y verifica `cloudflared`, crea un túnel HTTPS temporal e intenta autorizar su dominio en Firebase. La primera vez puede tardar.
2. El servidor intenta registrar en Firestore `publicAccountServer/current` la dirección `wss://...trycloudflare.com/play`, que caduca y se renueva automáticamente. También se muestra la dirección WSS en el launcher Windows.
3. Conéctate con Google **en ambos dispositivos**; en el móvil usa la dirección WSS del paso anterior en **Por IP** (no la IP LAN). En Windows el anfitrión entra por loopback local y su token Google sigue protegiéndose.
4. Al crear una sala comprueba que NO aparezca el mensaje «SIN RECOMPENSAS: el servidor no tiene Firebase habilitado». Elige Guerra o Duelo, 0 bots, 1 contra 1 (o equipos equilibrados), mejor de 5 y comienza la serie con todos preparados.
5. Juega al menos 15 segundos de actividad reconocida por jugador y completa la serie: el primero en ganar 3 rondas gana un mejor de 5. Se encola el resultado y el servidor escribe transaccionalmente en Firestore `matches/{id}` y en el perfil de cada jugador. Puede tardar unos segundos; los errores de escritura se reintentan desde el diario local.
6. Si no hay premios, comprueba los registros desde **Ver registros** en el launcher y la subcarpeta `firebase/reward-outbox` bajo `user://`. Un archivo JSON pendiente indica un resultado por entregar, no que haya desaparecido.

**Privacidad y seguridad:** nunca compartir `service-account.json`, no subirlo a repositorios ni adjuntarlo en chats o ZIP de diagnóstico. El túnel Quick Tunnel es provisional y no garantiza disponibilidad; usar solo para probar. La firma de APK debe tener su SHA-1 registrada en Firebase. La funcionalidad se ha comprobado con emulador y necesita validación real entre PC y Android.
