# v0.12.15 — Frente de conquista

El nuevo mapa se selecciona en Práctica y al crear una sala. Solo admite Guerra: gana el equipo que conquista los cinco puntos en orden, sin puntuación de bajas ni tickets.

El pasillo tiene 480 unidades de largo y 11.520 unidades² de suelo, con ocho tramos desplazados y pasos estrechos. Azul y rojo avanzan desde extremos opuestos. La reaparición queda siete unidades detrás del último punto conectado a la base y retrocede cuando el enemigo neutraliza terreno. Las entradas tardías usan el frente actual.

El servidor permite capturar únicamente el primer punto libre o enemigo desde la base de cada equipo. Una infiltración o un traslado no permiten saltarse puntos. La disputa detiene el progreso y la nueva partida reinicia terreno, reapariciones y eventos.

Tres pares de portales permiten traslados bidireccionales con dos segundos de enfriamiento. De forma aleatoria, cada 12–22 segundos, aparecen meteoros avisados o muros de piedra destructibles. El primer evento tarda 10–18 segundos. Los muros desaparecen también a los 30 segundos; su colisión y sus impactos usan la simulación compartida. El minimapa muestra portales, reapariciones, muros y avisos.

Los círculos de captura conservan su progreso y color de equipo, sin nombres ni etiquetas. Las barreras de escudo conservan su color elemental y sus efectos, sin las mallas negras del borde.

La nueva geometría exige ampliar el apuntado, adaptar la validación al número de puntos y actualizar el protocolo a 9. Cliente y servidor deben actualizarse juntos a esta versión.

Las pruebas cubren geometría y rutas, captura secuencial para ambos equipos, neutralización, disputa, victoria, respawn, entradas tardías, portales, meteoros, destrucción de muros, eventos reproducibles y limpieza. La comprobación Godot recorre los cinco mapas y comprueba la ausencia de etiquetas y bordes negros; la CI incluye una vista general del nuevo pasillo.
