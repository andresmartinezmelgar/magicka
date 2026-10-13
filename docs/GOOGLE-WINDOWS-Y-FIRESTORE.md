# Google Login en Windows — Arcana Arena

Android utiliza Credential Manager y FirebaseAuth. Windows utiliza el navegador del
sistema, OAuth 2.0 con PKCE, callback en `127.0.0.1` y el intercambio del ID token
Google por un ID token Firebase. Ambos leen el mismo proyecto Firestore.

## Configuración pública que falta para Windows

En **Google Cloud Console > APIs y servicios > Credenciales**, dentro del proyecto
`arcana-arena-f2997`, crea un **ID de cliente OAuth 2.0** de tipo
**Aplicación de escritorio** (no «Aplicación web»).

El ID público (sufijo `.apps.googleusercontent.com`) se facilita al ejecutable por
uno de estos medios:

- Variable de entorno `ARCANA_GOOGLE_DESKTOP_CLIENT_ID`.
- Fichero de texto `google-desktop-client-id.txt` al lado de `ArcanaArena.exe`.
- Fichero homónimo en el directorio de datos de usuario Godot.

Contenido del fichero: únicamente el ID público. No incluir claves privadas,
archivos de servicio, tokens ni `client_secret`. No es necesario cambiar el APK.

El botón «Entrar con Google» ya no está desactivado en Windows. Sin un ID de
escritorio configurado muestra el requisito; después de configurarlo abre el
navegador y permite autenticarse sin levantar un servidor propio. La sesión
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
