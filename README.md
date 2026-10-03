# Arcana Arena — v0.1.0

Primera recreación jugable en C# del combate elemental de **Magicka: Wizard Wars**. Cliente 3D de escritorio con **Godot 4.5.2 .NET**, servidor independiente **ASP.NET Core / .NET 8**, Docker y launcher con conexión por IP, búsqueda LAN y práctica local.

El código, las mallas y los sonidos son propios. Esta versión reproduce la estructura del combate; los gráficos, mapas, magicks y valores de daño son una aproximación, no una reproducción exacta del original.

![Launcher de la versión 0.1.0](docs/images/launcher.png)

![Práctica local en el cliente 3D](docs/images/game.png)

## Qué se puede jugar

- Ocho elementos y hasta tres elementos por hechizo. Las combinaciones se resuelven por reglas, no por un catálogo de unos pocos hechizos.
- Rayos continuos, chorros en cono, arcos eléctricos, rocas con carga, fragmentos de hielo, vapor, muros, minas y lanzamientos en área.
- Curación, escudos, guardas elementales, empujes, mojado, quemaduras, frío y congelación. Fuego amigo configurable.
- Duelo, todos contra todos y guerra por tres puntos de control, con tickets, muerte, reaparición y reinicio de partidas.
- Cuatro magicks: celeridad, traslación, meteoro y reanimación. Coste de energía y tiempos de reutilización.
- Bots que combaten, se curan y capturan puntos. Práctica local sin iniciar Docker.
- Launcher: nombre, equipo preferido, IP/dominio, contraseña opcional y descubrimiento automático UDP en LAN.
- Servidor autoritativo a 60 pasos/s, envío de estado a 20 Hz y representación interpolada en el cliente.

## Ejecutar el cliente en Windows

