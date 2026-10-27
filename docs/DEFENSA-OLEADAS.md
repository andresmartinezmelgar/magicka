# Supervivencia

Versión de pruebas **0.29.29**, basada en la estable **0.29.28**, con protocolo **39**. Incluye el mapa procedural y el autoguardado. Cliente y servidor deben usar la misma rama; la publicación preliminar conserva separada la versión estable.

## Partida y vidas

El modo Defensa ahora es **Supervivencia**, con enemigos continuos. Se han retirado las oleadas, el castillo y sus reparaciones.

- Local: IA / Defensa → Supervivencia, con cero a tres bots aliados.
- Online: crear sala de Supervivencia, de uno a cuatro defensores incluidos los bots. Todos colaboran y no hay fuego amigo.
- Cada jugador tiene **dos vidas**. La primera muerte permite reaparecer a los **4,5 segundos**, en el lugar de la caída; la segunda lo elimina. Las curaciones, la reanimación y reconectar con la misma cuenta no restituyen vidas agotadas.
- En cooperativo termina cuando todos los humanos han agotado sus vidas. Los bots vivos no prolongan la partida. Si el último humano con vidas sale y quedan humanos eliminados, también termina.
- El resultado permanece hasta volver al menú o iniciar otra partida. En red reinicia el anfitrión.

## Bestiario del perfil

Cada monstruo muestra su imagen, descripción, bajas de la cuenta y una tabla con vida máxima, daño, velocidad, distancia para atacar y pausa entre ataques. Las fichas incluyen también sus áreas, preparación, curaciones, escudos y refuerzos cuando corresponden. Los magos desglosan sus hechizos: daño por segundo para los canales, por impacto para las rocas y por fragmento para las esquirlas.

Los selectores de dificultad y defensores permiten consultar las cifras del combate para distintos grupos, incluidos los bots. La dificultad aumenta cada 90 segundos. Las cifras de daño se calculan contra un mago equilibrado, seco y sin defensas ni potenciadores temporales. La preparación y canalización se añaden a la pausa indicada entre ataques. Consultar otra dificultad no modifica las bajas ni guarda datos en Firestore.

## Guardar y continuar en local

La supervivencia local se autoguarda al activar la partida, cada **20 segundos**, al cambiar de aplicación y al volver al menú. También puedes usar **Guardar partida** o **Guardar y salir**. En **Contra IA / Práctica**, **Continuar supervivencia guardada** recupera el último punto incluso si Android cerró el proceso. Hay una ranura por dispositivo: activar una partida nueva sustituye el punto anterior; cancelar su carga conserva el anterior.

Se conservan el mapa y su semilla, el mago, sus aliados IA, vidas, salud y estados, equipo y afijos, mejoras, auras, habilidades pasivas, reliquias, enemigos, botín y el tiempo de dificultad y jefes. Los controles se sueltan al continuar y el tiempo fuera del juego no avanza la simulación. La escritura es atómica y se conserva una copia válida anterior que se usa si el archivo principal está dañado.

Continuar mantiene disponible el último punto y lo renueva al activar el juego. Así, otro cierre de Android durante la recuperación no obliga a empezar de cero. Agotar las vidas humanas invalida tanto el punto como su respaldo. El formato de guardado v2 incluye el mapa; se siguen leyendo los v1 con el terreno antiguo. Los nuevos guardados requieren esta rama o una versión posterior compatible.

Cambiar de aplicación conserva también una carga en curso: el tiempo en segundo plano no agota su plazo. Al volver a una partida activa, se muestra la pausa y puedes reanudarla. No se avanza la simulación local en segundo plano.

La partida se guarda únicamente en el dispositivo; no se almacena en Firestore. Este guardado se ofrece para supervivencia local, también con bots aliados. Las salas online conservan sus reglas de conexión. Las bajas del bestiario mantienen su identificador de partida y su acumulado al continuar.

En móvil, toca **Traslación** o **Meteoro**, ajusta dirección y distancia con el joystick derecho y suelta para lanzar. Una marca muestra el destino; el meteoro indica además su área. Tocar otra vez el mismo botón o cancelar el gesto no consume energía. Traslación recorre hasta 7 m sin atravesar muros ni conservar empujes anteriores. Meteoro alcanza 14 m, avisa durante 1,4 s y no daña a los defensores.

