# Defensa del bastión — primera versión experimental

Versión **0.18.1**, protocolo **22**, rama `feature/defensa-oleadas`. Esta rama se mantiene independiente de `main`. Las compilaciones verificadas se publican como versiones preliminares con APK directa. Las dos cuentas autorizadas reciben estas versiones mediante el actualizador; el resto conserva el canal estable. Véase [Canales de actualización](CANALES-ACTUALIZACION.md).

## Entrar en el modo

- **Local:** Inicio → IA / Defensa → modo Defensa del bastión → mapa Bastión de las oleadas. Seleccionar oleadas o defensa continua y entre cero y tres bots aliados.
- **Online:** PvP → alojar o abrir el servidor → crear sala → modo Defensa del bastión. Admite de uno a cuatro defensores, incluidos los bots. Todos están en el mismo equipo y no hay fuego amigo. El formato es partida única.
- Cliente y servidor deben proceder de esta misma versión experimental. La APK conserva el identificador y la firma habituales: instalarla sustituye la aplicación instalada, aunque el desarrollo y la distribución permanezcan separados de `main`. El servidor incluido en el paquete Windows permite alojar el modo; el móvil también puede entrar en una sala remota.

La tienda se abre desde el panel de estadísticas de la partida. Permite equipar, comparar, mejorar y vender piezas temporales, comprar auras y reparar el bastión. Se divide en Bastión, Equipo, Poderes y Estadísticas. Con un único humano, abrirla pausa toda la simulación, incluso con bots aliados y por red. Con dos o más humanos el combate sigue; la tienda indica el estado. La pausa no suma tiempo para premios. En móvil, cerrar la tienda recupera los controles táctiles.

## Objetivo y mapa

El bastión tiene **12.000 puntos de vida**, está situado al fondo del callejón occidental y no ataca ni se cura por sí mismo. Los minions llegan desde varias entradas y desde algunas posiciones aleatorias válidas del mapa, siempre fuera de un radio de seguridad de **20 metros** del bastión. Todos tienen una ruta hasta la zona defendida.

El objetivo es resistir. La destrucción del bastión termina la partida incluso si quedan defensores vivos. La muerte de todos los defensores no la termina mientras el bastión conserve vida. El resultado queda visible hasta elegir otra partida o volver al menú. En una sala online, solo el anfitrión puede reiniciar la partida.

## Oleadas y enemigos

| Oleada | Enemigos base |
| --- | ---: |
| 1, 2 y 3 | 20 por oleada |
| 4 | 50 |
| 5 | 100 |
| 6 | 200 |
| Siguientes | +25 por oleada, hasta 500 |

Hay ocho segundos de preparación al empezar y entre oleadas. Las oleadas grandes se introducen por grupos y nunca superan **64 minions simultáneos**. En defensa continua no hay pausas y la dificultad sube cada minuto.

| Tipo | Introducción | Comportamiento |
| --- | --- | --- |
| Minion cuerpo a cuerpo | Dificultad 1 | Avanza hacia el bastión y golpea a corta distancia. |
| Tanque | Dificultad 3 | Mucha vida, movimiento más lento y golpes más fuertes. |
| Arquero | Dificultad 4 | Lanza proyectiles a distancia. |
| Mago | Dificultad 7 | Usa hechizos existentes en canalizaciones breves. |
| Capitán | Dificultad 10 | Potencia un 35 % el ataque de minions cercanos; su efecto no se acumula por varios capitanes. |

La vida y el daño escalan con la dificultad. La pausa ofensiva empieza en un segundo y baja gradualmente hasta 0,3 segundos. La canalización de los magos termina antes de iniciar esa pausa. Los minions pueden romper muros creados por los defensores para evitar bloqueos permanentes.

Con más defensores aumenta el número de enemigos y su resistencia. Se conserva la escala del mayor grupo alcanzado durante esa partida para evitar oscilaciones al desconectarse alguien. Los bots aliados defienden la zona del bastión, atacan amenazas, curan a compañeros y gastan su oro en auras o reparaciones.

Desde dificultad 3 pueden aparecer **tanques**: cuatro veces la vida de un minion normal, movimiento un 35% más lento y golpes de 75 de daño base. La vida base normal pasa a 850 (700 arqueros, 950 magos, 1500 capitanes y 3400 tanques), escalada por dificultad y número de defensores. La fuerza crece un 18% por dificultad. Las oleadas también toman como mínimo la dificultad de cada 90 segundos transcurridos.

Las apariciones alternan grupos rápidos (intervalos de 0,16–0,46 s antes del escalado) con descansos aleatorios de 1,8–5 s. El número total de la oleada sigue siendo el anunciado.

## Progresión que solo dura una partida

Cada defensor comienza en nivel 1, sin oro ni equipo procedente de su inventario permanente. Cada baja concede 10 puntos de experiencia a los defensores. Subir de nivel aumenta vida máxima, potencia y curación. Las bajas personales se contabilizan por separado.

Probabilidades iniciales por minion derrotado:

| Botín | Probabilidad | Reparto |
| --- | ---: | --- |
| Oro | 35 % | Una recogida compartida; recogerla concede el importe a cada defensor presente. |
| Pieza de equipación | 6 % por defensor | Recogida asignada a su propietario. |
| Reliquia pasiva | 2 % por defensor | Recogida asignada; sus acumulaciones duran la partida. |

Las piezas utilizan las cuatro posiciones del equipo existente y permiten comparar sus atributos con la pieza equipada. Hay espacio para 24 piezas temporales. Un inventario lleno convierte nuevas piezas recogidas en oro de esa partida. El botín del suelo desaparece a los 75 segundos y hay un límite de 96 recogidas activas.

