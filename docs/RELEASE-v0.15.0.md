# Arcana Arena 0.15.0

Durante una partida se puede consultar el equipamiento de cualquier mago de la sala desde **Clasificación → Ver bonos de los magos**. La ficha muestra botas, gorro, pecho y arma, calidad y familia, atributos individuales, conjuntos de dos o cuatro piezas y los totales Piezas / Conjunto / Suma / Aplicado. Permite cambiar de jugador y actualizar la lista. La clasificación admite desplazamiento en salas grandes para mantener accesible el botón en móvil.

El servidor prepara la ficha al cargar el equipo y devuelve una copia pública del equipo activo en la ronda. Las consultas no recorren Firestore ni amplían los snapshots. Las respuestas se envían de forma fiable por la conexión de esa sala; una consulta no puede acceder a otra sala aunque coincidan sus IDs de mago. No se publican la UID, los IDs de objetos, el inventario sin equipar, el oro ni los recibos de recompensas. Consultar la ficha detiene los controles propios; la partida continúa.

## Google Login y Firebase

El launcher incorpora **Entrar con Google**. La página de acceso permite seleccionar cuenta y explica los errores de conexión, proveedor o dominio pendiente y ventanas emergentes bloqueadas. Solo habilita el botón tras cargar Firebase, mantiene la sesión del navegador en memoria y la cierra al completar el intercambio con el servidor. El cliente valida que la página y el secreto de acceso correspondan al servidor elegido.

Se puede configurar el servidor con `ARCANA_FIREBASE_CONFIG`, un archivo JSON con `projectId`, `apiKey`, `authDomain` y `publicUrl`. Las variables no vacías prevalecen sobre él. [Plantilla](../firebase/server.example.json). La credencial privada de Firebase Admin permanece separada, fuera del juego y del repositorio, mediante credenciales de aplicación de Google Cloud o `GOOGLE_APPLICATION_CREDENTIALS`.

El acceso Google real requiere un proyecto con el proveedor Google, una app web, Firestore y el dominio del servidor autorizados. Esta versión no activa por sí misma recursos de producción ni modifica el alojamiento. [Configuración y persistencia](PROGRESSION.md).

## Compatibilidad y comprobaciones

Cliente y servidor usan **protocolo 14** y deben actualizarse juntos. Versión Android **15000**. Los objetos, sus intervalos, el esquema de cuenta y la versión de generación se conservan; los sprites no cambian.

- Compilación del núcleo, servidor, pruebas y cliente Godot Debug/Release.
- 181 comprobaciones generales de reglas y transporte, 17 de cuentas con Firestore Emulator y servidor real, y 21 de red real: 219 correctas.
- Pruebas Godot de fichas públicas en escritorio y móvil, HUD y controles móviles; inventario, recuperación en un segundo proceso, ocho familias, sprites, laboratorio y combinaciones.
- Revisión OpenGL de la ficha en escritorio y móvil y del botón Google en el launcher.
- Sintaxis del JavaScript de acceso, YAML de CI y metadatos de exportación comprobados. Seis pruebas de soporte de versiones correctas; dos opcionales requieren PowerShell o el CLI .NET disponible en el entorno.

El flujo HTTP de cuenta se verifica con un proveedor de identidad de pruebas y el emulador. La autorización Google y Firebase de producción necesita una comprobación con el proyecto configurado. La validación anterior no genera una APK, ejecutable Windows ni una Release.
