Sprites de los cinco tipos de mago, integrados en el juego.

**Para jugar:** descarga `ArcanaArena-v0.5.0-Windows-x64.zip`, extrae todo y abre `ArcanaArena.exe`.

**Para compilar:** descarga el código actualizado y abre `Compilar-Juego.cmd`. El resultado sustituye `Juego/` y se abre en `Juego/ArcanaArena.exe`.

- Equilibrado, piromante, criomante, tempestario y geomante con rostros, túnicas, barbas, sombreros y bastones propios.
- Cuatro orientaciones, animación de pasos, balanceo, poses de carga/lanzamiento y destello al recibir daño. Los eventos instantáneos también activan la pose.
- Túnicas azules o rojas según equipo, conservando los colores de cada especialidad, con anillos de identificación.
- Barras de vida y escudo sobre el sombrero, alineadas a la cámara, sin sombras; burbuja de escudo ajustada al personaje.
- Cinco atlas RGBA compartidos, anclajes de pies por pose y mipmaps para el zoom. PNG, índice, shader, documentación y prompts incluidos en el código.

Protocolo 3: compatible con el servidor v0.4.0. La distribución incluye el servidor v0.5.0 y conserva IP, LAN, creación de salas, práctica y diagnóstico.

Validación: comprobación de sprites con 120 combinaciones, congelación, muerte, reaparición y limpieza; capturas reales del juego. También se ejecutan las pruebas de reglas/red, Docker por IP/LAN, compilación con doble clic y seis comprobaciones del ejecutable Windows. Incluye `SHA256SUMS.txt`.
