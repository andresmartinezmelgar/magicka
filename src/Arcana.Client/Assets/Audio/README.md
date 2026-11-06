# Pulso de batalla

Composición original para Arcana Arena. Sintetizadores, instrumentos y percusión
se generan por código, sin grabaciones, melodías ni muestras de terceros.
Los tres archivos Ogg de esta carpeta se ofrecen bajo **CC0-1.0**.

- Re menor, con la mayor como dominante.
- 144 BPM, 4/4, 24 compases, 40 segundos exactos.
- Estéreo, 24 kHz, 960 000 muestras por canal en cada capa.
- `arcane_atmosphere.ogg` combina cuerdas cortas, bajo y percusión ligera.
- `arcane_battle.ogg` añade golpes de bombo y caja, bajo sincopado y motivos de metales.
- `arcane_danger.ogg` acelera la subdivisión con cuerdas agudas y redobles de toms.

`AdaptiveMusic` inicia las tres capas con `AudioStreamSynchronized` y modifica
su volumen sin reiniciarlas. Cada Ogg tiene el bucle activado por código.

Fuente de la composición: `scripts/generate-soundtrack.py` (Python, NumPy y
FFmpeg con libvorbis). No se necesita Python ni FFmpeg para jugar.
El archivo `soundtrack.json` conserva parámetros y hashes de los Ogg entregados.
`python3 scripts/check-soundtrack.py` verifica los recursos sin compilar el juego.

CC0: https://creativecommons.org/publicdomain/zero/1.0/
