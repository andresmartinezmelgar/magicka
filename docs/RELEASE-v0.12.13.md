# Arcana Arena v0.12.13

El joystick móvil se activa al tocar cualquier punto libre de la mitad izquierda. Su centro aparece bajo el dedo y el arrastre conserva la intensidad proporcional. Los botones tienen prioridad; cada dedo mantiene su función aunque cruce al lado contrario. Soltar, cancelar, abrir el menú, morir o usar Traslación descarta el gesto anterior.

Los scripts de compilación Windows y Android comparten un bloqueo por carpeta de proyecto, también entre PowerShell y Bash/Python. La segunda compilación se detiene antes de modificar presets y herramientas. El bloqueo se libera al terminar; no hay que borrar su archivo.

Los snapshots de red se comprueban contra el contorno y tamaño del mapa antes de crear mallas. Posiciones, direcciones, radios, longitudes y potencias tienen límites; se admiten los extremos de rayos en el borde, los destellos del bastón y los tramos de cadenas eléctricas entre objetivos.

Las escenas y comprobaciones de CI pasan a `Scripts/Verification/ClientVerification*`. `Main` conserva el flujo de juego y una interfaz interna de acceso; el coordinador de verificación se crea únicamente con argumentos de prueba o captura.

Práctica incorpora **Medir rendimiento gráfico**. La escena reproduce 16 magos y los límites actuales de proyectiles, muros, campos, canales y eventos. Guarda tiempos de CPU/GPU, draw calls, primitivas y memoria, junto con el mapa, dispositivo, backend y procedencia. Los contadores no disponibles se representan como `null`. También se puede medir una APK de desarrollo mediante ADB. [Escena, comandos y límites de las mediciones](RENDIMIENTO.md).

Estas mejoras resuelven los cambios de código pendientes de la [auditoría v0.12.12](AUDITORIA-v0.12.12.md). La CI ejecuta el perfil gráfico en OpenGL por software y en un emulador Android; la medición en un móvil físico sigue requiriendo ese dispositivo. La escena mide representación local, sin simular red, IA ni combate, y no fija un objetivo de FPS para hardware sin medir.

Código y scripts en `main`. Protocolo 8, versión Android 133. La publicación de ejecutables permanece manual.
