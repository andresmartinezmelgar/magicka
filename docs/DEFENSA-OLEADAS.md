# Supervivencia en Anillos

Versión estable **0.21.0**, protocolo **27**, integrada en `main`. Se publican APK directas, paquetes Windows y deltas de hasta tres versiones estables anteriores. Las dos cuentas autorizadas conservan el [canal de pruebas](CANALES-ACTUALIZACION.md); el resto recibe el canal estable. La horda ampliada requiere actualizar también el servidor incluido en el paquete Windows a 0.21.0.

## Partida y vidas

El modo Defensa ahora es **Supervivencia**, con enemigos continuos. Se han retirado las oleadas, el castillo y sus reparaciones.

- Local: IA / Defensa → Supervivencia → Anillos, con cero a tres bots aliados.
- Online: crear sala de Supervivencia, de uno a cuatro defensores incluidos los bots. Todos colaboran y no hay fuego amigo.
- Cada jugador tiene **dos vidas**. La primera muerte permite reaparecer a los **4,5 segundos**, en el lugar de la caída; la segunda lo elimina. Las curaciones, la reanimación y reconectar con la misma cuenta no restituyen vidas agotadas.
- En cooperativo termina cuando todos los humanos han agotado sus vidas. Los bots vivos no prolongan la partida. Si el último humano con vidas sale y quedan humanos eliminados, también termina.
- El resultado permanece hasta volver al menú o iniciar otra partida. En red reinicia el anfitrión.

En móvil, toca **Traslación** o **Meteoro**, ajusta dirección y distancia con el joystick derecho y suelta para lanzar. Una marca muestra el destino; el meteoro indica además su área. Tocar otra vez el mismo botón o cancelar el gesto no consume energía. Traslación recorre hasta 7 m sin atravesar muros ni conservar empujes anteriores. Meteoro alcanza 14 m, avisa durante 1,4 s y no daña a los defensores.

**Reanimar** recupera con 600 de vida a un aliado caído a menos de 8 m, durante la espera de su primera reaparición. No recupera vidas agotadas. Si no hay objetivo, el botón muestra «Sin aliado» y no consume energía. Un muro sobre el lugar de la caída obliga a reaparecer en suelo libre cercano.

Equipar o quitar piezas mantiene abierto el selector; Atrás vuelve al inventario y salir aplica la selección. El lateral de combate muestra solo las mejoras recogidas. Los atributos del equipo y los totales siguen disponibles en el inventario y las estadísticas de la tienda.

## Mapa, aparición y rendimiento

**Anillos** ocupa 192 × 144 m: dos circuitos rectangulares, un cruce central y salas amplias en las intersecciones. Los pasillos miden 8 m de ancho. Los patios sin suelo son inaccesibles; las rutas y colisiones son las mismas en cliente y servidor.

Dos parejas de portales unen esquinas opuestas. Cada pareja comparte color. Basta entrar en el círculo para cruzar; hay que salir del portal de destino antes de reutilizarlo. Los minions también pueden cruzar.

Las apariciones utilizan posiciones transitables de todo el mapa. La exclusión de **16 m sigue a cada defensor con vidas**, incluso mientras espera reaparecer. Evita apariciones pegadas al personaje; los enemigos ya existentes sí pueden entrar y atacar. Si no hay una posición segura se aplaza la aparición.

La carga prepara los 48 **sectores de 24 × 24 m**, sus materiales y portales. Durante el juego solo se dibujan los sectores cercanos, y se limita también la representación de minions, botín y efectos a esa zona. Los sectores lejanos permanecen ocultos en una caché; cambiar de zona o saltar por un portal reutiliza la geometría preparada. Una reserva inicial de 192 representaciones de enemigos, repartida por tipos, reduce la creación de nodos al aparecer. La reserva puede crecer de forma acotada si la mezcla de tipos lo necesita. El servidor sigue simulando los enemigos del mapa entero y comparte rutas por objetivo para reducir búsquedas repetidas.

Todos los modos precargan las zonas del mapa, atlas de magos, sonidos, partículas, combinaciones, barreras, meteoros e iconos, dibujando fotogramas de preparación antes de habilitar los controles. Cancelar la carga devuelve al menú sin arrancar la simulación. La precarga reduce los tirones de primera aparición; los FPS también dependen del dispositivo y de los elementos visibles.

En 0.20.0, la IA evita recrear el equipo de cada minion si su daño y velocidad no cambian, y busca capitanes y objetivos sin crear consultas temporales. El HUD reutiliza los datos que no cambian y prepara las estadísticas al abrir su pestaña. En la comparación local contra 0.19.3, con cuatro defensores, 64 minions, 96 piezas y 1.200 pasos, la memoria temporal de simulación pasó de 41.774 a 14.140 bytes por paso (66 % menos). El snapshot final normalizado fue idéntico. Esta medición no representa los FPS de un dispositivo físico.

## Enemigos y dificultad

La dificultad sube cada **90 segundos**. Se ajustan vida, daño y frecuencia al número de defensores presentes, humanos o bots. Los enemigos existentes mantienen su vida cuando cambia el grupo.

