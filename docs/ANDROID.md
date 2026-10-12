# Arcana Arena para Android

Descargar el [código de main](https://github.com/andresmartinezmelgar/magicka/archive/refs/heads/main.zip), extraerlo y abrir **`Compilar-APK.cmd`** desde Windows. Instalar `artifacts/android/ArcanaArena-v0.15.0-Android-arm64.apk` cuando termine. Android puede pedir autorización a la aplicación usada para abrir la APK. Requiere Android 8 o posterior y procesador ARM64. En el móvil no hace falta instalar Godot ni .NET. Si Android rechaza actualizar una APK de desarrollo anterior porque tiene otra firma, desinstalar la anterior antes de instalar esta versión.

El juego se abre en horizontal. **Práctica** funciona sin conexión y permite elegir mapa, modo y tipo de mago, o entrar en el laboratorio de estados, barreras y hechizos. La APK incluye el motor, los sprites, los sonidos y el runtime C#.

En v0.12.14, los puntos de control usan seis círculos mágicos transparentes con sus colores originales y progreso luminoso. La compilación comprueba todas las licencias del mismo SDK antes de continuar.

En v0.12.13, tocar y arrastrar desde cualquier punto libre de la mitad izquierda activa el movimiento. Los botones tienen prioridad; el primer dedo conserva el joystick hasta soltarse. [Perfil gráfico y medición por USB](RENDIMIENTO.md).

En v0.12.12, el joystick conserva la intensidad del gesto y Traslación usa el último destino. Las barreras reducen sus mallas y la compilación Bash conserva la APK anterior si falla la validación.

En v0.12.11, morir y reaparecer cancela el destino anterior y los dedos del joystick. La siguiente orden parte de la reaparición. Escudo básico, puntos de captura y campos del suelo tienen rellenos y bordes más visibles.

En v0.12.9, agua azul, frío turquesa, rayo violeta y arcano magenta usan tonos más definidos. Los botones llevan símbolos y bordes más gruesos; el fondo más opaco y la iluminación más neutra mejoran la distinción de los elementos. El movimiento y los pasos usan la misma interpolación que el escritorio.

## Controles

| Control | Acción |
| --- | --- |
| Mitad izquierda | Tocar y arrastrar para mover. El joystick aparece donde apoyas el dedo; soltar detiene al mago. |
| Ocho botones de elementos en el borde derecho | Preparar hasta tres cargas, incluidos elementos repetidos. |
| Joystick derecho | Apuntar y lanzar mientras se mantiene el dedo. Soltar interrumpe los rayos o lanza la roca cargada. |
| Sobre ti | Curación, escudo, guarda o secado. |
| Área | Pulso alrededor del mago. |
| Bastón | Ataque cuerpo a cuerpo. |
| Vaciar | Quitar los elementos preparados. |
| Celeridad, Traslación, Meteoro y Reanimar | Magias especiales; muestran su coste o recarga. |
| Ayuda | Reglas y las 120 combinaciones con sus cifras de daño. |
| Tabla | Clasificación. |
| Menú / Atrás | Pausa y salida de la partida. |

Los elementos se muestran en dos columnas y cuatro filas encima del joystick derecho, con botones más grandes y el minimapa a su izquierda. Las tres casillas de elementos preparados y la barra de carga quedan justo encima de los cuatro hechizos, alineados al borde inferior para dejar libre el centro. Los controles admiten varios dedos simultáneos. La zona izquierda excluye los botones; cruzar al lado derecho con el dedo de movimiento no cambia su función. Soltar fuera del joystick también detiene la acción. Abrir la ayuda, abrir el menú o enviar la app a segundo plano cancela los movimientos y las cargas activos.

El láser ofensivo empieza con el 30 % del daño original. Mantener el joystick durante 2 s completa la carga y eleva el daño al 150 %. La barra inferior se llena del 0 al 100 %; el daño permanece al máximo mientras se mantiene el lanzamiento. Soltar, cambiar de hechizo, congelarse o morir reinicia la carga. Curación, quemadura y frío mantienen sus valores.

Las barreras combinadas muestran sus elementos: roca con llamas, cascadas de agua, arcos eléctricos, hielo o vapor. El meteorito tiene roca incandescente, estela y un aviso que marca su radio de impacto de 4 m; al caer proyecta fragmentos, humo y ondas breves.

## Partidas con PC

Iniciar el servidor en Windows o Docker. Conectar el móvil a la misma red y usar **Por IP** con la dirección del PC, por ejemplo `192.168.1.20:27015`. **LAN** busca servidores por UDP 27016; si la red bloquea descubrimiento, sigue disponible la dirección IP.

La APK puede consultar, crear y entrar en las salas del servidor. Usa protocolo 12; cliente y servidor deben actualizarse juntos a v0.13.0 o posterior. Permite elegir Azul, Rojo o Automático al crear o entrar en una sala. Una barra muestra la preparación del mapa y los efectos antes de habilitar los controles; las salas nuevas esperan a sus jugadores. La ayuda incluye la tabla de ventajas y debilidades elementales. El escudo básico deja pasar los hechizos de su propietario. El alojamiento del proceso de servidor se mantiene en PC y Docker; el móvil ejecuta las prácticas localmente.

## Compilación y comprobaciones

### Windows con doble clic

Extraer el código completo y abrir **`Compilar-APK.cmd`** en la raíz desde Windows 10/11 x64. El script descarga las herramientas oficiales que falten en `.tools/`, sin necesitar Android Studio, Visual Studio ni permisos de administrador. La primera vez necesita Internet y varios GB libres; el SDK muestra sus licencias en la consola para aceptarlas con `y` y Enter, primero en la revisión y después en cada licencia. Una comprobación final confirma que no queda ninguna pendiente.

La APK ARM64 queda en **`artifacts/android/ArcanaArena-v0.15.0-Android-arm64.apk`**, junto con `SHA256SUMS-Android.txt` y el manifest comprobado. El script verifica firma, versión, paquete, Android 8+, permiso de red y bibliotecas Godot/C#. Solo sustituye la APK después de comprobarla; si falla, conserva la anterior. Los registros se guardan en `artifacts/compilacion-apk-*.log`. Los scripts Windows y Bash comparten un bloqueo por carpeta de proyecto: una segunda compilación se rechaza antes de modificar presets o herramientas. El archivo `.tools/arcana-build.lock` permanece; el bloqueo se libera al terminar el proceso.

`VERSION` determina el nombre de la APK, la versión C#, `version/name` y `version/code` de Android. Los scripts actualizan los presets temporalmente antes de exportar y comprueban ambos valores en la APK generada. El código se calcula como `major × 1.000.000 + minor × 1.000 + patch`, con minor y patch entre 0 y 999; v0.13.0 usa el código 13000, superior al 134 anterior.

La firma de desarrollo se conserva en **`.tools/android-debug.keystore`**, excluida de Git. Mantener ese archivo permite actualizar las APK generadas en este equipo sin desinstalar el juego. Una APK de la Release puede usar otra firma. El script restaura los presets del proyecto y usa una configuración de Godot temporal.

Las rutas con espacios y paréntesis, como `magicka-main (1)`, se admiten también al comprobar licencias e instalar paquetes. El script ejecuta directamente la clase Java oficial de `sdkmanager`: evita el fallo `No se esperaba ... java.exe` del lanzador `.bat`. Si se usa una copia anterior, actualizar `scripts/auto-compile-apk.ps1` y `scripts/android-tools-support.ps1` juntos. Se conserva `.tools/` y su firma de desarrollo.

Si una instalación anterior dejó incompleto `cmdline-tools/latest`, el script la repara automáticamente. Un ZIP dañado en la caché se vuelve a descargar. No hace falta borrar `.tools/` ni la firma local. Las herramientas se extraen creando cada carpeta, sin usar copias con comodines.

La consulta conocida de `EditorSettings` al cerrar Godot .NET se muestra como `AVISO GODOT`; no implica por sí sola un fallo de exportación ([incidencia del motor](https://github.com/godotengine/godot/issues/84839)). Otros errores, las salidas no válidas y los códigos distintos de cero detienen la compilación.

También se puede ejecutar desde PowerShell y reutilizar herramientas propias:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/auto-compile-apk.ps1
# Opcional: indicar herramientas existentes o generar para un emulador.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/auto-compile-apk.ps1 -GodotPath "C:\Godot\Godot_v4.5.2-stable_mono_win64_console.exe" -JavaHome "C:\Java\jdk-17" -AndroidSdkPath "C:\Android\Sdk" -Architecture x86_64
```

`-AcceptAndroidLicenses` acepta las licencias sin interacción, para un entorno automatizado que ya autorice esa aceptación. Por defecto se muestran y se solicita respuesta. Estas APK usan firma de desarrollo para instalación directa.

Si aparece **`7 of 7 SDK package licenses not accepted`**, no se han aceptado los siete términos. El script se detiene antes de compilar aunque sdkmanager devuelva código cero. Para aceptar todos expresamente al iniciar la compilación:

```powershell
.\Compilar-APK.cmd -AcceptAndroidLicenses
```

La aceptación se guarda en el SDK de `.tools/android-sdk` o en el indicado mediante `-AndroidSdkPath`; una aceptación hecha en otro SDK no sirve para ese directorio.

### Linux

Godot 4.5.2 .NET, SDK .NET 8, OpenJDK 17, SDK Android con plataforma y build-tools 35.0.0 y plantillas de exportación .NET de Godot. El proyecto conserva `net8.0`; Godot selecciona el runtime Mono Android al exportar.

```bash
dotnet build src/Arcana.Client/Arcana.Client.csproj -c Debug
bash scripts/package-android.sh /ruta/al/godot-mono
```

`JAVA_HOME` señala al JDK y `ANDROID_HOME` al SDK Android. El script genera una APK firmada con una clave de desarrollo para instalación directa, verifica firma, paquete, versión, permisos y bibliotecas ARM64, y escribe `SHA256SUMS-Android.txt`. La clave de desarrollo no se incorpora al repositorio. Los binarios de nuevas compilaciones pueden tener una clave distinta; para actualizar conservando la instalación, reutilizar una clave propia mediante el flujo de exportación de Godot.

La integración continua prueba controles y ciclo de vida con Godot, captura launcher, partida y catálogo en OpenGL, verifica la APK ARM64 y ejecuta la misma app exportada en un emulador Android 15 x86_64. Solo publica la versión cuando pasan también Linux, Docker y Windows.

El inventario y el acceso Google se abren desde el launcher y la sala. Los objetos también se utilizan contra IA con la caché local. La venta por oro requiere sesión en el servidor central. El acceso utiliza el navegador del sistema; el servidor debe tener Firebase y HTTPS configurados. [Cuentas y progresión](PROGRESSION.md).
