# Comunicaciones — protocolo 27

Las acciones de combate conservan su formato binario y el servidor envía 20 estados por segundo. Cada delta es independiente respecto a una base identificada; nunca depende del mensaje anterior.

Cada 40 estados se ofrece una nueva base. El cliente la valida, conserva la anterior y confirma su identificador. El servidor solo cambia de base tras esa confirmación. La base se reintenta cada 20 estados si se descartó o no llegó la confirmación. Los controles de base se añaden al estado del mismo ciclo, sin reemplazarlo.

Los enemigos usan máscaras binarias de campos exactos por identificador, sin cuantizar posición, vida ni avisos. Así se evita construir árboles JSON por cada minion. Las otras colecciones con identificadores conservan altas, bajas y orden sin sustituir todos sus elementos cuando cambia la cantidad. Si el orden es estable, se actualizan solo los campos modificados. El inventario y los datos estables ya incorporados a la base se omiten hasta cambiar. La base JSON del servidor se prepara una vez y no se comprime además un snapshot completo para compararlo en cada envío.

El cliente conserva dos bases como máximo. El tamaño de mensaje y de descompresión sigue limitado a 256 KiB; la profundidad JSON admite 32 niveles para los cambios anidados de inventario. Los datos privados de equipo y botín se filtran antes de codificar el estado de cada destinatario. El token de recuperación viaja únicamente en el saludo y no se incorpora a las bases.

`--defense-benchmark` compara el codificador anterior con bases confirmadas usando los mismos estados reales y contabiliza los keyframes. Sus tiempos pertenecen al entorno de pruebas; no equivalen a FPS de Android o Raspberry Pi.

## Medición de 0.21.0

En una ejecución local de 180 estados idénticos para ambos codificadores, la base inicial fija envió 2.106.652 bytes y las bases confirmadas con enemigos binarios enviaron 1.260.391 bytes, incluyendo keyframes (40,2 % menos). La codificación pasó de 1.445,6 a 614,4 ms (57,5 % menos), y las asignaciones de 239.055.160 a 129.833.552 bytes (45,7 % menos). Son mediciones de CPU y tráfico del escenario de prueba, no FPS de dispositivos reales ni una garantía para cualquier partida.