| Defensores | Vida inicial normal | Límite inicial de enemigos |
| ---: | ---: | ---: |
| 1 | 340 | 24 |
| 2 | 374 | 32 |
| 3 | 408 | 40 |
| 4 | 442 | 48 |

Los grupos alternan apariciones rápidas (0,12–0,32 s antes de escalar) y descansos variables (1,3–3,1 s). El límite aumenta ocho plazas por dificultad; se reserva una para el coloso, con un máximo global de **192 enemigos**. Se conservan la vida y el daño de los seis tipos anteriores. La pausa entre ataques empieza en 1,35 s y baja hasta 0,4 s. Los minions buscan defensores vivos por todo el mapa, recorren pasillos y pueden romper barreras temporales.

El tope real también depende de los FPS de cada dispositivo. Se empieza con un presupuesto de 96; cada 5 s de juego activo se mide la media, excluyendo carga, tienda, pausa y ventana sin foco. Una ventana lenta reduce el presupuesto; tres ventanas estables permiten subir una banda. En cooperativo se usa el menor presupuesto de los humanos conectados. Si ya hay demasiados enemigos, se frenan las apariciones y solo se retiran los que estén a más de 60 m de todos los defensores, excluyendo colosos, sin bajas, experiencia ni botín. Los cercanos siguen visibles y activos. El HUD muestra enemigos actuales/límite y la guía también el presupuesto por rendimiento.

| FPS sostenidos | Presupuesto máximo |
| ---: | ---: |
| Menos de 18 | 32 |
| 18–24 | 48 |
| 25–31 | 64 |
| 32–39 | 96 |
| 40–47 | 128 |
| 48–55 | 160 |
| 56 o más | 192 |

| Tipo | Introducción | Vida base individual | Ataque base |
| --- | --- | ---: | --- |
| Minion | Desde el inicio | 340 | 20, cuerpo a cuerpo |
| Corredor | Dificultad 2 | 200 | 14; corre un 45 % más que el básico |
| Arquero | Dificultad 4 | 260 | Proyectiles de tierra |
| Detonador | Dificultad 4 | 240 | 95 en 3,2 m; avisa 1,1 s y muere al explotar |
| Tanque | Dificultad 5 | 1200 | 45; movimiento un 35 % más lento |
| Bruto | Dificultad 4 | 1500 | 70 en 2,8 m; avisa 0,9 s y se detiene |
| Mago | Dificultad 4 | 380 | Canaliza arcano y fuego durante 0,45 s |
| Chamán | Dificultad 3 | 540 | 12; cura 90 × fuerza a otro enemigo a 8 m cada 3 s |
| Capitán | Dificultad 5 | 700 | 32; potencia un 35 % a otros enemigos a menos de 8 m |
| Coloso | Intervalos variables | 4200 | 65; cuerpo a cuerpo de 2,4 m |

El primer coloso aparece entre **120 y 180 s**. Tras derrotarlo se espera un intervalo nuevo de **90 a 150 s**. Solo hay uno activo; su vida aumenta un 40 % por defensor adicional, además de la dificultad. La guía muestra los valores del último ejemplar descubierto.

Los minions normales tienen oportunidades periódicas de acelerar: un **60 % más de velocidad durante 2–4 s**, señalado por un anillo azul. El impulso termina y recuperan su velocidad base. Los colosos no reciben este impulso.

Los bots escoltan a los humanos, reparten amenazas y posiciones y asignan un sanador por aliado herido. Conservan la carga de sus rayos y compran auras con su oro.

## Tienda y progreso temporal

Abrir **Equipo / tienda** pausa toda la partida si solo hay un humano, también con bots y por red. Con más humanos el combate continúa. La pausa detiene el tiempo para premios. Las pestañas son Partida, Equipo, Poderes, Estadísticas y Guía.

El inventario reutiliza el de `main`: cuatro ranuras, selección por calidad, comparación, conjuntos, aportaciones por pieza y suma total. Cada pieza tiene tres iconos: vender, equipar o quitar y mejorar. La selección se aplica al volver a la tienda. Una pieza aleatoria cuesta 100 de oro; mejorar cuesta `35 × (mejoras previas + 1)`, hasta diez mejoras y sin superar sus máximos.

Nivel, oro, piezas, reliquias, auras y habilidades se conservan tras la primera muerte y se borran al reiniciar. **No se escriben en Firestore ni se importa el equipo permanente.** La misma cuenta puede recuperar progreso y vidas al reconectar a la misma sala activa.

Las bajas normales dan 10 EXP compartida y los colosos 60 EXP a los defensores con vidas. El botín conserva las probabilidades de PvP para calidad y estadísticas de las piezas:

| Botín | Probabilidad por baja | Reparto |
| --- | ---: | --- |
| Oro | 35 % | Recogerlo concede su importe a los defensores presentes |
| Equipo | 6 % por defensor | Personal |
| Reliquia | 2 % por defensor | Personal y acumulable |

Las recogidas duran 75 s, con un máximo de 96; el inventario temporal admite 24 piezas. Los sprites distinguen monedas giratorias, las cuatro piezas y cada mejora. La columna izquierda muestra en vertical solo las mejoras recogidas, además de nivel, EXP, oro, bajas y vidas. Las aportaciones del equipo se consultan en el inventario y en Estadísticas.

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
