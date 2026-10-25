# Efectos elementales adaptativos

La decisión es local y compartida por la arena y las demostraciones del libro
de hechizos. No cambia recetas, daño, alcance, estados, colisiones ni protocolo.

## Selección de calidad

- En Android, cuatro núcleos o menos, Adreno 3xx/4xx/5xx/60x/61x,
  Mali 4xx/T6xx/T7xx/T8xx/G31/G51/G52/G57/G71/G72, PowerVR SGX/GE8
  y renderizadores de software activan el perfil anterior desde el inicio.
  Es una heurística conservadora, no una medición de modelos de móviles.
- Un nombre de GPU vacío o desconocido se resuelve por rendimiento real.
- Se reutiliza el muestreo de partida de dos segundos, excluyendo pausa,
  pérdida de foco, ayuda, inventario, tienda y final de partida.
- Se baja al perfil anterior cuando la media cae por debajo del 80 % del
  objetivo o el percentil 95 del tiempo de fotograma supera 1,7 presupuestos
  de fotograma. A 60 FPS son 48 FPS o aproximadamente 28,3 ms.
- Para recuperar el perfil nuevo se requieren 12 segundos consecutivos con
  al menos el 94 % del objetivo y percentil 95 por debajo de 1,25 presupuestos.
  Cada ventana lenta reinicia el enfriamiento. El objetivo respeta un límite
  configurado de 30 a 60 FPS; un límite voluntario de 30 no se considera caída.
- El cambio vacía las partículas y corrientes nuevas, para no superponer las
  dos versiones. El hardware clasificado como limitado permanece en el perfil
  anterior y no prepara los recursos nuevos durante la carga.

## Aspecto y límites

| Familia | Perfil nuevo | Perfil anterior |
| --- | --- | --- |
| Fuego | Lenguas aprobadas con emisión a 45 Hz | Mismas lenguas a 30 Hz y menor límite |
| Frío | Cristales de seis puntas y fragmentos pequeños | Partículas prismáticas previas |
| Agua | Tres corrientes con ondulación, espuma y gotas | Corrientes y partículas previas |
| Hielo (agua + frío) | Proyectiles de cristal que giran hacia la cámara | Prismas previos |
| Vapor (fuego + agua) | Nube gris clara que avanza, asciende y se disipa | Efecto previo con color de vapor |
| Rayo | Núcleo blanco, quiebros, brillo y ramificaciones | Geometría anterior |
| Arcano | Núcleo violeta, filamentos y pulsos | Geometría anterior |

El vapor con arcano conserva el canal arcano y añade niebla sin dibujar fuego.
Se respeta la resolución de elementos opuestos; no se añaden recetas nuevas.
Las rocas imbuidas, sus auras y los canales de vida conservan su representación.

Todas las partículas comparten el almacén existente de 320 y el límite de 64
altas por fotograma. El subconjunto elemental nuevo está limitado a 192 y 32
altas por fotograma; el vapor ocupa como máximo 64. `DetailScale` reduce los
límites. Las corrientes continuas de agua se limitan a ocho autores y tres
ramas por autor, en un único lote. Rayo y arcano comparten las 512 instancias
de canal existentes; se dibujan primero los núcleos de todos los autores.

Las mallas, materiales, partículas y buffers se reutilizan. Los shaders no
requieren refracción, lecturas de pantalla, luces nuevas ni sombras.

## Comprobaciones

`AdaptiveSpellQualityTests` comprueba detección conservadora, dispositivos
desconocidos, caídas, percentil 95, recuperación, fluctuaciones, muestras
inválidas y límite de 30 FPS. La verificación de Godot comprueba familias,
mezclas, corte y caducidad, 128 emisores, límites, reutilización y cambios de
perfil; la verificación de proyectiles contempla ambas versiones del hielo.

Estas comprobaciones necesitan compilar y ejecutarse en la próxima sesión
autorizada. Hasta entonces, la validación disponible es de código, sintaxis y
previsualizaciones externas. Los GIF no son capturas de una compilación ni
mediciones de FPS de un teléfono. Quedan pendientes las cuatro orientaciones,
PvP, Prácticas, Supervivencia y dispositivos Android reales.