**Reanimar** recupera con 600 de vida a un aliado caído a menos de 8 m, durante la espera de su primera reaparición. No recupera vidas agotadas. Si no hay objetivo, el botón muestra «Sin aliado» y no consume energía. Un muro sobre el lugar de la caída obliga a reaparecer en suelo libre cercano.

Equipar o quitar piezas mantiene abierto el selector; Atrás vuelve al inventario y salir aplica la selección. El lateral de combate muestra solo las mejoras recogidas. Los atributos del equipo y los totales siguen disponibles en el inventario y las estadísticas de la tienda.

## Mapa, aparición y rendimiento

Cada partida nueva y cada reinicio generan **Laberinto**, de **160 × 120 m**: aproximadamente un 31 % menos de superficie delimitadora que los 192 × 144 m anteriores. Cambian las dimensiones y posiciones de nueve salas, las conexiones y los giros de los pasillos. Un árbol de conexiones garantiza acceso a todas las salas y se añaden de dos a cuatro ciclos para escapar. Los pasillos miden de 8 a 12 m de ancho y el centro reserva una sala de 36 × 30 m para aparecer.

La semilla y la versión del generador viajan en el snapshot y el guardado. Simulación, rutas, colisiones, portales, vista y minimapa reconstruyen el mismo terreno. Continuar conserva ese mapa; los guardados antiguos mantienen **Anillos** hasta empezar otra partida. El generador usa su propio azar, independiente de la horda y del botín.

Dos parejas de portales unen salas opuestas, con sus extremos dentro de suelo seguro y accesible. Cada pareja comparte color. Basta entrar en el círculo para cruzar; hay que salir del portal de destino antes de reutilizarlo. Los minions también pueden cruzar.

Las apariciones utilizan posiciones transitables de todo el mapa. La exclusión de **16 m sigue a cada defensor con vidas**, incluso mientras espera reaparecer. Evita apariciones pegadas al personaje; los enemigos ya existentes sí pueden entrar y atacar. Si no hay una posición segura se aplaza la aparición.

La carga prepara los **sectores de 24 × 24 m** que cubren el mapa, sus materiales y portales. Durante el juego solo se dibujan los sectores cercanos, y se limita también la representación de minions, botín y efectos a esa zona. Los sectores lejanos permanecen ocultos en una caché; cambiar de zona o saltar por un portal reutiliza la geometría preparada. Una reserva inicial de 192 representaciones de enemigos, repartida por tipos, reduce la creación de nodos al aparecer. La reserva puede crecer de forma acotada si la mezcla de tipos lo necesita. El servidor sigue simulando los enemigos del mapa entero y comparte rutas por objetivo para reducir búsquedas repetidas.

Todos los modos precargan las zonas del mapa, atlas de magos, sonidos, partículas, combinaciones, barreras, meteoros e iconos, dibujando fotogramas de preparación antes de habilitar los controles. Cancelar la carga devuelve al menú sin arrancar la simulación. La precarga reduce los tirones de primera aparición; los FPS también dependen del dispositivo y de los elementos visibles.

En 0.20.0, la IA evita recrear el equipo de cada minion si su daño y velocidad no cambian, y busca capitanes y objetivos sin crear consultas temporales. El HUD reutiliza los datos que no cambian y prepara las estadísticas al abrir su pestaña. En la comparación local contra 0.19.3, con cuatro defensores, 64 minions, 96 piezas y 1.200 pasos, la memoria temporal de simulación pasó de 41.774 a 14.140 bytes por paso (66 % menos). El snapshot final normalizado fue idéntico. Esta medición no representa los FPS de un dispositivo físico.

## Enemigos y dificultad

La dificultad sube cada **90 segundos**. Cada una de las primeras cuatro subidas aumenta vida y daño un **18 % acumulativo**; las siguientes, un **22 %**, hasta un máximo de ×1000. Se ajustan vida y apariciones al número de defensores presentes, humanos o bots. Los enemigos existentes mantienen la vida máxima y fuerza con las que aparecieron, incluso al cambiar el grupo o la dificultad; su velocidad y pausa entre ataques sí siguen la dificultad actual.

