# Resonancia del santuario

Composición original para Arcana Arena. Sintetizadores, instrumentos y percusión
se generan por código, sin grabaciones, melodías ni muestras de terceros.
Los tres archivos Ogg de esta carpeta se ofrecen bajo **CC0-1.0**.

- Tonalidad: re menor; progresión Dm(add9), Bb(add9), F(add9), C(sus2).
- 96 BPM, 4/4, 16 compases, 40 segundos exactos.
- Estéreo, 24 kHz, 960 000 muestras por canal en cada capa.
- `arcane_atmosphere.ogg`: cuerdas suaves, arpa y campanas.
- `arcane_battle.ogg`: ostinato grave y tambores.
- `arcane_danger.ogg`: pulsos agudos y acentos metálicos.

`AdaptiveMusic` inicia las tres capas con `AudioStreamSynchronized` y modifica
su volumen sin reiniciarlas. Cada Ogg tiene el bucle activado por código.

Fuente de la composición: `scripts/generate-soundtrack.py` (Python, NumPy y
FFmpeg con libvorbis). No se necesita Python ni FFmpeg para jugar.
El archivo `soundtrack.json` conserva parámetros y hashes de los Ogg entregados.
`python3 scripts/check-soundtrack.py` verifica los recursos sin compilar el juego.

CC0: https://creativecommons.org/publicdomain/zero/1.0/
