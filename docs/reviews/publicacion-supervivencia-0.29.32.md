# Supervivencia 0.29.32: configuración predeterminada y mejoras de combate

Rama `pruebas/supervivencia-procedural-autoguardado`, código Android 29032, catálogo 11 y protocolo 45. La petición expresa de tag y APK autoriza la compilación y publicación de pruebas; no se mezcla con main.

## Configuración predeterminada

La séptima pestaña de la tienda permite activar o desactivar la selección, elegir uno de los ocho elementos y ordenar hasta cinco afijos Míticos distintos. Primero compara los rangos máximos de los afijos elegidos, en orden; después el potencial del elemento, el anillo mágico correspondiente, atributos complementarios y calidad. Los empates conservan las piezas equipadas y usan el identificador del objeto para ser reproducibles.

La búsqueda evalúa combinaciones completas de seis ranuras, incluyendo bonos de conjunto y límites reales. No cuenta atributos inactivos del anillo mágico. Vida prioriza potencia de curación; Escudo, capacidad; el resto, el producto de potencia general y elemental. Repetir un mismo afijo no aumenta su puntuación. Marca y Eco pueden equiparse juntos en piezas diferentes.

Están disponibles los diecisiete afijos, incluidos los económicos del anillo de oro. La prioridad distingue el 10 % del 20 % de oro sin confundir el porcentaje con un rango de combate. Si dos prioridades requieren la misma ranura, prevalece la indicada primero.

Activar equipa la combinación elegida y vende los descartes, incluidas piezas antes equipadas. Cada recogida vuelve a comparar el nuevo objeto con el equipo y los favoritos disponibles. Los favoritos no seleccionados se conservan; si impiden guardar una mejora sin exceder las 24 piezas, se vende el objeto nuevo. El oro temporal, las mejoras y el recuento de ventas se actualizan una sola vez. Mientras esté activa, sustituye los filtros de venta por calidad y bloquea cambios manuales contradictorios. El control manual vuelve al desactivarla.

La preferencia se recuerda por cuenta o invitado en el dispositivo; el servidor conserva y valida la configuración durante la partida, reconexión, guardado y reinicio. Un guardado anterior comienza con la función desactivada. Los snapshots privados ocultan las prioridades de otros jugadores. Los selectores rechazan duplicados con un mensaje persistente y las operaciones tienen plazo de confirmación.

## Cambios pendientes incluidos

- Marca arcana y Eco arcano, con sus ventanas de daño, objetivos, retratos y continuidad de guardado.
- Fragmentación de roca: cuatro fragmentos en Diamante y seis en Mítico, conservando la mezcla y áreas elementales.
- Oráculo de asedio ocasional y reforzado; nuevas auras de Arcano y Agua para Supervivencia.
- Resumen personal de daño por elemento al morir y al terminar en todos los modos.
- Nombres enemigos ocultos en combate y retratos reales conservados en estadísticas y guía.
- Descripciones completas del estado Mojado y de atributos, sin las abreviaturas que ocultaban su significado.

## Validación de la publicación

Siete nuevas regresiones del equipo predeterminado cubren ocho elementos, conjuntos completos, anillos, prioridad y coexistencia de afijos, ventas, favoritos, inventario lleno, desactivación, protocolo, visibilidad privada, guardados actuales y anteriores y datos inválidos. La navegación Godot comprueba las siete pestañas, duplicados, activación, venta y desactivación en 640×450, 960×450 y 1440×900.

El publicador exige compilación y pruebas del motor y red, cuentas con emulador, Docker amd64/arm64, verificación Godot y paquetes Windows/Android antes de crear el tag. Se genera APK con firma permanente y manifiestos SHA-256, además de los paquetes de escritorio y parches del mismo canal. Los resultados y enlaces concretos se comprueban una vez terminada la ejecución.

### Primera ejecución (r617)

C# compila; las regresiones de Marca, Eco, fragmentación, auras y daño personal pasan. Las tres comprobaciones puras del selector también pasan. Cuatro regresiones de integración nuevas no llegaron al caso probado porque su fixture omitía el mapa Bastión; se explicita el mapa requerido. Las capturas móviles detectaron que los nombres completos de los atributos imponían anchuras excesivas en la ayuda; sus títulos y el encabezado del ejemplo ahora admiten varias líneas sin acortar el texto. Cuentas, Docker en ambas arquitecturas y paquete Windows pasan. La publicación queda bloqueada hasta repetir y completar todos los controles.

### Segunda ejecución (r618)

Motor y red, cuentas, Docker y ambos paquetes pasan. La navegación verifica la configuración predeterminada en las tres resoluciones, tanto con interfaz de escritorio como móvil. La revisión de capturas detecta ejes comprimidos y solapamiento de leyendas con retratos en las gráficas móviles: se amplía la altura en los 82 píxeles que ocupan los nuevos retratos y se separan del texto de las barras. Se añade una comprobación de espacio mínimo para los ejes. La prueba de guardado termina correctamente pero Godot falla al cerrar con referencias C# aún pendientes; se libera la escena y se espera la recolección con el motor activo, igual que en la prueba de botín existente, antes de declarar éxito y cerrar. Se requiere otra ejecución completa para publicar.

### Comprobaciones posteriores

La limpieza del guardado y las capturas pasan después del ajuste. La comprobación del desglose aún buscaba la etiqueta antigua «Ataque», aunque la interfaz ya muestra «Potencia de ataque». Se utiliza el nombre completo del catálogo para localizar el atributo; se mantienen las mismas exigencias sobre las cuatro aportaciones, el signo negativo, la suma y la exclusión de piezas sin equipar. La comprobación de atributos cancelados también utiliza los nombres actuales completos.
