# Arcana Arena v0.12.12

Traslación usa el último destino recibido, incluso si cambia justo antes del salto. El joystick conserva la intensidad de la inclinación; las diagonales siguen limitadas a la velocidad máxima.

El arco de las barreras sin piedra conserva sus segmentos y materiales en dos mallas en lugar de cuarenta. Las rutas buscan el nodo más cercano sin ordenar toda la navegación. F1 describe correctamente el consumo de escudo al reflejar rayos.

La compilación Android desde Bash valida los errores de Godot en importación y exportación y sustituye la APK solo después de comprobar firma, manifest y bibliotecas. Las regresiones verifican que los fallos conservan APK, checksum y presets.

[Revisión completa y mejoras pendientes](AUDITORIA-v0.12.12.md). Código y scripts en `main`, protocolo 8. Las correcciones de simulación requieren actualizar también el servidor.
