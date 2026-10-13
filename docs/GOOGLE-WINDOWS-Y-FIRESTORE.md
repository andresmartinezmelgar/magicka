# Google Login en Windows — Arcana Arena

Android utiliza Credential Manager y FirebaseAuth. Windows utiliza el navegador del
sistema, OAuth 2.0 con PKCE, callback en `127.0.0.1` y el intercambio del ID token
Google por un ID token Firebase. Ambos leen el mismo proyecto Firestore.

## Configuración de Google en Windows

En **Google Cloud Console > APIs y servicios > Credenciales**, dentro del proyecto
`arcana-arena-f2997`, crea un **ID de cliente OAuth 2.0** de tipo
**Aplicación de escritorio** (no «Aplicación web»).

En la pantalla de credenciales, selecciona **Descargar JSON**.

Para incluir automáticamente la configuración en la distribución Windows,
configura el secreto de GitHub Actions `ARCANA_GOOGLE_DESKTOP_OAUTH_BASE64`
con el JSON OAuth de escritorio codificado en Base64. En PowerShell:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("tu-archivo-oauth.json"))
```

En las siguientes compilaciones se adjuntará el archivo
`google-desktop-oauth.json` junto a `ArcanaArena.exe`. **Esto es lo
necesario para que los jugadores no tengan que configurar nada.**
No se debe usar una cuenta de servicio de Firebase.

Si la distribución todavía no trae la configuración, al pulsar
«Configurar y entrar con Google» se abre un selector de archivos: selecciona
directamente el JSON original descargado de Google Cloud. El juego lo
valida, lo guarda en los datos locales y abre el navegador para iniciar
sesión. Solo es necesario importarlo una vez.

De forma alternativa se puede guardar el archivo renombrado como
`google-desktop-oauth.json` junto a `ArcanaArena.exe` o en los datos de
usuario Godot. El juego leerá los campos
`installed.client_id` y `installed.client_secret`; el JSON no debe
subirse a GitHub y está excluido por `.gitignore`.

Alternativas de configuración: variables de entorno
`ARCANA_GOOGLE_DESKTOP_CLIENT_ID` y `ARCANA_GOOGLE_DESKTOP_CLIENT_SECRET`,
o `google-desktop-client-id.txt` con solo el ID público. Google documenta
el secreto de aplicación instalada como opcional, pero algunos clientes
de escritorio lo requieren al intercambiar el código; por eso se recomienda
el JSON completo. Los clientes OAuth instalados no pueden mantener secreto
este dato y **no debe confundirse** con una cuenta de servicio, clave privada
de Firebase Admin ni token de usuario. No es necesario cambiar el APK.

El botón de Google en Windows solicita el JSON únicamente si no se incluyó
la configuración en la distribución. Tras importarlo abre el navegador.
No requiere levantar el servidor de partidas o de cuentas. La sesión
se conserva en memoria mientras se ejecuta el juego; al cerrar el proceso
se requiere una nueva autenticación.

## Error 403 en Android y Windows

La pantalla puede indicar «Google conectado» mientras Firestore devuelve
`403 PERMISSION_DENIED`. Este estado significa que Firebase Authentication
sí ha autenticado a la persona, pero Firestore no autoriza la operación.
El repositorio ya contiene `firebase/firestore.rules`, pero las reglas locales
y las pruebas con emulador no garantizan que estén publicadas.

Publicar con una cuenta administradora del proyecto:

```bash
firebase login
firebase deploy --only firestore:rules --project arcana-arena-f2997
```

Alternativa: [Firestore > Reglas](https://console.firebase.google.com/project/arcana-arena-f2997/firestore/rules),
pegar `firebase/firestore.rules` y pulsar **Publicar**. Comprobar también que
la base de datos es `(default)` y que el proveedor de autenticación Google
está habilitado. Las reglas restringen cada `users/{uid}` al propietario y
no se deben sustituir por reglas abiertas.

No se puede corregir un 403 debido a reglas desplegadas solo modificando
el cliente o haciendo un commit en GitHub: exige publicar reglas en Firebase
con identidad administradora.
