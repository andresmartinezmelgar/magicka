# Publicación de pruebas de Supervivencia 0.29.29

El usuario autoriza tag y APK en la rama `pruebas/supervivencia-procedural-autoguardado`. Se prepara una preliminar `preview-v0.29.29-r<compilación>` y una APK ARM64 con el certificado permanente. Se conserva la versión estable en main.

## Algoritmo

Kruskal aleatorizado sobre una cuadrícula de nueve salas. Se desplazan sus centros y varían sus tamaños; Fisher–Yates baraja las doce conexiones candidatas. Una unión de componentes acepta ocho conexiones sin ciclos, garantizando que todas las salas quedan accesibles. Se añaden entre dos y cuatro conexiones extra. Cada conexión usa dos tramos perpendiculares en L, con anchura de 8–12 m. La unión de rectángulos se rasteriza en 160 × 120 celdas para extraer límites exactos. Las rutas utilizan la cuadrícula transitable compartida.

La semilla y la versión del generador reconstruyen el mismo mapa en cliente y servidor. Continuar recupera esa semilla; iniciar o reiniciar otra partida elige otra. El azar del terreno es independiente del de la horda y del botín.

## Medición local ejecutada

.NET 8.0.21, x64; 64 semillas nuevas para cada medida, después de cuatro calentamientos. Compilación con optimizaciones. Los números no incluyen texturas, sonidos, shaders ni precarga de Godot y no corresponden a Android físico.

| Medida | Mediana | Percentil 95 | Máximo |
| --- | ---: | ---: | ---: |
| Geometría y colisiones | 0,411 ms | 0,954 ms | 2,847 ms |
| Mundo con geometría y navegación | 9,184 ms | 14,730 ms | 22,409 ms |

Datos: [medicion-mapas-supervivencia-0.29.29.json](medicion-mapas-supervivencia-0.29.29.json). El benchmark se repite en CI con `--profile-survival-generation` y queda registrado en el log de compilación.

## Validación y correcciones previas

La suite completa se ejecutó localmente: **537 correctas, 0 fallidas**, incluidas las diez regresiones de continuidad. Se compilaron Core, Tests y Client. Se corrigió la ambigüedad entre `System.Environment` y `Godot.Environment` al medir la suspensión.

La suite completa inicial detectó cuatro escenarios dependientes del mapa antiguo. El escenario integral de rendimiento ahora usa los portales del mapa generado y coloca las reliquias dentro de sus salas; la prueba histórica de daño fija explícitamente su terreno original. La comprobación visual de sectores también usa los portales reales y compara la cámara con su destino, sin coordenadas antiguas. La ayuda describe el mapa variable.

Se detectó además que un rayo reflejado cambia de propietario pero mantiene el alcance restante del lanzador equipado. El validador ahora admite ese alcance dentro del máximo de equipo, manteniendo el límite de los rayos directos sin equipo. Una regresión comprueba los tres casos: reflejo válido, directo sin alcance prestado y reflejo por encima del máximo.

La simulación de guardado provoca la última muerte mediante la ruta real de daño; asignar manualmente cero de vida no ejecutaba la transición de fin de partida.

Reiniciar cambiaba el terreno, pero dejaba en el snapshot rayos y eventos recientes con coordenadas del mapa anterior. Se retiran al reiniciar; la regresión lanza un rayo desde un portal y comprueba que la nueva partida no reproduce sus efectos ni eventos. La suite completa vuelve a pasar con 537 pruebas.

La simulación táctil antigua enviaba pausa de Android pero no reanudación; ahora reproduce ambos eventos antes de cerrar el menú. La espera de CI añade un cierre forzado si Godot no responde a la señal de terminación.

La publicación exige que CI vuelva a ejecutar la suite completa, pruebas LAN, simulaciones visuales, reglas Firestore, Docker y empaquetados. El job de preliminares depende de todas esas comprobaciones y verifica la firma permanente y los SHA-256 antes de publicar. La solicitud del flujo se limita a esta rama, a cambiar su activador y al mensaje explícito de publicación; subir otros cambios no lanza una compilación.
