# Arcana Arena v0.16.0 — Optimización del tráfico PvP

Versión de protocolo: **19**. Cliente y servidor deben actualizarse juntos.

## Cambios
- Acciones de combate binarias con movimiento cuantizado a 16 bits y apuntado de precisión completa.
- Snapshot inicial usado como referencia por conexión; los datos sin cambios no se repiten en los deltas.
- Deltas independientes respecto al saludo, resistentes al descarte de actualizaciones intermedias.
- Comparación de campos e índices de arrays; sustitución de arrays cuando cambia su longitud.
- Compresión Brotli selectiva de snapshots y límite de descompresión.
- Comparación entre tamaño del snapshot completo y del delta para escoger el menor.
- Métricas de tráfico de snapshots encolados frente al equivalente completo.
- Pruebas de roundtrip y benchmark de tamaño de mensajes.

## Validación
La etiqueta `v0.16.0` solo se crea automáticamente cuando todos los trabajos de CI (núcleo, red, cliente Godot, Windows, Android y emulador) finalizan correctamente. La publicación de ejecutables sigue siendo manual.

## Limitaciones
Los deltas usan JSON estructurado con compresión opcional; no se ha sustituido por completo por un esquema binario de estados dinámicos. Las métricas miden bytes puestos en cola, no el tráfico de red efectivo ni el coste de transporte.