1. Instalar el **SDK .NET 8 x64**: <https://dotnet.microsoft.com/download/dotnet/8.0>.
2. Descargar **Godot 4.5.2 .NET** (la edición con soporte C#, llamada `mono` en los archivos): <https://github.com/godotengine/godot/releases/tag/4.5.2-stable>.
3. Descargar o clonar este repositorio. Desde su raíz:

```powershell
dotnet build ArcanaArena.sln -c Debug
```

4. En Godot, importar `src/Arcana.Client/project.godot` y pulsar **F5**.
5. En el launcher, abrir **Práctica** para jugar con bots, **Por IP** para conectar directamente o **LAN** para buscar servidores.

Visual Studio abre `ArcanaArena.sln`. El cliente se ejecuta desde Godot; el servidor y las pruebas se pueden ejecutar desde Visual Studio o la consola.

## Iniciar el servidor con Docker

Desde la raíz del repositorio:

```sh
docker compose up -d --build
```

En el launcher, introducir la IP de ese equipo, por ejemplo `192.168.1.20:27015`. Si servidor y cliente están en el mismo PC: `127.0.0.1:27015`.

```sh
docker compose logs -f server
docker compose down
```

Para configurar nombre, modo, bots, contraseña o puertos, copiar `.env.example` a `.env` y editarlo. El archivo `.env` queda excluido de Git.

```dotenv
SERVER_NAME=Partida de Andrés
GAME_MODE=warfare
BOTS=2
MAX_PLAYERS=8
SERVER_PASSWORD=
```

LAN por broadcast en un servidor **Linux**:

```sh
docker compose -f compose.lan.yaml up -d --build
```

Esta variante utiliza la red del host. Ejecutar una sola variante a la vez. En Docker Desktop, la conexión por IP local funciona con `compose.yaml`; el descubrimiento por broadcast depende de cómo Docker y la red del equipo encaminen UDP.

## Servidor sin Docker

```powershell
dotnet run --project src/Arcana.Server -c Release
```

No necesita Godot. El estado del servidor está en `http://IP:27015/health`; la información de partida está en `/info`.

| Uso | Protocolo | Puerto predeterminado |
|---|---|---:|
| Partida, saludo y estado | TCP / WebSocket | 27015 |
| Descubrimiento en la misma LAN | UDP | 27016 |

Para conectar desde Internet, el puerto TCP de la partida debe ser accesible en la IP pública del servidor. El descubrimiento LAN se queda en la red local. El launcher admite `wss://dominio/play` si se configura un proxy con TLS.

## Controles

| Acción | Entrada |
|---|---|
| Agua / vida / escudo / frío | Q / W / E / R |
| Rayo / arcano / tierra / fuego | A / S / D / F |
| Caminar hacia el cursor | Clic izquierdo |
| Movimiento directo | Flechas |
| Lanzar; mantener rayos/chorros o cargar tierra | Mantener clic derecho |
| Interrumpir o soltar la roca cargada | Soltar clic derecho |
| Lanzar sobre el propio mago | Botón central |
| Lanzar en área | Mayús + clic derecho |
| Golpe de bastón | Mayús + clic izquierdo |
| Celeridad / traslación / meteoro / reanimación | 1 / 2 / 3 / 4 |
| Vaciar elementos / zoom | Retroceso / rueda |
| Clasificación / ayuda / menú | TAB / F1 / ESC |

Ejemplos: **FFF** fuego; **SSS** rayo arcano; **DDF** roca incendiaria; **QR** hielo; **QF** vapor; **WWW + central** curación; **E + central** escudo; **EF + central** guarda de fuego.

Se anulan fuego/frío, vida/arcano, agua/rayo, tierra/rayo y dos escudos. Un mago mojado recibe más daño eléctrico; agua y frío pueden congelar; fuego sobre ti elimina mojado y frío cuando todavía puedes actuar.

## Código

| Proyecto | Responsabilidad |
|---|---|
| `src/Arcana.Core` | Elementos, combinaciones, simulación, geometría, protocolo, cliente de red y descubrimiento LAN |
| `src/Arcana.Server` | Conexiones, colas, validación, temporizador autoritativo y descubrimiento UDP |
| `src/Arcana.Client` | Launcher, entrada, escenario 3D, HUD y sonidos sintetizados |
| `tests/Arcana.Tests` | Pruebas de reglas y pruebas con conexiones reales |

Para leer el código: `Elements.cs` → `Entities.cs` → `GameWorld.cs` → `Protocol.cs` → `GameService.cs` → `Main.cs`.

## Pruebas

```powershell
dotnet run --project tests/Arcana.Tests -c Release
```

Pruebas con un servidor ya iniciado:

```powershell
dotnet run --project tests/Arcana.Tests -c Release -- --server=127.0.0.1:27015 --discovery-port=27016
```

Si se usa contraseña, definir `TEST_PASSWORD` en el proceso de pruebas. El cliente y el servidor deben ejecutar la misma versión del protocolo.

En la preparación de v0.1.0 pasaron **19 pruebas**: 15 de reglas y cuatro de red, incluidas dos conexiones reales, descubrimiento UDP, rechazo de versión incompatible y contraseña incorrecta. El cliente se compiló con los generadores de Godot y arrancó una práctica de cuatro magos durante 240 pasos en modo headless.

El workflow de GitHub incluye la compilación normal, las pruebas de red, el arranque headless y dos modalidades Docker: puertos publicados para IP directa y red del host para descubrimiento LAN en Linux. Su resultado aparece en **Actions**.

## Alcance de esta primera versión

El núcleo PvP, el launcher y el servidor están implementados. El mapa, los modelos, la animación y el balance son propios. Esta versión usa WebSocket sobre TCP y suavizado del estado; no incorpora predicción con reconciliación, compensación de latencia, matchmaking público, cuentas, persistencia de estadísticas ni las tablas completas de balance y equipo del original. La preferencia de equipo se respeta cuando permite mantener equipos equilibrados.

Documentación adicional: [servidor y redes](docs/SERVIDOR.md), [arquitectura](docs/ARQUITECTURA.md), [exportar el ejecutable](docs/EXPORTAR.md), [cambios de versión](CHANGELOG.md).
