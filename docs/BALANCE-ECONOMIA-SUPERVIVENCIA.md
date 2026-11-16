# Balance de la economía de Supervivencia

## Costes triplicados

Las mejoras de piezas, auras y habilidades cuestan exactamente tres veces lo que costaban en 0.29.67. El factor de equipo se aplica después de redondear cada escalón: también triplica exactamente los precios de Oro que antes acababan en cinco.

Los suministros siguen siendo compras de objetos, con su tarifa de 50. Las ventas, botín y oro ganado mantienen sus reglas. Los anillos permanentes de cuenta usan su economía propia. Continuar una partida conserva el saldo y los niveles guardados y aplica los nuevos precios a las compras siguientes.

## Precios por nivel de destino

| Nivel que se compra | Aura | Habilidad automática | Mejora mítica |
| --- | ---: | ---: | ---: |
| 1 | 120 | 240 | 120 |
| 2 | 180 | 390 | 180 |
| 3 | 270 | 630 | 300 |
| 4 | 390 | 990 | 420 |
| 5 | 570 | 1500 | 600 |
| 6 | 810 | — | 840 |
| 7 | 1140 | — | 1140 |
| 8 | 1560 | — | 1500 |
| 9 | 2100 | — | 1920 |
| 10 | 2760 | — | 2400 |

Un aura completa suma 9900, una habilidad 3750 y una pieza mítica 9420. Llegar al nivel 6 suma 2340 para un aura, 2460 para una pieza mítica y 615 para una de Plata. Una habilidad de nivel 3 suma 1260.

## Referencia de ingresos por defensor

El modelo anterior estimaba, con 40 bajas del equipo por minuto, 5423 de oro a los 20 minutos. La hipótesis era recoger el 75 % de las monedas y piezas, vender la mitad de las piezas recogidas y excluir los bonos económicos. Es una referencia teórica: no simula el combate ni cómo la nueva velocidad de mejora modifica las bajas, el riesgo o las decisiones de compra.

| Bajas del equipo por minuto | 5 min | 10 min | 15 min | 20 min |
| --- | ---: | ---: | ---: | ---: |
| 20 | 417 | 1007 | 1770 | 2712 |
| 40 | 833 | 2013 | 3540 | 5423 |
| 60 | 1250 | 3020 | 5310 | 8135 |

Estas cifras no se triplican: el cambio encarece las mejoras, no aumenta las recompensas.

## Rutas anteriores con el nuevo presupuesto

| Ruta | Auras | Habilidades | Anillos míticos | Gasto | Saldo sobre 16200 |
| --- | --- | --- | --- | ---: | ---: |
| Especializar | Una al 10 | Una al 5 | Uno al 6 | 16110 | 90 |
| Repartir | Tres al 6 | Tres al 3 | Dos al 6 | 15720 | 480 |

Esas rutas ya no caben en los 5400 de la referencia anterior. El techo ideal de cinco auras, cuatro habilidades y seis piezas míticas al máximo cuesta 121020.

## Ruta equilibrada con 5400

Cinco auras de diez niveles, cuatro habilidades de cinco y seis piezas de diez suman 130 niveles posibles. Con piezas de Oro, una ruta equilibrada es:

| Bloque | Nivel por mejora | Niveles adquiridos | Coste |
| --- | ---: | ---: | ---: |
| Cinco auras | 3/10 | 15 | 2850 |
| Cuatro habilidades | 1/5 | 4 | 960 |
| Seis piezas de Oro | 3/10 | 18 | 1440 |
| Total | | **37/130 (28,5 %)** | **5250** |

Quedan 150. El siguiente nivel cuesta 390 para un aura, 390 para una habilidad y 165 para una pieza de Oro. Ninguno se puede comprar con ese saldo. La antigua ruta de 69 niveles pasa a costar 15840.

## Comprobaciones

Las pruebas de economía cubren los precios completos por calidad, fondos insuficientes, compras rechazadas sin cobro, límites máximos, rutas de gasto y conservación de partidas guardadas. Su ejecución queda pendiente de la próxima compilación autorizada.

Queda pendiente contrastar la nueva curva con partidas reales, midiendo oro y niveles a los 5, 10, 15 y 20 minutos y separando humanos de bots. Los ingresos hipotéticos anteriores no garantizan la misma progresión con el nuevo precio.
