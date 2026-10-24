# Arcana Arena

**Juego multijugador de combate mágico en tiempo real para Windows y Android.**

**Estado del repositorio:** versión estable **0.25.4**, Godot **4.5.2 .NET**, **C# / .NET 8** y protocolo de red **27**. El servidor autoritativo está desarrollado con ASP.NET Core. El juego puede practicarse sin conexión, alojarse en un PC o utilizar un servidor dedicado. El acceso Google y la progresión se apoyan en Firebase, cuando están configurados.

**Supervivencia en Anillos está integrado en `main`.** Se distribuye junto a los modos PvP en la versión estable. Las cuentas autorizadas conservan el [canal de pruebas](docs/CANALES-ACTUALIZACION.md). [Funcionamiento y reglas de Supervivencia](docs/DEFENSA-OLEADAS.md).

Proyecto inspirado en el combate elemental de *Magicka: Wizard Wars*, con código, escenarios, animaciones, efectos y reglas propios. No es una reproducción oficial ni exacta del juego original.

**Enlaces rápidos:** [Descargar última versión](https://github.com/andresmartinezmelgar/magicka/releases/latest) · [Todas las versiones publicadas](https://github.com/andresmartinezmelgar/magicka/releases) · [Acciones y pruebas](https://github.com/andresmartinezmelgar/magicka/actions) · [Historial técnico](CHANGELOG.md)

## Qué se puede hacer

| Funcionalidad | Estado actual |
| --- | --- |
| **PvP online** | Salas independientes en un servidor Windows o dedicado; conexión por LAN, IP o WSS seguro. |
| **Versus IA** | Partidas locales contra bots sin levantar un servidor. |
| **Laboratorio** | Práctica libre de hechizos, barreras, estados y daños contra objetivos de prueba. |
| **Google y perfil** | Inicio de sesión con Firebase Authentication en Windows y Android; perfil e inventario en Firestore. |
| **Inventario y equipación** | Cuatro piezas equipables, atributos, calidades y bonificaciones por conjuntos; caché para practicar sin conexión. |
| **Recompensas PvP** | Implementadas en el servidor, **condicionadas a Firebase configurado y a una partida válida**. |
| **Actualizaciones** | Comprobación de GitHub Releases, descarga con aceptación del jugador, instalación y parches diferenciales cuando son compatibles. |

El inicio muestra cuatro accesos principales en **2 × 2**: **PvP, Versus IA, Mi perfil y Cómo jugar**. La navegación inferior permite acceder además a **Inventario** y a las demás secciones. La cabecera muestra el nombre y el estado de Google; los detalles de estadísticas y oro se consultan en el perfil o el inventario.

### Combate

- **Ocho elementos:** agua, vida, escudo, frío, rayo, arcano, tierra y fuego; hasta tres elementos seleccionados por lanzamiento.
- Hechizos en cono, rayos continuos, roca cargada, hielo, vapor, barreras, minas, áreas, curación, escudos y guardas. Hay afinidades, debilidades, efectos de estado e interacciones entre proyectiles y haces.
- **Cinco tipos de mago:** equilibrado, piromante, criomante, tempestario y geomante, con especialidades y debilidades.
- Magicks de celeridad, traslación, meteoro y reanimación, con energía y reutilización.
- Movimiento, colisiones, reapariciones, minimapa, clasificación, indicadores de daño y efectos visuales. La simulación decisiva se ejecuta en el servidor.
- Equipación: **botas, gorro, pecho y arma**, con estadísticas propias y bonificaciones que modifican el combate. Otros jugadores pueden consultar la equipación y las bonificaciones visibles de un mago.

### Modos de juego

| Modo | Objetivo |
| --- | --- |
| **Duelo** | Dos jugadores o un jugador contra IA; ganar por bajas según el límite configurado. |
| **Todos contra todos** | Cada mago compite por alcanzar el límite de bajas. |
| **Guerra** | Capturar y defender puntos; las condiciones de victoria dependen del mapa. |
| **Supervivencia** | Resistir con dos vidas por jugador en Anillos, con uno a cuatro humanos o bots aliados y colosos a intervalos variables. |
| **Laboratorio** | Probar hechizos y estados sin una partida competitiva. |

**Mapas jugables:** Arena clásica, Ciudadela de los pasillos, Coliseo de la ascensión, Santuario de las reliquias, Frente de conquista y Anillos de supervivencia. El laboratorio dispone de su propio escenario de pruebas. Ciudadela requiere mantener el control total; Frente de conquista obliga a capturar cinco puntos en orden y utiliza portales, meteoros y muros destructibles. [Detalles de los escenarios](docs/MAPAS.md) · [Reglas de victoria](docs/COMO-GANAR.md).

## Descargar e instalar

Lo más sencillo es descargar los **paquetes completos** de la [última Release](https://github.com/andresmartinezmelgar/magicka/releases/latest), no el código fuente:

| Plataforma | Archivo | Requisitos |
| --- | --- | --- |
| **Windows** | `ArcanaArena-vVERSION-Windows-x64.zip` | Windows 10/11 x64 y gráficos compatibles con OpenGL 3.3. |
| **Android** | `ArcanaArena-vVERSION-Android-arm64.apk` | Android 8 o posterior, procesador ARM64 y uso horizontal. |

**Windows:** extrae **todo** el ZIP en una carpeta y ejecuta `ArcanaArena.exe`. Mantén juntos el ejecutable, sus datos y la carpeta `server/`. El paquete incluye el servidor y los runtimes necesarios; no hace falta instalar Godot, .NET ni Docker para jugar.

**Android:** instala la APK completa. Al actualizar debe conservarse la misma firma que la aplicación instalada; la versión de Release utiliza el certificado de distribución. En el primer uso Android puede solicitar permisos de instalación.

Los archivos con `from-vVERSION` o extensión `.delta` son **parches para actualizar desde una versión concreta**: normalmente los gestiona el actualizador del juego, no se instalan manualmente como una APK nueva.

## Jugar

### Contra la IA o practicar

Abre **Versus IA**, selecciona mapa, modo y número de bots y comienza. No necesitas Internet, cuenta Google ni un servidor. El **laboratorio de hechizos** también funciona localmente.

### Alojar una sala PvP desde Windows

1. Abre **PvP → Alojar** y pulsa **Iniciar servidor y ver salas**. El servidor incluido se ejecuta en el propio ordenador.
2. Pulsa **Crear sala**. Elige **Duelo, Guerra o Todos contra todos**, mapa, plazas, bots, opciones y contraseñas opcionales.
3. En Duelo o Guerra puedes escoger **partida única** o serie **al mejor de 5, 7 o 9**.
4. En las series se abre una **sala de espera** con equipos azul y rojo: cada jugador elige equipo y pulsa **Estoy preparado**. El anfitrión pulsa **Comenzar partida** cuando corresponde. Hay botón **Salir de la sala**.
5. Al finalizar una serie puede prepararse una nueva en la sala, con una nueva confirmación de jugadores.

Las contraseñas del **servidor** y de la **sala** son independientes. Crear la sala no implica que los invitados comiencen a jugar inmediatamente.

### Entrar desde otro PC o desde Android

- **LAN:** busca el servidor en la misma red Wi-Fi/LAN, abre su lista de salas y selecciona una.
- **Por IP:** escribe la IP y puerto del servidor, o una dirección segura `wss://dominio/play`.
- **Servidor online con Google · conexión automática:** el móvil consulta Firestore para localizar la dirección WSS vigente del servidor público anunciado. No requiere copiar manualmente una URL temporal.

**Importante:** si una sala LAN tiene Firebase y exige Google, el cliente intenta utilizar su **misma instancia** mediante WSS verificado antes de enviar credenciales. No transmite el token de Google mediante una conexión LAN `ws://` sin cifrar. En servidores sin Firebase puede haber juego como invitado, pero sin premios persistentes.

El descubrimiento público actual está pensado para **el servidor anunciado en Firebase**, no es un buscador global de múltiples servidores ni un sistema de matchmaking.

## Google, Firestore e inventario

Se utiliza el proyecto Firebase **`arcana-arena-f2997`**. La autenticación Google funciona **independientemente del servidor de partidas**:

- **Windows:** OAuth de escritorio mediante el navegador. Si el paquete no trae su configuración pública de cliente OAuth, el juego permite importar el JSON OAuth de tipo «Aplicación de escritorio». La sesión puede recuperarse al volver a abrir el juego.
- **Android:** inicio de sesión nativo mediante Credential Manager y Firebase Authentication.
- **Firestore:** almacena el perfil, victorias/derrotas, oro, objetos y selección de equipación. El juego puede utilizar una **copia local del perfil** para prácticas sin conexión.
- Si Google aparece conectado pero Firestore muestra `403 PERMISSION_DENIED`, hay que revisar las **reglas publicadas**: autenticarse no equivale a tener permiso de lectura.

Las recompensas, las victorias y las operaciones que alteran oro u objetos se validan en el servidor. El cliente **no puede asignarse premios** escribiendo directamente en Firestore.

En **Inventario**, pulsa Botas, Gorro, Pecho o Arma para abrir sus objetos en dos o tres columnas, ordenados de mayor a menor calidad, con desplazamiento táctil. La selección y las bonificaciones se previsualizan al instante. Las cuatro posiciones se guardan juntas al salir del inventario; salir sin cambios no escribe en Firestore.

### Recompensas PvP

Para optar a recompensas deben cumplirse, entre otras, estas condiciones:

- Servidor con Firebase habilitado; ambos jugadores —o todos los integrantes de los equipos— con sesión Google válida.
- **Equipos humanos equilibrados** de 1 contra 1 hasta 4 contra 4, **sin bots**.
- Duelo o Guerra en una **serie válida**; el resultado se confirma desde el servidor.
- Al menos **15 segundos de actividad válida por participante** antes de terminar la serie.

El servidor registra el resultado en Firestore y concede un objeto al ganador (o su conversión en oro si corresponde). Si Firestore no está disponible temporalmente, conserva un **diario de recompensas pendientes** para reintentarlo. Que `/info` indique `rewardsAvailable: true` no garantiza por sí solo que ya se haya entregado un premio real.

### Preparar un servidor Windows con Firebase

Para que **tu PC aloje partidas con recompensas**, necesitas Google Authentication habilitado, Firestore `(default)`, reglas publicadas y una cuenta de servicio con permisos adecuados para Firestore y la configuración de Authentication.

En Windows, los archivos privados permanecen **fuera de la carpeta del juego y de GitHub**:

```text
%APPDATA%\Godot\app_userdata\Arcana Arena\firebase\
  service-account.json
  server.local.json
```

Ejemplo de `server.local.json`:

```json
{
  "projectId": "arcana-arena-f2997",
  "apiKey": "API_KEY_PUBLICA_DE_FIREBASE",
  "authDomain": "arcana-arena-f2997.firebaseapp.com",
  "publicUrl": "http://127.0.0.1:27015",
  "autoPublicHosting": true
}
```

Sustituye **solo** la API key por la del proyecto. `service-account.json` es una credencial **privada de administración**: nunca se sube al repositorio, a una Release ni a un informe. Si se cambia el puerto del servidor, el puerto de la URL local inicial debe coincidir.

Con `autoPublicHosting: true`, **Windows** puede preparar un Cloudflare Quick Tunnel HTTPS, autorizar temporalmente su dominio y publicar la dirección `wss://…trycloudflare.com/play` en `publicAccountServer/current` de Firestore. La URL puede cambiar al reiniciar el servidor; el móvil la resuelve mediante Firebase. Es una solución **provisional para pruebas y partidas pequeñas**, no alojamiento permanente con disponibilidad garantizada.

[Configuración detallada de Firebase y pruebas de recompensas](docs/PROBAR-RECOMPENSAS-FIREBASE-WINDOWS.md) · [Google en Windows y reglas Firestore](docs/GOOGLE-WINDOWS-Y-FIRESTORE.md) · [Reglas del repositorio](firebase/firestore.rules).

## Actualizaciones desde la aplicación

Windows y Android consultan las **Releases oficiales** de GitHub. Cuando hay una versión más reciente, el juego muestra un aviso y permite elegir **Descargar actualización** o continuar jugando.

- **Perfil → Buscar actualización** permite consultar en cualquier momento y muestra la versión instalada, la ausencia de novedades o un error de conexión.
- Muestra tamaño, progreso y estado; la descarga puede cancelarse.
- Prefiere un **parche diferencial** compatible si reduce suficientemente la descarga; en caso contrario, descarga el paquete completo.
- Comprueba hashes SHA-256 y versión antes de instalar.
- En Windows, el instalador sustituye los archivos del juego, conserva los datos locales compatibles y permite revertir ante fallos.
- En Android, usa el instalador del sistema; Android puede requerir aprobación del usuario.
- No instala mientras hay una partida o un servidor local activo.

[Funcionamiento y formato de las actualizaciones](docs/actualizaciones-github-releases.md).

## Servidor y conectividad

El servidor es **autoritativo**: procesa combate, puntuación y resultados; el cliente representa los estados recibidos. La simulación funciona a **60 Hz** y el estado se transmite aproximadamente a **20 Hz**, con deltas y compresión cuando conviene.

| Servicio | Puerto predeterminado | Transporte |
| --- | ---: | --- |
| Partidas, salas y endpoints `/health`, `/info` y `/rooms` | **27015** | TCP, HTTP y WebSocket |
| Descubrimiento LAN | **27016** | UDP |

Puedes cambiar los puertos en el alojamiento Windows. `/health` devuelve el estado básico; `/info` informa, entre otros campos, si las recompensas están habilitadas.

### Servidor dedicado y Docker

No necesitas abrir Godot para ejecutar el servidor .NET. Desde el código fuente:

```powershell
dotnet run --project src/Arcana.Server -c Release
```

También hay un `Dockerfile` y dos opciones de Docker Compose:

```bash
docker compose up -d --build
# En Linux, para permitir descubrimiento por broadcast en LAN:
docker compose -f compose.lan.yaml up -d --build
```

El servidor Docker **no configura automáticamente** el túnel temporal de Firebase para Windows. Para partidas con recompensas en un alojamiento dedicado hay que configurar explícitamente autenticación, credenciales y HTTPS/WSS. El descubrimiento LAN no sustituye a una dirección pública segura.

[Documentación del servidor](docs/SERVIDOR.md) · [Arquitectura](docs/ARQUITECTURA.md).

## Controles

| Acción en Windows | Control |
| --- | --- |
| Agua / vida / escudo / frío | **Q / W / E / R** |
| Rayo / arcano / tierra / fuego | **A / S / D / F** |
| Movimiento | Clic izquierdo para desplazarse; flechas para movimiento directo |
| Lanzar, cargar o canalizar | Clic derecho (mantener/soltar según el hechizo) |
| Lanzar sobre uno mismo | Botón central |
| Lanzar en área | Mayús + clic derecho |
| Golpe de bastón | Mayús + clic izquierdo |
| Celeridad / traslación / meteoro / reanimación | **1 / 2 / 3 / 4** |
| Vaciar elementos | Retroceso |
| Clasificación / ayuda / pausa o menú | **TAB / F1 / ESC** |

La configuración del joystick y los botones está en **Mi perfil → Configurar joystick y botones**, disponible también como invitado. El tamaño, las posiciones, la zona muerta, la sensibilidad y el modo zurdo se guardan en este dispositivo.

En Android se utilizan **joysticks y botones táctiles** para movimiento, apuntado, elementos, lanzamiento y magicks. La ayuda dentro del juego explica combinaciones, objetivos y tipos de mago. [Guía Android](docs/ANDROID.md) · [Combinaciones](docs/COMBINACIONES.md).

## Desarrollo y compilación

### Tecnologías y estructura

Todas las pantallas y subpantallas de menú comparten la cabecera y la navegación inferior de `ArcanaMenuChrome` en móvil y escritorio. El contenido se adapta o se desplaza dentro del área central, sin ocultar ni tapar estas barras. El editor de posiciones utiliza el lienzo completo del HUD mientras se arrastran los controles.

| Ruta | Contenido |
| --- | --- |
| `src/Arcana.Core/` | Reglas, simulación, red, protocolo, elementos, equipo y lectura del perfil |
| `src/Arcana.Client/` | Godot 4.5.2 .NET, menús, HUD, renderizador, controles, Google y actualizador |
| `src/Arcana.Server/` | ASP.NET Core, partidas, salas, autoridad de combate y recompensas |
| `src/Arcana.AccountServer/` | Servicio de cuentas heredado, mantenido en el repositorio |
| `android/firebase-plugin/` | Plugins nativos Android para Firebase y actualizaciones |
| `firebase/` | Reglas de Firestore y configuración de ejemplo sin secretos |
| `tests/` | Pruebas funcionales, red, cuentas y regresiones |
| `scripts/` | Compilación, exportación, diagnósticos y pruebas |
| `.github/workflows/` | CI, paquetes Windows/Android y publicación |

**Requisitos para compilar:** SDK .NET 8, Godot 4.5.2 .NET, y para Android además JDK 17 y Android SDK 35. Los scripts Windows descargan y reutilizan las herramientas que falten.

Desde la raíz del repositorio en Windows:

```text
Compilar-Juego.cmd
Compilar-APK.cmd
```

El primero genera `Juego/ArcanaArena.exe` junto al servidor incluido. El segundo genera la APK en `artifacts/android/`. La primera compilación requiere Internet y espacio libre. **Las APK compiladas localmente pueden utilizar otra firma**: para conservar los datos instalados al actualizar, reutiliza la misma clave de firma.

### Pruebas

```powershell
dotnet build ArcanaArena.sln -c Release
dotnet run --project tests/Arcana.Tests/Arcana.Tests.csproj -c Release
```

GitHub Actions se ejecuta manualmente cuando lo indique el usuario. Subir código o abrir una PR no compila ni genera tags. El flujo comprueba código, simulación, partidas LAN, salas PvP, reglas y transacciones Firestore mediante emuladores, además de los paquetes Windows/Android, controles de interfaz y **capturas visuales automatizadas**. Para crear el tag y publicar una versión de pruebas también hay que activar `publish_preview`, desactivado por defecto.

Los test y las capturas automatizadas **no sustituyen** una partida entre dispositivos reales ni la comprobación de permisos de un proyecto Firebase desplegado. Para diagnosticar fallos utiliza **Ver registros** en el juego; los logs locales se almacenan habitualmente en:

```text
%APPDATA%\Godot\app_userdata\Arcana Arena\logs
```

## Límites actuales y documentación

- El proyecto es **jugable y está en desarrollo**. El equilibrio, rendimiento móvil, experiencia de red y distribución siguen sujetos a pruebas reales.
- El **Quick Tunnel de Cloudflare es temporal**, no garantiza URL ni disponibilidad fijas y el anuncio de Firestore identifica un único servidor público.
- **No hay matchmaking global** ni un servicio permanente de servidores públicos integrado.
- Firebase, OAuth de escritorio, los permisos IAM y las reglas desplegadas **requieren configuración externa** para utilizar Google y recompensas.
- Las versiones de cliente y servidor deben ser compatibles en **versión de juego y protocolo**.

Documentación temática: [cómo ganar](docs/COMO-GANAR.md) · [mapas](docs/MAPAS.md) · [equipación y progresión](docs/PROGRESSION.md) · [red](docs/NETWORK-OPTIMIZATION.md) · [diagnóstico](docs/DIAGNOSTICO.md) · [exportar](docs/EXPORTAR.md). El historial de cambios, cuando se necesite, se mantiene separado en [CHANGELOG.md](CHANGELOG.md).

Canal estable para cuentas generales y [versiones de prueba por UID autenticado](docs/CANALES-ACTUALIZACION.md).