| Defensores | Vida inicial normal | Límite inicial de enemigos |
| ---: | ---: | ---: |
| 1 | 340 | 24 |
| 2 | 374 | 32 |
| 3 | 408 | 40 |
| 4 | 442 | 48 |

Las apariciones siguen una cadencia continua de 0,32–0,48 s antes de escalar por dificultad y grupo, sin descansos entre grupos. Se prioriza el entorno de los defensores, manteniendo al menos 16 m de separación al aparecer. Si la población está completa, un enemigo normal a más de 60 m de todos los defensores puede volver a sus alrededores: conserva identidad, salud y estados, sin generar bajas ni premios. Los colosos y los enemigos próximos no se recolocan. El límite aumenta ocho plazas por dificultad; se reserva una para el coloso, con un máximo global de **192 enemigos**. La vida y el daño base se conservan. La pausa entre ataques empieza en 1,35 s y baja hasta 0,4 s. Los minions recorren pasillos y pueden romper barreras temporales.

El tope real también depende de los FPS de cada dispositivo. Se empieza con un presupuesto de 48; cada 2 s de juego activo se miden la media y el percentil 95, excluyendo carga, tienda, pausa y ventana sin foco. Una ventana lenta reduce el presupuesto; cinco ventanas estables permiten subir una banda, dejando margen sobre el objetivo de 30 FPS. El mínimo es 16 enemigos. No es una garantía absoluta de FPS para cualquier hardware. En cooperativo se usa el menor presupuesto de los humanos conectados. Si ya hay demasiados enemigos, se frenan las apariciones y solo se retiran los que estén a más de 60 m de todos los defensores, excluyendo colosos, sin bajas, experiencia ni botín. Los cercanos siguen visibles y activos. El HUD muestra enemigos actuales/límite y la guía también el presupuesto por rendimiento.

| FPS sostenidos | Presupuesto máximo |
| ---: | ---: |
| Menos de 24 | 16 |
| 24–29 | 24 |
| 30–33 | 32 |
| 34–39 | 48 |
| 40–47 | 64 |
| 48–55 | 96 |
| 56–63 | 128 |
| 64–79 | 160 |
| 80 o más | 192 |

| Tipo | Introducción | Vida base individual | Ataque base |
| --- | --- | ---: | --- |
| Minion | Desde el inicio | 340 | 48, cuerpo a cuerpo |
| Corredor | Dificultad 2 | 200 | 32; corre un 45 % más que el básico |
| Arquero | Dificultad 4 | 260 | 40 por flecha de tierra |
| Detonador | Dificultad 4 | 240 | 260 en 3,2 m; avisa 1,1 s y muere al explotar |
| Tanque | Dificultad 5 | 1200 | 110; movimiento un 35 % más lento |
| Bruto | Dificultad 4 | 1500 | 180 en 2,8 m; avisa 0,9 s y se detiene |
| Mago | Dificultad 4 | 380 | Alterna haces, relámpagos y conos de arcano y fuego |
| Chamán | Dificultad 3 | 540 | 30; cura 90 × fuerza a otro enemigo a 8 m cada 3 s |
| Capitán | Dificultad 5 | 700 | 95 en 3,4 m, avisa 1,05 s; potencia un 35 % a otros enemigos a menos de 8 m |
| Coloso | Intervalos variables | 4200 | 280 en 5 m; avisa 1,05 s y tiene una pausa un 35 % menor |
| Tejedor glacial | Dificultad 4 | 760 | Conos de frío, cinco esquirlas y rocas heladas |
| Conjurador ígneo | Dificultad 5 | 920 | Rocas arcanas o de fuego, con explosión y suelo ardiente |
| Guardián rúnico | Dificultad 6 | 1900 | 160 en 3,8 m, avisa 1,2 s; escudo de tierra de 280 × fuerza |
| Prisma elemental | Dificultad 3 | 640 | 48; solo recibe el elemento de su color y fija una víctima hasta 10 s |
| Oráculo de asedio | Dificultad 6 | 1100 | Tres cristales de 160, radio 3 m, alcance 24 m; aura de escudos de 14 m |

