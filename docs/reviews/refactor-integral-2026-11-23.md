# Refactorización integral del repositorio

Fecha: 23 de noviembre de 2026. Base: `b4ff22eaf30afc33af4ab334b39aa37806a2960b`.

## Alcance

El inventario inicial contiene 615 archivos de código y automatización, incluidos 503 archivos C# y 3.016 métodos. La revisión se amplió a los 13 shaders/includes y dos scripts CMD, además de proyectos, recursos, reglas, dependencias y configuración. El inventario de código y automatización pasa de 630 a 705 archivos tras separar responsabilidades. El cambio afecta a 110 archivos: 77 nuevos y 33 existentes.

Se ha revisado la estructura de todos los módulos propios mediante inventario, análisis sintáctico, búsqueda de duplicación, resolución de referencias y revisión de los puntos de entrada y componentes principales. La lectura detallada se ha centrado en los componentes refactorizados y sus dependencias. Esto no constituye una auditoría funcional ejecutada de cada método. Los módulos pequeños y cohesionados no necesitan cambios por apariencia.

## Cambios por área

| Área | Refactorización aplicada |
| --- | --- |
| Núcleo | `GameWorld` separado en comandos, simulación, lanzamiento, áreas, daño, entidades, bots y ciclo de partida. El paso del mundo delega la actualización de jugadores manteniendo su posición original. Supervivencia separa aparición, IA y cierre/reinicio. |
| Cuentas y transporte del cliente | Autenticación y equipamiento separados en `AccountClient`; conexión/cierre y lectura/escritura separados en `ArenaConnection`. Descubrimiento LAN, catálogo, modificadores y sorteo tienen archivos propios. |
| Servidor de juego | Separación de conexiones, simulación, entrada, transporte y recompensas. La entrada mantiene la exclusión del simulador y el límite de 32 comandos por sesión y paso. `GameRoom` y `PlayerSession` tienen archivos propios. |
| Servidor de cuentas | Arranque y rutas internas separados. Ambos servidores comparten límites HTTP, contrato JSON y registro de servicios de cuentas mediante `ArcanaWebServices`; el proyecto heredado enlaza el archivo compartido. |
| Cliente Godot | `Main` separado en sesión, fotograma, entrada, pausa, diagnóstico y perfilado. Separación de presentación/estado/acciones en HUD, inventario, defensa, launcher y salas. Actualizaciones separadas en presentación, transferencia, instalación y verificación. Sesión y configuración de Google de escritorio separadas. |
| Utilidades de verificación del cliente | La representación de documentos Firestore utilizada por las pruebas de tienda y ventas comparte `FirestoreFixture`. El preflight de autenticación sigue los nuevos archivos y comprueba acciones del perfil en vez de un texto traducido obsoleto. |
| Android | `StoreProductQuote` concentra la selección de ofertas de compra única y planes prepago; catálogo y compra utilizan la misma selección. Revisadas las políticas de sesión y actualización, el estado de autenticación y las declaraciones de dependencias. |
| Dashboard | Esquema, validación, agregación, formato y exportación CSV separados. Reutilización de los formateadores numéricos de precisión habitual, con caché acotada a tres instancias. Instalación y actualización apuntan por defecto a `main`. |
| Actualizadores y scripts | `server_release.py` concentra la identidad de versiones estables de ambos actualizadores. Los instaladores copian el módulo junto al programa instalado. Verificada la ejecución aislada de la ayuda del actualizador instalado. |
| Pruebas C# | El runner principal delega casos en grupos de combate, mapas, protocolo, transporte, conexión, lobby y paquete. Helpers de simulación y flujo auxiliar independientes. Los argumentos, contadores y resultados permanecen en el runner. |
| Configuración y shaders | Revisados enlaces de proyectos, módulos JavaScript, includes de shaders, correspondencia de dependencias Gradle/Godot, reglas y disparadores de CI. Los shaders 2D/3D ya comparten sus cálculos mediante includes. |

