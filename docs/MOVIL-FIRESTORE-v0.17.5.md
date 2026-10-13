# Verificación móvil y solución del error 403 Firestore (v0.17.5)

## Causa del mensaje «Firestore ha rechazado la lectura»

La autenticación con Google y el acceso a Firestore son operaciones diferentes. El SDK de Firebase Authentication puede haber iniciado sesión correctamente y, aun así, Firestore devolver `403 PERMISSION_DENIED` cuando **las reglas publicadas en el proyecto no autorizan la lectura**. Los tests del repositorio se ejecutan en un emulador: **no despliegan las reglas al proyecto real**.

El cliente de la versión 0.17.5 muestra ahora qué operación ha sido denegada:

- `users/{tu cuenta}`: lectura del perfil, primera solicitud de la aplicación.
- `users/{tu cuenta}/loadouts/active`: equipamiento seleccionado.
- `lista de objetos de tu inventario`: colección de objetos.
- `creación del perfil inicial`: alta de una cuenta nueva con saldos cero.

No se registra ni expone el token de identidad.

## Una única configuración necesaria en Firebase real

1. Accede con la cuenta administradora a [Firestore Database > Reglas](https://console.firebase.google.com/project/arcana-arena-f2997/firestore/rules).
2. Comprueba que trabajas en el proyecto **arcana-arena-f2997** y la base de datos **(default)**.
3. Copia el contenido completo de `firebase/firestore.rules` del mismo commit que la app y pulsa **Publicar**. Guardar el archivo en GitHub no actualiza las reglas desplegadas.
4. En **Authentication > Métodos de inicio de sesión**, comprueba que Google está activado y la aplicación Android pertenece al mismo proyecto.
5. En Arcana Arena abre **Mi cuenta** y pulsa **Reintentar acceso a Firebase**. No es necesario desinstalar, reiniciar ni volver a seleccionar la cuenta de Google para volver a probar.

La regla `googleOwner(uid)` únicamente permite a la cuenta de Google autenticada leer su propio perfil e inventario; la cabecera nueva nace con cero oro y cero premios, y la selección de equipamiento verifica pertenencia. Las operaciones privilegiadas siguen siendo responsabilidad del servidor C#; **no** activar reglas globales `allow read, write: if true` para solucionar el 403.

Si persiste el error con reglas publicadas, anota el estado exacto (401, 403 o 404) y la operación mostrada. 401 significa sesión no válida; 404 al crear el perfil apunta a proyecto o base de datos inexistente; 403 significa autorización/servicio rechazado. No compartas ID tokens ni credenciales privadas.

## Revisión de pantallas móviles

- **Inicio:** botones táctiles con altura mínima de 68 unidades de interfaz y texto ampliado.
- **PvP:** pestañas Por IP/LAN; botones **Volver**, **Ver salas**, **Cómo jugar** y **Salir** permanecen visibles mientras se desplazan las opciones.
- **Versus IA:** selecciones de mapa/modo, botones +/- de bots de al menos 88×70, acciones **Volver** y **Entrenar con bots** fijas; el contenido se desplaza.
- **Salas PvP:** lista y formulario de creación desplazables; **Entrar**, **Crear sala**, **Actualizar**, **Crear y entrar**, **Volver a salas** y **Volver al launcher** quedan anclados en la franja inferior según el panel activo.
- **Pruebas:** `--mobile-menu-smoke` ejecuta una comprobación geométrica de las acciones visibles contra el rectángulo del viewport y aborta CI si alguno queda fuera. No sustituye pruebas táctiles en un teléfono real.

Validación manual: en un teléfono Android real recorrer PvP > LAN > salas, crear sala, Versus IA > seleccionar mapa > bots y jugar, y comprobar que los controles fijos no quedan tapados por gestos ni barras del sistema.