Las cifras de la tabla son las bases a dificultad 1 antes de multiplicarlas por la fuerza de aparición. El bestiario del perfil permite consultar cualquier dificultad y grupo. Los magos usan una potencia de 0,42 (el conjurador ígneo añade un 20 %), canalizan haces y relámpagos durante **1,2 s**, y conos durante **0,9 s**. La velocidad básica empieza en 3,67 m/s y aumenta hasta 5,27 m/s; cada tipo aplica su propio multiplicador.

Una congelación no se renueva por nuevos impactos sobre un defensor. Al congelarse se limpia el frío acumulado y, tras descongelarse, el defensor dispone de **tres segundos sin acumular frío ni volver a congelarse**. Los ataques helados siguen causando daño. El fuego puede liberar antes del hielo. La protección incluye a los bots y se conserva al guardar, continuar o reconectar; el hielo PvP mantiene sus reglas.

El primer coloso aparece entre **120 y 180 s**. Tras derrotarlo se espera un intervalo nuevo de **90 a 150 s**. Solo hay uno activo; su vida aumenta un 40 % por defensor adicional, además de la dificultad. La guía muestra los valores del último ejemplar descubierto.

Los minions normales tienen oportunidades periódicas de acelerar: un **60 % más de velocidad durante 2–4 s**, señalado por un anillo azul. El impulso termina y recuperan su velocidad base. Los colosos no reciben este impulso.

Los bots escoltan a los humanos, buscan puestos a un mínimo de 3,5 m y se apartan si coinciden con ellos. Reparten amenazas y posiciones y asignan un sanador por aliado herido. Sus ataques priorizan la afinidad del mago; ante un prisma utilizan su único punto débil. Conservan la carga de sus rayos y compran auras con su oro.

## Tienda y progreso temporal

Abrir **Equipo / tienda** pausa toda la partida si solo hay un humano, también con bots y por red. Con más humanos el combate continúa. La pausa detiene el tiempo para premios. Las pestañas son Partida, Equipo, Poderes, Estadísticas y Guía.

El inventario reutiliza el de `main`: cuatro ranuras, selección por calidad, comparación, conjuntos, aportaciones por pieza y suma total. Los nombres de los atributos ocupan la columna derecha. Cada pieza tiene tres iconos: vender, equipar o quitar y mejorar. La selección se aplica al volver a la tienda. Una pieza aleatoria cuesta 100 de oro; mejorar cuesta `35 × (mejoras previas + 1)`. Cada atributo tiene diez incrementos de un punto porcentual sobre su valor inicial: −6 % → +4 % y +10 % → +20 %. El máximo individual se fija al recibir la pieza temporal y no modifica el equipo permanente ni los límites totales de combate.

Nivel, oro, piezas, reliquias, auras y habilidades se conservan tras la primera muerte y se borran al reiniciar. **No se escriben en Firestore ni se importa el equipo permanente.** La misma cuenta puede recuperar progreso y vidas al reconectar a la misma sala activa.

Las bajas normales dan 10 EXP compartida y los colosos 60 EXP a los defensores con vidas. El botín conserva las probabilidades de PvP para calidad y estadísticas de las piezas:

| Botín | Probabilidad por baja | Reparto |
| --- | ---: | --- |
| Oro | 35 % | Recogerlo concede su importe a los defensores presentes |
| Equipo | 6 % por defensor | Personal |
| Reliquia | 1 % por defensor | Personal y acumulable |

Las recogidas duran 75 s, con un máximo de 96; el inventario temporal admite 24 piezas. Los sprites distinguen monedas giratorias, las cuatro piezas y cada mejora. Las mejoras recogidas se consultan en Reliquias del menú o de la tienda. El HUD muestra nivel, EXP, oro, bajas y vidas. Las aportaciones del equipo se consultan en el inventario y en Estadísticas.

Las auras de fuego, rayo y regeneración siguen al personaje. Admiten diez niveles y cuestan `80 × (nivel actual + 1)²`. Las habilidades automáticas admiten cinco niveles y cuestan `140 × (nivel actual + 1)²`:

