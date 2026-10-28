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

## Hallazgo durante la compilación r614

Motor y red: 569 correctas y 0 fallidas; lobby: 547 correctas y 0 fallidas. Los diez biomas completaron la comprobación en escritorio y móvil. La integración de cuentas detectó que una salida normal podía dejar al jugador reservado para reconexión: el cliente enviaba CloseOutput y abortaba inmediatamente, sin esperar confirmación; el servidor tampoco respondía al estado CloseReceived.

El cliente espera al lector dentro del plazo de cierre ya existente y el servidor confirma los cierres recibidos. La salida explícita no notifica pérdida de red. Se añade una regresión con WebSocket real y confirmación controlada, incluidos dos cierres simultáneos; la integración conserva la prueba original de salida y nueva entrada con recursos intactos. La siguiente compilación vuelve a ejecutar todas las comprobaciones antes de publicar.
