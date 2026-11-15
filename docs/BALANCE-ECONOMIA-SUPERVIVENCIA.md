# Balance de la economía de Supervivencia

## Objetivos de la curva

- Desbloquear auras y habilidades al principio, con 40 y 80 de oro respectivamente.
- Permitir combinar varios poderes intermedios y alcanzar el salto de los anillos en nivel 6.
- Alcanzar al menos la mitad de los niveles de una ruta equilibrada a los 20 minutos de referencia, sin completar toda la progresión.
- Reservar los niveles finales para las prioridades de la partida: cada incremento de precio crece o se mantiene, y el incremento final supera al inicial.
- Conservar el oro y los niveles de partidas guardadas y la tarifa permanente de los anillos de cuenta.

Esta es una estimación teórica de gasto. Los 20 minutos son una referencia de diseño. Queda pendiente contrastarla con partidas completas, mediana de oro gastable y decisiones de compra reales.

## Precios por nivel de destino

| Nivel que se compra | Aura | Habilidad automática | Mejora mítica |
| --- | ---: | ---: | ---: |
| 1 | 40 | 80 | 40 |
| 2 | 60 | 130 | 60 |
| 3 | 90 | 210 | 100 |
| 4 | 130 | 330 | 140 |
| 5 | 190 | 500 | 200 |
| 6 | 270 | — | 280 |
| 7 | 380 | — | 380 |
| 8 | 520 | — | 500 |
| 9 | 700 | — | 640 |
| 10 | 920 | — | 800 |

Un aura completa suma 3300, una habilidad 1250 y una pieza mítica 3140. Llegar al nivel 6 suma 780 para un aura, 820 para una pieza mítica y 205 para una de Plata. Una habilidad de nivel 3 suma 420.

## Hipótesis de ingresos por defensor

Se consideran ritmos constantes e hipotéticos de 20, 40 o 60 bajas del equipo por minuto. El oro de las monedas se comparte con cada defensor; el modelo no lo divide entre los jugadores.

Las reglas actuales dan un 35 % de probabilidad de monedas por baja. Una moneda da 8 más 2 veces la dificultad, que en este modelo aumenta cada 90 segundos. Se supone que se recoge el 75 % de las monedas y del equipo y se vende el 50 % de las piezas recogidas. El equipo aparece con un 6 % por baja y defensor; su venta media ponderada por calidades es 40.55 de oro.

Se excluyen los bonos del anillo de oro y afijos económicos, y solo se usa la dificultad temporal. El resultado es un valor esperado redondeado, sin simular combates, variación de bajas, rutas, riesgo de recoger botín, dificultad de sectores ni efectos de las compras sobre la supervivencia.

| Bajas del equipo por minuto | 5 min | 10 min | 15 min | 20 min |
| --- | ---: | ---: | ---: | ---: |
| 20 | 417 | 1007 | 1770 | 2712 |
| 40 | 833 | 2013 | 3540 | 5423 |
| 60 | 1250 | 3020 | 5310 | 8135 |

## Dos rutas con un presupuesto de referencia de 5400

El escenario intermedio estima 5423 de oro a los 20 minutos. Se toma 5400 como presupuesto de contraste:

| Ruta | Auras | Habilidades | Anillos míticos | Gasto | Saldo |
| --- | --- | --- | --- | ---: | ---: |
| Especializar | Una al 10 | Una al 5 | Uno al 6 | 5370 | 30 |
| Repartir | Tres al 6 | Tres al 3 | Dos al 6 | 5240 | 160 |

Ambas rutas caben en ese presupuesto. Tras repartir, el siguiente nivel de cualquiera de esas auras cuesta 380, por encima del saldo disponible. Tras especializar, ni siquiera cabe desbloquear otra aura de 40. Las pruebas del motor realizan esas compras y comprueban el saldo y el rechazo del siguiente gasto.

El techo ideal de cinco auras, cuatro habilidades y seis piezas míticas al máximo suma 40340. Completar ese techo excede ampliamente el presupuesto de referencia. Los objetos de menor calidad ofrecen mejoras más baratas para abrir otras rutas de gasto.

## Referencia de media progresión

Se cuentan niveles adquiridos, no porcentaje de daño ni de oro gastado: cinco auras de diez niveles, cuatro habilidades de cinco y seis piezas de diez suman **130 niveles**. La ruta equilibrada con equipo de Oro permite:

| Bloque | Nivel por mejora | Niveles adquiridos | Coste |
| --- | ---: | ---: | ---: |
| Cinco auras | 5/10 | 25 | 2550 |
| Cuatro habilidades | 2/5 | 8 | 840 |
| Seis piezas de Oro | 6/10 | 36 | 1890 |
| Total | | **69/130 (53,1 %)** | **5280** |

Quedan 120 de los 5400 de referencia; no alcanza para el siguiente escalón de ninguno de esos bloques. La calidad del equipo, el ritmo real de bajas, los bonos de oro y la recogida cambian el resultado: este objetivo no garantiza un porcentaje idéntico en cada partida. Las pruebas realizan las compras y comprueban niveles, saldo y rechazos.

## Comprobación pendiente en partidas

Revisar el oro disponible a los 5, 10, 15 y 20 minutos, el momento del primer poder y del nivel 6 de los anillos, y cuántos niveles finales se compran. Medir por separado humanos y bots y revisar la inversión temprana en el anillo de oro, que puede elevar el presupuesto del modelo. Ajustar estas tablas con esos resultados, conservando el arranque accesible y la progresión creciente.
