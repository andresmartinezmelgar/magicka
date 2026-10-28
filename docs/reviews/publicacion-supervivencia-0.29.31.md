# Publicación de pruebas de Supervivencia 0.29.31

Versión 0.29.31, código Android 29031 y protocolo 41, en `pruebas/supervivencia-procedural-autoguardado`. Tag `preview-v0.29.31-r<compilación>`. La estable en main permanece separada.

## Cambios incluidos

- Venta automática por ranura, de cualquier elemento, con migración que protege objetos actuales, favoritos y equipados.
- Fichas sin atributos inactivos ni cifras a cero de efectos que el objeto no aporta.
- Diez biomas visuales reproducibles por semilla, conservados al guardar y continuar.
- Descripciones abreviadas de equipo, afijos, anillos, ayudas y estadísticas; conservan valores, condiciones y límites. Los botones mantienen sus acciones y las tablas su formato.

## Validación y paquetes

La publicación requiere todos los trabajos de CI correctos: compilación C#, regresiones del motor y red, cuentas con emulador, Docker amd64/arm64 y simulaciones Godot. La revisión de los diez biomas renderiza y captura cada ambiente en escritorio y móvil.

Se generan APK de firma permanente, Windows ZIP y RAR 5 verificado por extracción y SHA-256. Los parches se calculan solo contra bases válidas del mismo canal y rama. Los manifiestos y PREVIEW identifican commit y compilación. El publicador crea el tag únicamente después de las comprobaciones obligatorias.