| Habilidad | Nivel 1 |
| --- | --- |
| Meteoritos | Cada 3 s: 260 de daño en 3 m, con aviso previo y enemigos a 16 m |
| Berserker | Cada 16 s en combate: +50 % daño y +15 % velocidad durante 4,5 s |
| Pulso glacial | Cada 5 s: 65 de daño y ralentización en 5 m |
| Cadena de relámpagos | Cada 4 s: 140 de daño a hasta tres enemigos a 10 m |

Guía conserva porcentajes de drop y las estadísticas del último enemigo de cada tipo descubierto, incluidos colosos, aunque haya muerto. Estadísticas separa las cuatro piezas y el total efectivo.

## Premios permanentes y pendientes

Con cuenta verificada y servidor configurado: un objeto cada **cinco minutos activos**, hasta cuatro por partida. No se conceden premios permanentes en local ni a bots. La pausa y la inactividad no suman participación; tampoco se sigue sumando tras agotar las vidas.

Cada hito se escribe de forma duradera antes de confirmarse. Si falla Firebase o el servicio de cuentas, queda pendiente y se reintenta cada 15 s. Reiniciar el servidor con el mismo `ARCANA_REWARD_OUTBOX` recupera las entregas, aunque el jugador ya no esté conectado. El recibo por partida, cuenta e hito impide duplicados y conserva el generador PvP habitual.

## Verificación

```bash
dotnet build ArcanaArena.sln -c Release
dotnet run --project tests/Arcana.Tests -c Release --no-build
dotnet run --project tests/Arcana.Tests -c Release --no-build -- --defense-balance
dotnet run --project tests/Arcana.Tests -c Release --no-build -- --defense-benchmark
```

La auditoría visual real comprueba móvil y escritorio, límites táctiles, inventario, guía, coloso, sectores cargados y descarga tras un portal. Las mediciones automatizadas no sustituyen la prueba de rendimiento en un Android físico.

## Cambios de 0.21.0

El inventario permite combinar filtros de familia elemental, calidad y atributo. Cada pieza no equipada muestra las diferencias de atributos respecto a la equipada; la comparación no incluye los conjuntos. Filtrar no cambia la selección ni escribe en Firestore.

Ante una caída de red, el cliente intenta recuperar la misma conexión durante 30 segundos. Se conservan el personaje, posición, inventario y vidas; el personaje continúa expuesto en el servidor si hay otros jugadores conectados. No se concede invulnerabilidad ni actividad para premios. Salir voluntariamente elimina la plaza inmediatamente. La recuperación utiliza un token aleatorio de la sesión y, con Google, exige la misma identidad verificada. No recupera partidas después de reiniciar el servidor.

El ajuste gráfico reduce primero partículas decorativas, manteniendo las áreas y avisos de ataque. Las ventanas incorporan el percentil 95 del tiempo por fotograma; los picos persistentes y la carga de simulación del servidor también limitan las apariciones. Los enemigos cercanos y los colosos siguen protegidos frente a la retirada por rendimiento.

El chamán puede aparecer desde el minuto 3, el bruto y el mago desde 4:30 y el capitán desde 6:00; no cambian sus atributos base.

El prisma conserva su color elemental incluso al arder o congelarse. Las mezclas solo aportan el daño de ese elemento. Agua pura causa 55 de daño base por carga contra la variante azul. Vida y Escudo no aparecen como vulnerabilidades.

El oráculo retrocede por debajo de 16 m, bombardea desde hasta 24 m y concede escudos a 14 m sin atravesar paredes. El escudo base es 220 × fuerza, con reparaciones de hasta 110 × fuerza cada 4 s; dura seis segundos desde el último pulso. Varias auras no aceleran la reparación. Los cristales avisan entre 1,4 y 2 s y se cancelan al morir el lanzador. Hay como máximo dos oráculos y ocho prismas en la generación natural.

Las hordas respetan un intervalo mínimo de aparición de 0,4 s con presupuesto de hasta 48 enemigos, 0,25 s hasta 96 y 0,18 s por encima. Los refuerzos llegan en paquetes de uno, dos o tres según el presupuesto. La reducción de población retira únicamente enemigos lejanos.

El [equipamiento compartido de bots](EQUIPAMIENTO-BOTS-ANALISIS.md) queda analizado y sin implementar.
