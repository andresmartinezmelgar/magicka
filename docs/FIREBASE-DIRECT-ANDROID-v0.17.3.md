# Arcana Arena 0.17.3 — Firebase directo en Android

## Contrato de la versión

- **Android**: Credential Manager abre el selector nativo de Google. El SDK Firebase Authentication intercambia la credencial con Firebase, conserva la sesión y renueva su ID token. Ni el login ni la lectura del perfil contactan con el servidor C#.
- **Firestore**: el ID token del usuario autoriza lectura de `users/{uid}`, `users/{uid}/items` y `users/{uid}/loadouts/active`. Una cuenta nueva crea exclusivamente una cabecera con 0 oro, 0 victorias/derrotas y 0 objetos. Puede seleccionar sus piezas con reglas que comprueban pertenencia y ranura. El cliente no puede generar oro, objetos ni resultados.
- **Servidor C#**: sigue gestionando PvP, validación de resultados, recompensas, ventas y cualquier otra operación privilegiada. Verifica los ID tokens Firebase mediante Admin SDK. El servidor de cuentas opcional permite ventas por HTTPS sin ser requisito para el login.
- **Windows**: no se añade autenticación web de escritorio en esta versión; se conserva práctica IA y perfiles guardados. Google directo está disponible en Android.
- **Offline**: sin conexión a Internet solo puede utilizarse el perfil en caché; iniciar sesión y cargar Firestore requieren Internet, pero no un servidor propio.

## Preparación imprescindible del proyecto Firebase

1. En Authentication > Sign-in method: proveedor **Google habilitado**.
2. Aplicación Android `com.arcana.arena` con SHA-1 y SHA-256 del **certificado permanente** usado para la APK firmada de GitHub Release. `google-services.json` facilitado contiene ID Android, clave pública de API e ID de cliente OAuth web. Los valores públicos se han introducido en el plugin nativo; nunca incluye contraseñas privadas ni credenciales Admin.
3. Crear base de datos Firestore `(default)` si todavía no existe.
4. **Publicar las reglas de `firebase/firestore.rules` en el proyecto real**. Desde una sesión administradora con Firebase CLI:
   ```bash
   firebase login
   firebase deploy --only firestore:rules --project arcana-arena-f2997
   ```
   O pegar las reglas en Firebase Console > Firestore Database > Rules y pulsar Publish. Cambiar el archivo en GitHub no publica reglas automáticamente.
5. No insertar `service-account.json` ni secretos GitHub en el cliente o APK.

## Cómo construir

- GitHub Actions compila el plugin Android nativo desde `android/firebase-plugin`, copia `ArcanaFirebase.aar` al addon Godot y exporta APK con build Gradle.
- El script de Windows `Compilar-APK.cmd` descarga Gradle 8.9 cuando hace falta y compila automáticamente el plugin. **Esta APK local usa la clave de depuración**: para probar Google Login, también debes registrar su SHA-1 en Firebase o instalar una APK de **GitHub Release firmada con la clave permanente**.
- La firma de Release requiere `ARCANA_KEYSTORE_BASE64`, `ARCANA_KEYSTORE_PASSWORD`, `ARCANA_KEY_ALIAS` y `ARCANA_KEY_PASSWORD` ya configurados en GitHub. La canalización publica la versión firmada solo desde una etiqueta `v0.17.3`.

## Prueba funcional obligatoria en teléfono real

1. Instalar la APK **firmada con certificado registrado**.
2. Apagar el servidor C# completamente y abrir **Mi perfil**. Entrar con Google desde el selector nativo.
3. La primera vez, verificar en Firestore `users/{uid}` que oro, victorias y derrotas son 0 y los objetos están vacíos.
4. Con el servidor aún apagado, comprobar la lectura de oro, perfil e inventario, el cambio entre piezas ya adquiridas y su persistencia al reiniciar.
5. Salir de la aplicación sin cerrar sesión y abrirla de nuevo. La sesión debe restaurarse automáticamente.
6. Pulsar **Cerrar sesión**, reabrir la app y confirmar que no queda una cuenta activa.
7. Con el servidor C# encendido y configurado con permisos Firebase Admin, entrar a PvP y verificar que acepta un ID token válido y rechaza ID tokens falsificados o de otro proyecto.
8. Verificar que los clientes no pueden actualizar oro, inventario ni victorias por Firestore REST: deben recibir `PERMISSION_DENIED`. Las recompensas reales se conceden desde el servidor.

## Diagnóstico

- **Google no abre**: comprobar que se instaló APK con `ArcanaFirebasePlugin` incluido; verificar Google Play Services, app Android y SHA del certificado.
- **Error de desarrollador / OAuth**: comprobar el cliente OAuth web y que el certificado de firma corresponda a la huella Android registrada en Firebase.
- **Google conecta pero Firestore rechaza**: publicar las reglas de esta versión y verificar que el proveedor Google está habilitado.
- **Venta o recompensas bloqueadas**: es esperado sin servidor C# configurado; la sesión y la lectura del inventario deben seguir funcionando.
- Los datos originales del servidor de cuentas y sus credenciales privadas no se eliminan. El lector directo no necesita `account-server.json` para Google.

## Pruebas automáticas y limitaciones

GitHub Actions verifica compilación .NET y Java, empaquetado y presencia del plugin en la APK. No puede validar una sesión Google real, el estado de Firestore remoto ni los permisos administradores del proyecto sin identidad conectada. La versión no se considera validada en dispositivo hasta superar los ocho pasos anteriores.