La tienda ofrece una pieza aleatoria por 100 de oro. Cada mejora de una pieza suma un punto porcentual a cada atributo hasta su máximo original, con hasta diez mejoras. Su precio es `35 × (mejoras previas + 1)`. Vender una pieza devuelve su valor en oro temporal.

Morir conserva experiencia, nivel, oro, equipo, auras y reliquias. La reaparición tarda `4 + 0,5 × muertes` segundos, sin heredar el límite de diez segundos de Ascensión. Reiniciar borra toda esta progresión. Una cuenta autenticada puede recuperar su progreso al reconectar a esa misma partida, mientras el servidor y la sala sigan activos.

**Ninguno de estos datos temporales se escribe en Firestore.** El inventario permanente tampoco se importa a la partida.

## Reparaciones y auras

Reparar requiere estar a menos de doce metros del bastión, recupera hasta un 20 % de su vida máxima y cuesta inicialmente 100 de oro. Cada reparación multiplica el precio compartido por 1,5, redondeando hacia arriba. Alternar jugadores no reinicia el precio; una reparación sin efecto no consume oro. Los hechizos y las auras de curación no reparan el bastión.

Las auras de fuego y rayo dañan minions cercanos; la de regeneración cura defensores. Siguen al personaje, tienen hasta diez niveles y aumentan su radio y efecto. El siguiente nivel cuesta `80 × (nivel actual + 1)²`. La tabla de bonificaciones muestra los totales efectivos y las auras activas, con bordes visibles. El panel lateral mantiene nivel, experiencia, bajas, oro, daño, defensa, velocidad, regeneración y métricas de red. En las partidas online cada cliente recibe únicamente su inventario y sus recogidas asignadas; los atributos públicos y las auras de los compañeros siguen visibles.

## Habilidades automáticas

Se compran en Poderes y duran la partida; no requieren botones nuevos. Cada una admite cinco niveles, a un coste de `140 × (nivel actual + 1)²`. Sus recargas se detienen en pausa y mientras el defensor está muerto; nunca suman participación humana por sí solas.

| Habilidad | Efecto base de nivel 1 |
| --- | --- |
| Meteoritos | Cada 3 s busca un enemigo a 16 m y marca un impacto de 260 de daño base en 3 m; tarda 1,4 s en caer. |
| Berserker | Cada 16 s en combate, +50% daño y +15% velocidad durante 4,5 s; más daño y duración al mejorar. |
| Pulso glacial | Cada 5 s, 65 de daño base y ralentización en 5 m. |
| Cadena de relámpagos | Cada 4 s, 140 de daño base a hasta tres enemigos a 10 m; aumenta daño y objetivos. |

Los poderes esperan a tener enemigos válidos y no dañan aliados ni al bastión. El daño efectivo incluye tus bonificaciones; la tabla muestra berserker mientras está activo.

## Premios permanentes

| Participación activa individual | Objetos permanentes acumulados |
| --- | ---: |
| 5 minutos | 1 |
| 10 minutos | 2 |
| 15 minutos | 3 |
| 20 minutos o más | 4 |

Los hitos se registran en el servidor para humanos con identidad Google verificada. La entrega requiere que el servicio de cuentas vuelva a estar disponible. Se permiten bots aliados, que no reciben premios. El tiempo se cuenta por participación individual, con actividad reciente o canalización, y no por el tiempo previo de una sala a la que se acaba de entrar. El juego local no concede premios permanentes.

Cada hito genera una pieza con el mismo generador, probabilidades de calidad y rangos de atributos que PvP. Los premios se guardan al alcanzar el hito y no requieren ganar una partida de supervivencia. No aumentan las victorias ni las derrotas de PvP. Recibos por partida, cuenta e hito impiden duplicados; el diario guarda cada hito de forma atómica y fuerza la escritura a disco antes de confirmarlo a la simulación. Si Firebase o el servicio de cuentas falla, conserva el hito y reintenta la entrega cada 15 segundos. Si el servidor se apaga, otro inicio recupera los archivos del mismo directorio `ARCANA_REWARD_OUTBOX`. Una respuesta perdida después de crear el objeto se reintenta con el mismo recibo y no genera otro premio. La entrega no exige que el jugador siga conectado ni que la sala exista. El objeto permanente se genera al confirmar la entrega, con el generador PvP habitual; mientras tanto no se vuelve a sortear desde el cliente. Se conserva el comportamiento existente de conversión en oro permanente si el inventario de la cuenta está lleno.

## Validación reproducible

```bash
dotnet build ArcanaArena.sln -c Release
dotnet run --project tests/Arcana.Tests -c Release --no-build
dotnet run --project tests/Arcana.Tests -c Release --no-build -- --defense-benchmark
bash scripts/test-network.sh
RUNNER_TEMP=/tmp bash scripts/test-series-lobby.sh
```

La CI comprueba las transacciones y recompensas en el emulador Firestore, y ejecuta `--defense-smoke` en Godot para escritorio y móvil. El modo de verificación local de premios `--defense-rewards-only` acepta exclusivamente un emulador en loopback y el proyecto ficticio `demo-arcana-defense`; no utiliza Google Login ni credenciales reales.

Las pruebas visuales comprueban tienda, compras, inventario, límites de pantalla, recuperación del control táctil y ausencia de botones superpuestos. Las capturas y la medición de carga se guardan como artefactos de prueba. La medida de carga utiliza cuatro defensores, 64 minions y 96 piezas temporales.

Quedan para fases posteriores el arte definitivo, jefes, más clases de minion y árboles amplios de habilidades. El equilibrio y el rendimiento en dispositivos Android y Raspberry Pi físicos requieren partidas reales.
