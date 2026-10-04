# Revisión del código v0.11.1

Ámbito: todos los archivos de simulación y transporte de `Arcana.Core`, servicios y endpoints de `Arcana.Server`, scripts C# y shader del cliente Godot, configuraciones de exportación, scripts de compilación, Docker, Compose y CI. Se revisaron también las pruebas y las instrucciones incluidas en el paquete.

## Fallos corregidos

| Área | Problema | Corrección y comprobación |
|---|---|---|
| Carga | Se conservaba `ChargeTime` al interrumpir la roca; podía reaparecer su animación tras morir. | Cancelación común en todos los caminos. Regresiones por timeout, congelación, cambio de hechizo y muerte. HUD y efectos ignoran carga residual si `Casting` es falso. |
| Hielo | Cada fragmento generaba el mismo evento de lanzamiento. | Un evento por descarga, conservando los cinco fragmentos de QRR. |
| Movimiento | Comprobar solo el destino permitía atravesar un pilar o muro con un empuje fuerte. | Comprobación del recorrido por ambos ejes, conservando el deslizamiento. |
| Proyectiles | Ocho muestras aproximaban el choque con muros y podían situar el impacto dentro de la superficie. | Intersección continua segmento/cápsula, respetando el primer impacto. |
| Muros | Un muro eliminado por un ataque o por caducidad todavía aplicaba contacto ese paso. | Se descartan sus efectos antes de resolver contacto. |
| Rayos | El bonus del primer autor se aplicaba también al componente del otro mago. | Cada componente conserva su propietario y bonus al consumir un escudo; prueba en ambos órdenes. |
| Aparición | Un muro nuevo podía cubrir una base o el lugar donde murió un aliado. | Se elige una base libre o suelo cercano si las cuatro están bloqueadas. Reanimar conserva el lugar original solo si es transitable. |
| Conexiones | El saludo o error de un intento anterior podía actuar sobre una sesión posterior. | Se comprueba la identidad del intento antes de cada callback, se cancelan intentos pendientes y se cierran conexiones sustituidas. |
| Salas/LAN | No se podía volver durante una consulta; una búsqueda LAN antigua podía alterar el estado posterior. | Salida disponible mientras se carga, cancelación inmediata y callbacks asociados a su búsqueda. |
| Datos recibidos | Crear sala solo validaba su ID; el directorio no comprobaba todos los límites ni IDs repetidos. | Validador compartido por HTTP, WebSocket y LAN. Pruebas con respuestas HTTP reales y campos inválidos. |
| Snapshots | IDs repetidos y eventos desordenados podían confundirse en el renderer. | Unicidad de entidades, orden de eventos, vida coherente y carga entre 0 y 1. |
| Alojamiento | El servidor incluido heredaba `ARENA_MAP` del entorno. | Se elimina la variable al iniciar el proceso; regresión en el paquete Windows. |
| Distribución | Propiedades Windows, etiquetas Docker/Compose y LEEME seguían indicando versiones antiguas. | Metadatos actualizados y comprobación del ejecutable exportado. |
| Capturas | El zoom general del mapa se sobrescribía cada fotograma. | Se conserva el encuadre calculado para la vista general. |

## Simplificaciones

- Una tabla inmutable de magias comparte nombre, coste y espera entre HUD y simulación.
- La demostración F1 conserva su receta y consulta la mina directamente, sin copiar el mundo cada paso para localizarla.
- La vista F1 crea un snapshot cuando avanza la simulación y lo reutiliza mientras está pausada. El filtro calcula una vez las repeticiones de teclas; la búsqueda admite hasta 64 caracteres.
- Eliminados `RoomLobby._launcher` y el parámetro correspondiente, `ArenaRenderer._playing`, el helper antiguo `ArenaMap.Spawn`, los uniformes `floor_size`/`floor_center` y asignaciones de escudo sobrescritas en el mismo fotograma.

## Validación

- 80 pruebas de simulación, límites, HTTP y diagnóstico; 19 de red real: **99 correctas, 0 fallidas**.
- Las regresiones de combate reproducen los fallos con el código anterior. La de rayos también se ejecutó contra la implementación anterior aislada.
- Prueba nativa Godot de saludo tardío, error obsoleto, cambio a práctica y sustitución de conexión. La prueba del launcher Windows incluye cancelación antes y después del saludo y sustitución de conexión.
- CI compila los cuatro proyectos, prueba Docker con puertos publicados y LAN, y ejecuta el paquete Windows autónomo.
- Comprobaciones Godot de práctica, diagnóstico, HUD, sprites, F1, mapas y efectos; capturas OpenGL de cuatro mapas y tres niveles de carga.

No hay cambio de formato de red: se conserva el protocolo 8. Un valor de carga residual de un servidor anterior se admite, pero el cliente no lo representa como un lanzamiento activo. Todas las correcciones de simulación requieren actualizar también el servidor.
