# Optimización de tráfico PvP

## Implementado

- Los mensajes de acciones del cliente usan `Protocol.SerializeAction`, que omite los campos de saludo y los de acciones distintas.
- Los snapshots de sala se serializan una vez por sala y, si superan 1024 bytes, se comprimen con Brotli en nivel Fastest.
- Solo se envía la versión comprimida si ahorra al menos 16 bytes; en otro caso se conserva JSON.
- Los fotogramas comprimidos llevan prefijo 0xA1 y viajan como WebSocket Binary; el cliente acepta también Text JSON.
- La descompresión está limitada a 256 KiB y el resultado sigue pasando por `SnapshotValidation`.
- Los mensajes de control, saludo y consulta de equipamiento siguen usando JSON.
- La simulación y la frecuencia de snapshots permanecen sin cambios.

## Trabajo pendiente para alcanzar la arquitectura objetivo

1. Instrumentar bytes reales por tipo de mensaje, jugadores y escenarios de carga.
2. Separar datos de sala y jugador inmutables, enviarlos durante la carga PvP y versionarlos por hash.
3. Introducir un protocolo binario tipado para posiciones, vectores, elementos y máscaras de cambios.
4. Implementar deltas con secuencia base y ACK por cliente; la cola actual `DropOldest` no admite deltas dependientes sin resincronización.
5. Dar entrega fiable a creación/destrucción de entidades y eventos importantes.
6. Cuantizar coordenadas tras pruebas de precisión de apuntado, colisiones e interpolación.
7. Evitar acumulación de inputs de movimiento sin eliminar comandos discretos (cast/release).
8. Añadir pruebas de regresión y benchmarks reales antes de establecer porcentajes de ahorro.

## Compatibilidad

El cambio de transporte es bilateral: cliente y servidor deben actualizarse juntos. La compatibilidad de mensajes de texto JSON no implica que un cliente anterior admita fotogramas binarios. Antes del despliegue de producción debe incrementarse `Protocol.Version` y actualizarse la documentación de versiones, descubrimiento y exportación Android.

No se han medido porcentajes de reducción ni compilado el código en el entorno de edición GitHub.
