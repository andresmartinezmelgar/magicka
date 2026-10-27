# Generador libre de Supervivencia

Petición: dedicar hasta 10 segundos a generar el mapa, sin un número de salas predeterminado ni un trazado único de pasillos. Cambios posteriores a `preview-v0.29.29-r612`, en `pruebas/supervivencia-procedural-autoguardado`.

## Geometría y azar

- Generador v2: colocación libre sobre una retícula de geometría de 2 m dentro de los límites de 160 × 120 m. La retícula sirve para construir y validar el contorno; no determina filas, columnas ni un número de salas.
- Cantidad de salas emergente: densidad aleatoria, formas y dimensiones distintas, rechazo de centros demasiado próximos y de solapamientos excesivos. Mínimo de cinco salas por seguridad: inicio y cuatro extremos distintos de los portales. El bucle tiene límite de intentos, sin fijar un total de salas.
- Seis familias de contorno con tamaños, proporciones y orientaciones aleatorias, combinables por intersecciones de caminos. El núcleo de 12 m conserva las apariciones y evita portales pegados a paredes.
- Grafo completo sobre los centros reales. Kruskal con costes de distancia perturbados aleatoriamente conecta las salas; conexiones adicionales crean un número variable de ciclos.
- Pasillos independientes por conexión: directos, diagonales, acodados, de varios giros, curvas Bézier cúbicas o paseos aleatorios dirigidos. La anchura varía entre 8 y 16 m durante el recorrido. Los paseos tienen un remate que garantiza alcanzar su destino.
- La superficie se empaqueta en rectángulos disjuntos. Suelo, límites y navegación comparten la unión exacta. El empaquetado reduce la geometría a dibujar y evita contar el área varias veces.
- Dos pares de portales seleccionan centros alejados sin repetir extremos ni usar el inicio.

## Presupuesto y continuidad

`SurvivalMapSpec.GenerationBudgetMilliseconds = 10_000`. Comprobaciones periódicas durante colocación, conexiones, empaquetado y validación. Incluye preparar la navegación, con comprobaciones por columna al generar nodos y por bloques de nodos al conectar vecinos. Es un plazo cooperativo: una operación en curso o una suspensión del proceso puede retrasar la observación del vencimiento. No se introduce una espera para consumir el tiempo disponible.

El reloj nunca selecciona la geometría ni el siguiente intento: solo aborta una generación que haya agotado su presupuesto. Los reintentos tienen un orden determinista y un límite de ocho candidatos. Esto evita que dos dispositivos reconstruyan mapas distintos para una misma semilla. Se validan las cuatro apariciones, toda la conectividad de navegación y la salida de los portales. El azar de mapa sigue separado de la horda y del botín.

La versión 1 del generador se mantiene geométricamente intacta para las partidas guardadas de la preliminar. La versión 0 conserva Anillos. Las partidas nuevas y reinicios usan la versión 2. No cambia el formato de autoguardado v2, que ya incluía la versión de terreno. El protocolo sube de 39 a 40 para impedir que un cliente antiguo entre en una partida cuyo terreno no puede reconstruir.

## Validación

Se amplían las regresiones para comprobar diversidad de cantidades y formas, cuatro portales distintos, conexión del suelo sin solapamientos, semillas extremas, reconstrucción después de expulsar la caché, conservación del generador v1 y recuperación de sus guardados. El perfil de generación registra el presupuesto y la distribución de cantidades de salas. En v2 obtener el mapa prepara también la navegación: el campo de tiempos se denomina `GeometryAndNavigation` para no confundirlo con la medición de geometría aislada del generador v1.

La petición posterior de tag y paquetes autoriza compilar y publicar 0.29.30. La compilación local de Core y Tests pasa, con 541 regresiones correctas. Las 64 semillas medidas usan entre 5 y 15 salas, con las seis familias de salas y las cinco de pasillos presentes en la muestra. Generación y navegación: mediana 29,072 ms, P95 54,122 ms y máximo 58,591 ms en .NET 8.0.21 x64. Estos son resultados del generador v2; no se reutilizan los tiempos ni las pruebas de la APK anterior. La integración C#/Godot, red, emulador, Windows y Android se vuelve a ejecutar en CI antes de publicar. Android físico no está medido.

La revisión inicial sin compilar incluyó análisis de sintaxis C# de los diez archivos afectados, comparación exacta del cuerpo del generador v1 con el commit publicado, `git diff --check`, metadatos de exportación y disparadores de Actions. La publicación autorizada de 0.29.30 actualiza de forma expresa el activador limitado a esta rama, versión y mensaje de publicación.
