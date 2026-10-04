Compilación con doble clic, objetivos de partida, afinidades y efectos de combate.

**Para jugar:** descarga `ArcanaArena-v0.4.0-Windows-x64.zip`, extrae todo y abre `ArcanaArena.exe`.

**Para compilar el código:** descarga el repositorio, extrae todo y abre `Compilar-Juego.cmd`. Descarga .NET y Godot si faltan, compila cliente y servidor y sustituye `Juego/`. El resultado se abre en `Juego/ArcanaArena.exe`. Los registros quedan en `artifacts/compilacion-*.log`.

- Cinco tipos: equilibrado, piromante, criomante, tempestario y geomante. Los especializados hacen +25 % de daño elemental y reciben +25 % del contrario, calculado por el servidor por componente.
- Ayuda «Cómo ganar», objetivo antes de entrar en la sala y bajo el marcador. Usa los límites reales de la partida.
- Estelas, ondas, fragmentos, explosiones, impactos y rotura de escudo; partículas de quemadura, mojado, frío y celeridad. Nodos reutilizados con límites de 384 partículas y 32 ondas.
- Sombreros, bastones y anillos de afinidad. Se conservan las barras de vida y escudo con cantidad y duración.
- Protocolo 3: actualizar cliente y servidor juntos, incluido el contenedor Docker.

Validación: 43 pruebas de núcleo/diagnóstico/red, Docker por IP y LAN, servidor incluido y cinco comprobaciones del ejecutable Windows. Pruebas de reemplazo completo de la carpeta, segunda publicación y fallo sin perder la compilación anterior. Incluye `SHA256SUMS.txt`.