## Tamaño de los puntos de entrada

Las líneas trasladadas siguen existiendo en sus módulos correspondientes. Estas cifras describen la separación de responsabilidades, no una reducción del trabajo de ejecución.

| Archivo | Antes | Después |
| --- | ---: | ---: |
| `src/Arcana.Core/GameWorld.cs` | 1.179 | 155 |
| `src/Arcana.Server/GameService.cs` | 721 | 118 |
| `src/Arcana.AccountServer/Program.cs` | 89 | 33 |
| `src/Arcana.Client/Scripts/Main.cs` | 786 | 195 |
| `src/Arcana.Client/Scripts/ReleaseUpdateNotice.cs` | 674 | 179 |
| `tests/Arcana.Tests/Program.cs` | 2.225 | 174 |

## Comprobaciones realizadas

- 126 pruebas ejecutadas y superadas: 29 JavaScript y 97 Python. Dos comprobaciones de versiones se omitieron: consulta de MSBuild y comparación opcional con PowerShell.
- Pruebas de exclusión entre procesos y liberación de bloqueos superadas, usando procesos Python temporales.
- Preflight estático de autenticación y coherencia de metadatos de versión superados.
- Análisis sintáctico de los 571 archivos C#, 16 Java, 15 JavaScript y 32 Python; `bash -n` sobre los 16 scripts shell; lectura de siete YAML, diez XML de proyectos/configuración y 33 JSON.
- Análisis de los 27 PowerShell: 23 aceptados por el parser utilizado. Cuatro scripts conservan limitaciones del parser que también aparecen en la base; ninguno de esos cuatro se modifica. La validación nativa de Windows sigue pendiente.
- Resueltas las importaciones relativas JavaScript, los archivos C# enlazados y las referencias entre proyectos. Comprobados los includes de los 13 shaders y la igualdad de las siete dependencias Android declaradas en Gradle y Godot.
- De los 5.306 miembros C# originales de producción, 5.298 conservan su declaración exacta, incluida identidad de tipo y espacio de nombres. Los ocho restantes corresponden a dos fases extraídas y a la consolidación del serializador de los fixtures.
- Al expandir los nuevos helpers de simulación, los dos métodos de ciclo conservan exactamente sus tokens originales. También se comprobaron los tokens del serializador compartido, las rutas internas y la selección de ofertas Android, y el AST de las funciones Python de versión.
- El runner de pruebas conserva el orden y los argumentos de 195 registros; los 118 cuerpos de casos definidos dentro del runner son equivalentes por tokens, tras calificar el helper común.
- El modelo del dashboard conserva sus 23 exportaciones públicas y el texto de sus 15 funciones. Comparación adicional de 440 informes válidos o malformados con la base, sin diferencias de resultado.

Las pruebas de actualizadores simulan construcción, cambio y recuperación de contenedores. Sus mensajes de registro no representan compilaciones ni despliegues reales.

## Validación pendiente

`AGENTS.md` reserva las compilaciones, instaladores, flujos de compilación, tags y releases para una petición expresa. Esta tarea no los ejecuta. Quedan pendientes:

- Compilar la solución .NET/Godot y el servicio de cuentas heredado; ejecutar sus pruebas C# y las integraciones con emuladores Firebase.
- Compilar Java/Gradle y comprobar en Android autenticación, sesión, tienda y actualización.
- Ejecutar las comprobaciones nativas de Windows y PowerShell.
- Ejecutar las pruebas visuales de Godot y las mediciones de FPS, CPU y asignaciones. No se atribuyen mejoras de rendimiento medido a la separación de archivos.

Los workflows se revisaron antes de preparar la subida: `ci.yml` y la publicación de APK se ejecutan manualmente; los workflows de petición por push filtran cambios en su propio archivo. Esos archivos no forman parte del cambio.

Los resultados y la lista exacta de archivos están en [refactor-integral-2026-11-23-evidencias.json](refactor-integral-2026-11-23-evidencias.json).
