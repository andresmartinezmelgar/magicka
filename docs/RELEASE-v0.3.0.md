Revisión de código, correcciones de conexión y combate, optimizaciones y diagnóstico integrado.

**Descarga `ArcanaArena-v0.3.0-Windows-x64.zip`, extrae todo y abre `ArcanaArena.exe` con doble clic.** El paquete incluye motor, cliente, servidor y runtimes.

- **Diagnóstico** en el launcher o **ESC → Exportar diagnóstico**: ZIP con logs de cliente y servidor alojado, versión, sesión, sala y tick. Errores con trazas y avisos del motor; contraseñas ocultas, rotación y escritura en segundo plano.
- Corregidos el cierre concurrente, cancelación del saludo, mensajes mutables, secuencias inválidas, IPv6 y respuestas malformadas.
- Arranque del servidor con identidad propia, control del doble inicio y aviso si termina inesperadamente. Los registros anteriores se conservan.
- Las salas vacías pausan bots. HUD, clasificación, materiales y búsqueda de objetivos reutilizan recursos. Liberación de audio al cerrar.
- Docker conserva los logs en un volumen. Parámetros de log documentados en `docs/DIAGNOSTICO.md`.

Publicación condicionada a compilación, 33 pruebas con servidor protegido, Docker, dos pruebas del servidor incluido y tres comprobaciones del ejecutable Windows: práctica, alojamiento y exportación de diagnóstico. `SHA256SUMS.txt` permite comprobar la descarga.

Protocolo v2. Salas y estadísticas temporales. Mapa, recursos y balance propios; la recreación sigue siendo una aproximación jugable de Magicka: Wizard Wars.
