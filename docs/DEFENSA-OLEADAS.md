# Supervivencia en Anillos

Versión **0.19.1**, protocolo **25**, rama `feature/defensa-oleadas`. Esta rama sigue independiente de `main`. Se publican APK directas y paquetes Windows preliminares. Las dos cuentas autorizadas reciben el [canal de pruebas](CANALES-ACTUALIZACION.md); el resto conserva el canal estable. Cliente y servidor deben utilizar el mismo protocolo.

## Partida y vidas

El modo Defensa ahora es **Supervivencia**, con enemigos continuos. Se han retirado las oleadas, el castillo y sus reparaciones.

- Local: IA / Defensa → Supervivencia → Anillos, con cero a tres bots aliados.
- Online: crear sala de Supervivencia, de uno a cuatro defensores incluidos los bots. Todos colaboran y no hay fuego amigo.
- Cada jugador tiene **dos vidas**. La primera muerte permite reaparecer a los **4,5 segundos**, en el lugar de la caída; la segunda lo elimina. Las curaciones, la reanimación y reconectar con la misma cuenta no restituyen vidas agotadas.
- En cooperativo termina cuando todos los humanos han agotado sus vidas. Los bots vivos no prolongan la partida. Si el último humano con vidas sale y quedan humanos eliminados, también termina.
- El resultado permanece hasta volver al menú o iniciar otra partida. En red reinicia el anfitrión.

## Mapa, aparición y rendimiento

**Anillos** ocupa 192 × 144 m: dos circuitos rectangulares, un cruce central y salas amplias en las intersecciones. Los pasillos miden 8 m de ancho. Los patios sin suelo son inaccesibles; las rutas y colisiones son las mismas en cliente y servidor.

Dos parejas de portales unen esquinas opuestas. Cada pareja comparte color. Basta entrar en el círculo para cruzar; hay que salir del portal de destino antes de reutilizarlo. Los minions también pueden cruzar.

Las apariciones utilizan posiciones transitables de todo el mapa. La exclusión de **16 m sigue a cada defensor con vidas**, incluso mientras espera reaparecer. Evita apariciones pegadas al personaje; los enemigos ya existentes sí pueden entrar y atacar. Si no hay una posición segura se aplaza la aparición.

El cliente crea y retira **sectores de 24 × 24 m** alrededor de la cámara. También limita minions, botín y efectos a esa zona. Los sectores lejanos no se construyen. El servidor sigue simulando los enemigos del mapa entero y comparte rutas por objetivo para reducir búsquedas repetidas. El salto por un portal reposiciona la cámara y cambia los sectores cargados.

## Enemigos y dificultad

La dificultad sube cada **90 segundos**. Se ajustan vida, daño y frecuencia al número de defensores presentes, humanos o bots. Los enemigos existentes mantienen su vida cuando cambia el grupo.

| Defensores | Vida inicial normal | Máximo inicial de minions normales |
| ---: | ---: | ---: |
| 1 | 340 | 8 |
| 2 | 374 | 12 |
| 3 | 408 | 16 |
| 4 | 442 | 20 |

Los grupos alternan apariciones rápidas (0,16–0,46 s antes de escalar) y descansos variables (2,6–5,2 s). El límite normal aumenta dos plazas por dificultad; se reserva una para el coloso, con un máximo global de **64 enemigos**. La pausa entre ataques empieza en 1,35 s y baja hasta 0,4 s. Los minions buscan defensores vivos por todo el mapa, recorren pasillos y pueden romper barreras temporales.

| Tipo | Introducción | Vida base individual | Ataque base |
| --- | --- | ---: | --- |
| Minion | Desde el inicio | 340 | 20, cuerpo a cuerpo |
| Arquero | Dificultad 4 | 260 | Proyectiles de tierra |
| Tanque | Dificultad 5 | 1200 | 45; movimiento un 35 % más lento |
| Mago | Dificultad 7 | 380 | Canaliza arcano y fuego durante 0,45 s |
| Capitán | Dificultad 10 | 700 | 32; potencia un 35 % a otros enemigos a menos de 8 m |
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

Las recogidas duran 75 s, con un máximo de 96; el inventario temporal admite 24 piezas. Los sprites distinguen monedas giratorias, las cuatro piezas y cada mejora. La columna izquierda muestra en vertical las bonificaciones efectivas, además de nivel, EXP, oro, bajas y vidas.

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
