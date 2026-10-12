# Optimización de comunicaciones PvP — protocolo 19

## Código implementado
- Acciones cliente-servidor en binario (0xB1) con código de acción, secuencia y campos específicos.
- Ejes de movimiento cuantizados a int16; objetivos de movimiento omitidos si MoveTo es falso. El apuntado conserva float32.
- Un estado base por jugador se transmite en el saludo (welcome). Los siguientes snapshots se codifican como deltas independientes frente a esa base durante toda la conexión.
- El delta incluye cambios de campos y, si el tamaño de la colección se conserva, solo índices modificados. Si cambia el tamaño se transmite la colección.
- Los fotogramas se comprimen opcionalmente con Brotli y viajan como WebSocket Binary (0xA1); la descompresión se limita a 256 KiB.
- La cola DropOldest es segura para estos deltas porque no dependen de fotogramas intermedios.
- La precarga estática de partida se realiza mediante el estado inicial del saludo; nombres, equipación y demás datos sin cambios se omiten en los deltas.
- Instrumentación de bytes y número de snapshots en el servidor; benchmarks y pruebas de roundtrip añadidos.

## Consideraciones
- Los clientes y el servidor deben actualizarse conjuntamente a la versión 19.
- La codificación de snapshots sigue siendo JSON delta comprimido, no un esquema binario tipado completo.
- La comparación de estados por cliente aumenta el trabajo de CPU; medirlo en servidores con múltiples salas.
- No se han ejecutado las pruebas ni compilado la solución desde esta conexión. No se garantiza aún funcionamiento en producción.
- Para una reducción adicional, sustituir los campos dinámicos del delta por un esquema binario versionado, cuantizar posiciones tras medir tolerancias y añadir pruebas de estrés reales.
