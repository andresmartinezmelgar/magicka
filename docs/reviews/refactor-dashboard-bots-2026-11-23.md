# Refactorización del dashboard, bots e interfaz

Fecha: 23 de noviembre de 2026. Base: `e461416c32ee6f0840b396d1835502ca7d5dda19`.

Esta continuación modifica 33 archivos: 10 existentes y 23 nuevos, incluidos pruebas e informes. Combina extracción de responsabilidades con dos reducciones concretas de trabajo repetido. No contiene cambios de versión ni de configuración de cuentas, pagos o anuncios.

## Dashboard

- `app.mjs` conserva los eventos, la conexión y el estado de la interfaz. Las seis vistas y sus componentes HTML pasan a `views.mjs` y `view-components.mjs`.
- `data-view.mjs` valida y deduplica al recibir datos. Reutiliza el historial filtrado, sus totales y los streams filtrados mientras no cambien sus entradas. Buscar, paginar o cambiar de pestaña deja de repetir esos cálculos.
- Las entradas se invalidan también cuando el proveedor entrega de nuevo la misma fila o el mismo array. Se renueva la identidad de la cabecera del informe para invalidar el índice de métricas asociado; no se realiza una copia profunda de todo el historial.
- `telemetry.mjs` separa la selección de revisiones de la proyección temporal. La actividad del directo se recalcula con el reloj en cada lectura, incluidos los límites de 90 segundos de antigüedad y 30 segundos de adelanto.
- `model.mjs` conserva sus 23 exportaciones públicas. Orden de revisiones, ranking y detalles de participantes tienen módulos propios.

No se modifican los intervalos de descarga, las credenciales HTTP ni los filtros que distinguen el historial del directo. Las cachés retienen únicamente el último resultado de cada selección; no crecen con cada búsqueda.

## Bots de Supervivencia

Separadas planificación, elección de objetivos y repertorio de hechizos. Las tres selecciones del planificador —humano más próximo, aliado herido y amenaza— usan recorridos directos en lugar de construir consultas LINQ por bot. Conservan sus umbrales, continuidad de objetivo, exclusión de reservas y criterios de desempate. La comparación de claves usa `float.CompareTo`, incluido el tratamiento de valores no finitos.

Se mantienen los buffers compartidos y el intervalo de planificación de 0,45 segundos. El resto del planificador coincide por tokens al sustituir las tres llamadas extraídas por sus consultas anteriores. La equivalencia de las nuevas selecciones tiene tres regresiones C# preparadas, pendientes de ejecución.

## Renderizado y menús

| Componente | Responsabilidades separadas |
| --- | --- |
| `AreaVisuals` | Materiales y patrones; construcción de geometría en el tipo privado `Ink`. |
| `ArenaRenderer.Defense` | Pool y preparación de enemigos; verificaciones de modelos e indicadores. |
| `Launcher.OptionA` | Diseño adaptable, navegación y aviso del tutorial. |
| `MatchStatisticsPanel` | Descarga y versiones, métricas y presentación. |

Se conservan los cuerpos de los miembros trasladados, los shaders, los encabezados de tipos y el orden de los campos e inicializadores originales.

## Distribución

Los scripts de comprobación de Docker y del dashboard Windows pasan a recorrer todos los módulos `.mjs`, para cubrir también las dependencias extraídas. Se ha revisado que el contenido del servidor y el servidor estático Windows incluyen y sirven esos archivos. Los scripts de integración están preparados; no se han ejecutado sus servicios ni compilaciones.

## Comprobaciones completadas

| Comprobación | Resultado |
| --- | --- |
| `node --test dashboard/tests/*.test.mjs` | 37 pruebas superadas; 8 nuevas. |
| Comparación del modelo con la base | 432 combinaciones equivalentes de filtros, bots, versión, búsqueda y reloj. |
| Comparación de las seis vistas con la base | 2.592 salidas HTML idénticas. |
| Límites temporales del directo | Cinco puntos equivalentes alrededor de los límites. |
| API pública del modelo | Las mismas 23 exportaciones. |
| Sintaxis | 638 archivos C#, 17 Java, 22 JavaScript y 32 Python analizados sin errores. |
| Miembros C# de la base | 6.052 de 6.054 conservados exactamente; cambian el planificador y el registro de las nuevas pruebas. |
| Contratos y campos C# | Conservados 924 encabezados de tipos/constructores de records y el orden original de los campos. |
| Referencias | Archivos enlazados de proyectos e imports locales resueltos. |
| Scripts modificados | `bash -n` y análisis sintáctico de PowerShell superados. |

Las ocho nuevas pruebas JavaScript cubren filtros e invalidación, reutilización de cálculos, caducidad del directo, revisiones inválidas, independencia de rangos, escape de nombres y el flujo de la aplicación con HTTP y DOM simulados: conexión, exportación CSV, importación, rechazo de un archivo inválido y vuelta al servidor. No representan una prueba visual en navegador.

Los dos miembros C# modificados intencionadamente son `GameWorld.PlanDefenseBots` y `DefenseTests.Run`. El segundo únicamente registra los tres nuevos casos; las pruebas anteriores conservan su registro y su orden.

## Pendiente

`AGENTS.md` exige una petición expresa para compilar. Quedan pendientes la compilación .NET/Godot, las tres regresiones C# de selección de objetivos, las integraciones Docker/Windows, la comprobación visual y las mediciones de rendimiento en una partida real. El análisis de sintaxis y equivalencia de código no sustituye esas comprobaciones.

No se han ejecutado workflows, generado APK ni creado tags o releases. No se atribuyen mejoras numéricas de FPS, latencia o memoria sin mediciones. Esta tanda continúa la refactorización general y no certifica que todo el código esté optimizado al máximo.

Archivos y evidencias: [refactor-dashboard-bots-2026-11-23-evidencias.json](refactor-dashboard-bots-2026-11-23-evidencias.json).
