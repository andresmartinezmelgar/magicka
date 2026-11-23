# Continuación de la refactorización

Fecha: 23 de noviembre de 2026. Base: `9bda7e07158b5619a904246370bfb26da3b5ca8c`.

Esta pasada continúa sobre los componentes que no se modificaron en la anterior. Afecta a 74 archivos: 56 nuevos y 18 existentes. De esos 18, 15 no habían cambiado en la primera pasada; los otros tres son integración, preflight y registro de pruebas. Los archivos nuevos contienen responsabilidades extraídas, dos regresiones y el informe; no representan 56 funcionalidades nuevas.

## Cambios

| Área | Trabajo realizado |
| --- | --- |
| Protocolo | Separados codificación de acciones, compresión de snapshots, mensajes y contratos del estado del mundo. Conservados discriminantes, parámetros, atributos JSON y límites. |
| Supervivencia | Separados guardado, tipos privados serializados, restauración y validación. Economía organizada en comandos, controles de inventario/tienda, poderes, botín y snapshots. Los seis controles extraídos mantienen sus comprobaciones antes del bloqueo de acciones de jugadores caídos. |
| Cuentas y Firestore | Opciones Firebase, verificación Google, login y comunicación con el servicio remoto separados. Mutaciones de equipo, recompensas y conversión Firestore en módulos propios. El servidor de cuentas heredado enlaza los siete archivos nuevos que necesita. |
| Renderizado e interfaz | Separados geometría, entidades y visibilidad del renderer; entrada y dibujo de controles móviles; acciones y estado del perfil; ofertas y acciones de la tienda. Las comprobaciones de layout y cabecera de escritorio tienen módulos propios. |
| Diagnóstico | Fachada de logs, escritor, censura de datos, exportación y captura de errores de proceso separados. |
| Android | Verificación de almacenamiento privado, SHA-256, paquete y versión extraída a `UpdateApkVerifier`, antes de crear la sesión de instalación. El controlador conserva permisos, foco, confirmación y reintento. |
| Pruebas | Casos de cuentas agrupados en inventario, login, HTTP, red y recompensas con un contexto explícito. Pruebas de Defensa agrupadas por catálogo, combate, economía, continuidad, bots, asistencia, progresión y poderes; benchmark y red independientes. |
| Scripts | Preflight de autenticación adaptado a los módulos extraídos. Referencias documentales del estudio PvP actualizadas a la familia `Protocol.*.cs`. |

## Reducción del trabajo repetido

- `BuildInfo.Version` y `UpdateBuildNumber` conservan las expresiones de lectura de metadatos, evaluadas una vez por proceso. La creación de mensajes y líneas de diagnóstico obtiene el valor preparado sin repetir reflexión y partición de cadenas.
- La censura de logs prepara la representación JSON escapada al registrar cada secreto. Cada línea utiliza las representaciones ya preparadas. Se conservan el bloqueo, orden de sustitución, tratamiento de duplicados, límite de 256 entradas, expresión regular y truncado.

Son reducciones verificables en las operaciones del código. No se atribuyen cifras de FPS, latencia o memoria sin mediciones de ejecución.

## Tamaño de los archivos principales

Las líneas trasladadas permanecen en sus módulos correspondientes.

| Archivo | Antes | Después |
| --- | ---: | ---: |
| `Protocol.cs` | 282 | 34 |
| `Diagnostics.cs` | 244 | 22 |
| `GameWorld.DefenseEconomy.cs` | 406 | 50 |
| `GameWorld.SurvivalSave.cs` | 372 | 66 |
| `AccountService.cs` | 254 | 74 |
| `AccountRepository.cs` | 277 | 119 |
| `ArenaRenderer.cs` | 476 | 234 |
| `AccountProfilePanel.cs` | 299 | 175 |
| `ItemShopPanel.cs` | 356 | 120 |
| `MobileControls.cs` | 348 | 164 |
| `Arcana.Account.Tests/Program.cs` | 567 | 74 |
| `DefenseTests.cs` | 754 | 67 |

## Comprobaciones completadas

- Análisis sintáctico de los 624 archivos C#, 17 Java y 32 Python, sin errores.
- Comparación de 6.020 miembros C# de la base, incluido código de pruebas: 6.012 conservan su declaración exacta. Los ocho cambios intencionados corresponden a las cachés, la extracción de comandos y dos registros de pruebas.
- Conservados los 880 encabezados de tipos y constructores de records originales, y el orden de los campos. La separación mantiene las identidades de los tipos privados de guardado y los contratos serializados.
- Al expandir las seis funciones de control de Supervivencia, el método de comandos recupera exactamente sus tokens originales. El bloque Android de verificación y su posición anterior a la creación de sesión también son equivalentes por tokens.
- Conservados orden, argumentos y cuerpos de 38 registros del ejecutable de cuentas y 36 de Defensa. Esta comprobación no ejecuta sus casos.
- Resueltos los archivos enlazados y las referencias de proyectos; comprobada la inclusión de los siete módulos en el host heredado.
- Superados el preflight de autenticación, la comprobación de metadatos `0.29.73 / 29073` y la revisión de espacios del diff.

Se han preparado dos regresiones en `DiagnosticRedactionTests.cs`: registro concurrente de secretos Unicode con su representación JSON escapada; duplicados y retención FIFO de 256 entradas. Están registradas en las pruebas de transporte/diagnóstico, pendientes de ejecución.

## Validación pendiente

`AGENTS.md` reserva la compilación para una petición expresa. Esta pasada no ejecuta compilaciones, workflows, APK, tags ni releases. Quedan pendientes la compilación .NET/Godot/Android, las pruebas C# y sus integraciones Firebase, el flujo real de instalación Android y las comprobaciones visuales y de rendimiento. Las 126 pruebas JavaScript/Python de la pasada anterior no se presentan como pruebas de estos cambios C# y Java.

El análisis global conserva los componentes pequeños que ya tienen una responsabilidad definida. Esta revisión y las comprobaciones de equivalencia no sustituyen las pruebas funcionales del juego completo.

Lista de archivos y resultados: [refactor-continuacion-2026-11-23-evidencias.json](refactor-continuacion-2026-11-23-evidencias.json).
