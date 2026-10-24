# Cambios

## 0.29.15 — Inventario visual del mago

- Dos paneles permanentes: el mago a la izquierda y los objetos de la zona seleccionada a la derecha, en inventario de cuenta y Supervivencia.
- Seis zonas resaltadas: gorro, pechera, botas, arma y un anillo en cada mano. Selección por tacto, ratón y teclado, sin filtros.
- Estadísticas totales desde el panel derecho, ordenadas de mayor a menor. Los detalles por pieza y los límites se conservan en la información de cada valor.
- Solo se bloquea la venta de lo equipado actualmente. Cambiar de pieza permite vender la anterior sin salir; los favoritos siguen marcados y también pueden venderse.
- La venta confirma primero la sustitución cuando es necesaria. Un fallo al guardar no elimina el objeto ni paga oro; se conservan los controles de propiedad y los reintentos.

## 0.29.14 — Dos anillos de equipo

- Dos posiciones nuevas: anillo de oro y anillo mágico, disponibles como botín y en la tienda en PvP y Supervivencia sin sustituir botas, gorro, pecho o arma. Sus efectos básicos existen en todas las calidades.
- Anillo de oro: una moneda extra cada 11 de base en nivel 0; cada mejora reduce el intervalo hasta una extra por moneda en nivel 10. En Supervivencia actúa al recoger y en PvP sobre el premio final.
- Anillo mágico: renueva su escudo cada 16–8 s según nivel, incluso después de romperse, sin consumir elementos ni interrumpir lanzamientos. Niveles 0–5: escudo y elemento; 6–10: elemento duplicado. Nueve acciones: repulsión, pulso sanador, amparo compartido, brasas, salpicadura, escarcha, descarga selectiva, pulso arcano y sacudida de piedra.
- Los anillos admiten diez mejoras: oro temporal en Supervivencia y oro permanente de cuenta en PvP. Las mejoras de cuenta son transacciones idempotentes; el nivel y los atributos persisten.
- Inventario, equipos guardados, fichas públicas, límites, venta protegida y estadísticas incluyen los anillos. Sus aportaciones se suman al equipo, pero no aumentan los conjuntos de cuatro piezas. Se conservan nivel y recarga al guardar, continuar o reconectar.
- Catálogo 5 y protocolo 33; actualizar cliente y servidor. Las reglas Firestore admiten las dos posiciones opcionales, conservan las selecciones antiguas y validan propiedad y tipo de cada pieza.

## 0.29.13 — Oro extra según el nivel de la pieza

- El afijo de monedas da 1 de oro extra cada 11 monedas base en nivel 0. Cada mejora reduce el intervalo en una moneda: cada 6 en nivel 5 y una extra por moneda en nivel 10.
- El bono depende de la mejora de esa pieza, no del nivel del personaje. Si hay varias piezas con el efecto se usa la mejor, y se puede mejorar hasta nivel 10 aunque sus atributos ya estén al máximo.
- El inventario, la comparación de equipo y las fichas de otros magos muestran el nivel y el intervalo actuales. Los porcentajes de oro del otro afijo mantienen sus valores del 10 % y 20 %.
- Guardar y continuar conserva las fracciones exactas; mejorar, cambiar o quitar equipo no pierde el progreso ni genera pagos retroactivos. Los puntos antiguos y premios PvP pendientes siguen siendo compatibles.
- En Supervivencia actúa al recoger oro; en PvP, sobre el premio final. Las ventas siguen excluidas. Cliente y servidor deben actualizarse juntos al protocolo 32.

## 0.29.12 — Inventario protegido, equipo de oro y estadísticas claras

- Las piezas equipadas aparecen siempre primero, incluso al filtrar u ordenar por atributo, y no se pueden vender de forma individual, rápida o múltiple en ningún modo.
- Nuevos efectos de equipo Diamante/Mítico: una moneda extra por cada dos de oro base y +10 %/+20 % de oro. En Supervivencia se aplican durante la partida y conservan los restos al guardar.
- El PvP válido entrega 50 oro por victoria y 25 por derrota al acabar la serie, con los bonos del equipo y pagos idempotentes. Los mismos efectos no se acumulan; las ventas no generan bonos.
- El daño de cada elemento se desglosa en ataques directos, áreas, auras y quemaduras, respetando las mezclas. Los informes anteriores conservan su total como «Sin desglose».
- Gráficas con escala automática, lineal o logarítmica, cifras numéricas y porcentajes del total mostrado. Las barras pequeñas siguen siendo legibles; daño enemigo realizado y recibido mantienen gráficas separadas.

## 0.29.11 — Descargas reanudables y limpieza de actualizaciones

- Puedes pausar o cerrar el juego durante una actualización y continuar después con los bytes conservados. El aviso recupera el progreso y muestra «Continuar descarga».
- La continuación valida la versión, compilación, tamaño y SHA-256. Si el servidor no admite continuar o el parcial está alterado, se descarga el paquete completo sin instalar archivos incompletos.
- Android elimina la APK descargada solo tras confirmar una instalación correcta. El arranque también limpia paquetes, parches, parciales y metadatos de versiones ya instaladas, incluidos los restos antiguos.
- Solo se conserva la descarga pendiente del destino seleccionado y su alternativa completa; las partidas y los datos de usuario se mantienen.
- Conservar el progreso no mantiene una descarga activa cuando Android cierra el proceso. Al abrir de nuevo el juego puedes continuarla desde el aviso.

## 0.29.10 — Estadísticas exclusivamente en Firebase

- Las partidas envían sus estadísticas directamente a Firestore al terminar o abandonar, sin crear un historial ni una cola de archivos locales.
- Las dos cuentas autorizadas consultan las gráficas globales de Firebase desde Perfil, con filtros, paginación y revisiones agrupadas por partida.
- Los envíos fallidos se reintentan cada 30 segundos únicamente en memoria mientras la aplicación está abierta; cerrar el proceso pierde los pendientes sin confirmar.
- El archivo de supervivencia guarda solo el estado de la partida y omite el informe estadístico y sus métricas. Cada continuación abre un tramo nuevo para no duplicar lo ya jugado.
- Los puntos antiguos que contienen estadísticas siguen abriendo y al guardar de nuevo se omite su telemetría. Se verifica guardar, cargar y volver a guardar sin informes locales.

## 0.29.9 — Estadísticas al cierre y gráficas globales

- Las estadísticas se envían directamente a Firestore al terminar o abandonar una partida. Durante el combate solo se acumulan en memoria; guardar un punto de supervivencia sin salir no dispara un envío.
- Se elimina el historial y la cola de archivos JSON tanto del cliente como del servicio de compatibilidad del servidor. Los errores conservan el informe únicamente en memoria y se reintentan cada 30 segundos mientras la aplicación sigue abierta; al cerrarla se pierden los pendientes no confirmados.
- Las dos cuentas autorizadas abren desde Perfil las gráficas de los informes de Firebase de todos los dispositivos. La consulta comprueba la sesión y el permiso, carga por páginas y permite actualizar o ampliar la muestra hasta 1000 documentos sin duplicar revisiones de una partida.
- Se mantienen los filtros, la suma de humanos y bots y las dos escalas separadas de daño enemigo. Un error de lectura conserva la muestra ya cargada; cambiar de cuenta o cerrar sesión cierra el visor.
- El punto local de supervivencia permite continuar el estado del juego. Cada continuación abre un tramo estadístico nuevo para evitar contar de nuevo el acumulado anterior. No se crean ni consultan archivos de estadísticas; los archivos de versiones anteriores no se importan automáticamente.
- Pruebas de reintentos en memoria, cierre de partida, mutación concurrente de contadores, envío REST real al emulador, paginación y gráficos en móvil y escritorio.

## 0.29.8 — Estadísticas directas a Firestore

- Android y Windows envían los informes directamente a Firestore por HTTPS, sin descubrir ni contactar el servidor de cuentas o partidas y sin enviar tokens, cookies ni identificadores de cuenta.
- El historial local se actualiza durante el juego. Los informes terminados, abandonados, guardados o interrumpidos entran en el envío; los errores de conexión, permisos o cuota conservan la cola para reintentar.
- Cada revisión cerrada tiene una clave estable por partida, revisión y estado. Los reintentos no crean copias, una revisión tardía no sobrescribe el final y las lecturas deben agrupar por partida antes de sumar estadísticas.
- Se preparan reglas de escritura anónima con campos y tamaños acotados. Los informes existentes son inmutables y la lectura conserva sus permisos. Publicar estas reglas en Firebase es necesario para habilitar el envío real.
- Las pruebas ejercitan el transporte REST real contra el emulador, envíos concurrentes, informes antiguos, permisos, cuota, anonimato y documentos malformados. El visor sigue leyendo los archivos locales.

## 0.29.7 — Integración estable y reliquias al 1 %

- Se integran en main todas las mejoras de la rama de pruebas: partículas, rayos y muros agrupados, buffers reutilizables y actualizaciones visuales solo cuando cambia su estado.
- Estadísticas incorpora las barras por elemento y dos gráficas independientes de daño realizado y recibido por monstruo. Humanos y bots se suman por defecto, con los filtros y tablas numéricas disponibles.
- Continuar supervivencia consume el punto local al activar la partida. Guardar otra vez habilita una nueva continuación; cancelar la carga conserva el punto.
- Las modales comparten marco, colores, botones y sombreado con el resto de pantallas.
- La aparición de reliquias en supervivencia sube del 0,5 % al 1 % por baja y defensor. Se comprueban las tiradas inmediatamente antes y después del umbral.

## 0.29.6 — Daño enemigo en gráficas separadas

- Monstruos separa el daño realizado y el recibido en dos gráficas verticales, cada una con escala propia desde cero. El daño recibido elevado deja de aplanar las diferencias de daño realizado.
- Se conservan los mismos enemigos, orden, valores acumulados, escudos y desplazamiento horizontal. Realizado utiliza rojo; recibido, azul.
- La verificación comprueba escalas independientes con daño recibido miles de veces superior, categorías sin actividad y capturas de ambas gráficas en móvil y escritorio.

## 0.29.5 — Barras de balance, continuación única y modales coherentes

- Elementos incorpora barras verticales para los ocho elementos: daño real acumulado, curación efectiva para Vida y absorción para Escudo. Monstruos compara el daño realizado y recibido por especie con dos barras y una escala común desde cero. Las categorías sin actividad se conservan y las gráficas permiten desplazamiento horizontal.
- Humanos y bots se suman por defecto en Elementos y Hechizos, y en un único registro por clase y equipo. Los filtros individuales y los informes existentes se mantienen, sin duplicar los totales ni modificar Firebase.
- Continuar una supervivencia consume su punto local y su respaldo al activar el juego. Cancelar la carga lo conserva. Guardar otra vez crea un punto nuevo, también recuperable si se daña el archivo principal.
- Las ventanas de venta, comparación, conjuntos y reliquias comparten marco azul, títulos dorados, botones y sombreado. También se unifican pausa, ayuda, inventario, tienda de supervivencia, salas, inspección y aviso de actualización, incluidos menús desplegables.
- La prueba larga de supervivencia libera sus escenas y finaliza los recursos C# antes de cerrar Godot; los errores de limpieza siguen provocando fallo de la verificación.
- Se documenta cómo se agrupan partículas, rayos y muros, cómo se reutilizan los buffers y qué miden las comparaciones de rendimiento. Trabajo en `codex/visual-performance`; la versión estable continúa en 0.29.2.

## 0.29.4 — Segunda optimización visual en pruebas

- Los cristales, postes y runas de cada muro se agrupan por material y padre animado. Conservan todos sus vértices, normales, UV, colores, sombras y animación; las coronas de hielo y las crestas mantienen sus materiales y su desvanecimiento. Cada muro conserva su propia visibilidad.
- Los núcleos, filamentos y pulsos de los canales comparten mallas MultiMesh. Sus radios y transparencia viajan por instancia, sin crear materiales nuevos mientras se carga el hechizo. Se conservan los extremos suavizados y el límite global de 512 tramos.
- Los lotes de partículas y rayos empiezan con una reserva pequeña, crecen por duplicación cuando hace falta y liberan el exceso al cambiar de escena. Se evitan subidas de cientos de matrices vacías en escenas con pocos efectos, manteniendo los mismos límites.
- Las barras de vida y escudo, tintes, orientación y auras de los monstruos solo actualizan propiedades del motor cuando cambian. Los muros también reutilizan su estado visual; sus superficies y crestas siguen animándose cada fotograma.
- Las pruebas cubren daño, congelación, quemadura, reparación y reutilización de enemigos, así como desvanecimiento y recuperación de los materiales de los muros. Los buffers de los canales se leen con un viewport real y se comparan capturas y mediciones con 0.29.3.
- Se mantiene la rama `codex/visual-performance` y el canal de pruebas; `main` continúa en 0.29.2.

## 0.29.3 — Pruebas de rendimiento visual

- Las partículas con la misma forma y material se dibujan en lotes MultiMesh, con una sola subida de transformaciones por lote. Conservan colores, sombreado de las facetas, cantidad, trayectorias, duración y presupuestos; los enlaces eléctricos y las ondas mantienen su representación específica.
- Los segmentos sencillos de relámpagos, conos y empujes comparten geometría y se agrupan por material. Conservan sus extremos y radios. Los cilindros de los canales eliminan subdivisiones longitudinales innecesarias; los núcleos sin animación dejan de actualizar un reloj de shader que no utilizan.
- El perfilador separa el coste de crear/simular la escena del coste de actualizar el renderizador. Las pruebas comprueban las transformaciones y los datos reales del buffer, extremos, radios, colores, caducidad, limpieza y reutilización.
- Trabajo aislado en `codex/visual-performance`, distribuido por el canal de pruebas. La versión estable de `main` permanece en 0.29.2.

## 0.29.2 — Acceso local a estadísticas y escudos enemigos legibles

- El botón Estadísticas de partidas aparece en Perfil para los dos UID indicados, incluido su perfil guardado sin conexión. Abre las gráficas de los informes JSON del dispositivo y permite actualizar su lectura. Ya no consulta permisos ni partidas en Firestore para abrir este visor.
- Una barra azul fina sobre la vida muestra el escudo de los monstruos y su integridad. Se oculta al agotarse y reaparece con las reparaciones. Los magos enemigos usan la misma señal compacta, sin cifras de escudo, burbujas protectoras ni anillos adicionales.
- Las barras miran a la cámara y los nodos se reutilizan. Se comprueban ambos UID, otras cuentas, cierre de sesión, lectura local y transiciones de escudo completo, dañado, agotado y reparado, también al reutilizar enemigos. No se modifica la configuración de Firebase.
- La simulación de combinaciones libera sus escenas y finaliza los recursos C# antes de cerrar Godot; los errores de limpieza siguen provocando fallo de la prueba.

## 0.29.1 — Gráficas de balance

- Las seis vistas incorporan gráficas con datos de los informes filtrados: daño por origen y elemento, amenaza media por aparición de cada monstruo, ataques con más daño y más usos, vida inicial por dificultad, objetos recogidos y comparación de clases/equipos.
- Las barras distinguen daño a vida y a escudos; la curva de vida pondera las apariciones de cada etapa y permite consultar sus valores. El porcentaje congelado usa el tiempo vivo de cada clase. Se conservan las tablas numéricas.
- Verificación de filtros de modo y origen, medias ponderadas y límites de gráficas en escritorio y móvil. El acceso sigue requiriendo los permisos de las dos cuentas Google en Firestore; no se han configurado cuentas sin sus identificadores reales ni credenciales Admin.

## 0.29.0 — Informes anónimos de partidas y visor de balance

- Todas las rondas locales/IA y PvP guardan informes JSON locales: daño real por elemento, combinación y ataque, escudos, monstruos, bajas, curación, estados, economía, progresión y rendimiento. Los grupos de humanos, bots y monstruos se distinguen sin identificar jugadores.
- Cola persistente con reintentos al relé oficial y Firebase, revisiones idempotentes y protección frente a duplicados. Una confirmación antigua no elimina una revisión nueva. Guardar/continuar supervivencia conserva el acumulado; rebobinar o cambiar de versión abre otro tramo.
- Visor en Perfil con seis vistas y filtros por modo, versión, periodo y partidas incompletas. Acceso restringido mediante permisos Firestore para las dos cuentas Google autorizadas. La activación requiere sus correos/UID reales y desplegar las reglas con credenciales Admin; hasta entonces el acceso permanece cerrado.
- Contrato sin nombres, correos, UID, IP, dispositivos ni horas exactas; envío sin tokens ni cookies. Los informes tienen límites de tamaño y rechazan campos extra. Las herramientas de QA no envían telemetría de producción.
- Protocolo 30: actualizar cliente y servidor juntos. Pruebas de motor, red, transacciones, reglas y visor móvil/escritorio. Detalles y puesta en marcha en `docs/ESTADISTICAS-PARTIDAS.md`.

## 0.28.0 — Prisma elemental, oráculo de asedio e IA de supervivencia

- Nuevo prisma elemental desde dificultad 3. Su color y rótulo muestran el único elemento que lo daña: Fuego, Arcano, Frío, Agua, Rayo o Tierra. Las mezclas aportan solo ese componente; Vida y Escudo nunca son puntos débiles. La variante de Agua recibe daño del agua pura. La inmunidad incluye estados, poderes automáticos y daño periódico.
- Nuevo oráculo de asedio desde dificultad 6. Mantiene distancia, se retira por debajo de 16 m y anuncia tres cristales explosivos desde hasta 24 m. Su aura de 14 m concede 220 × fuerza de escudo y repara hasta 110 × fuerza cada 4 s, sin acumular frecuencias entre oráculos ni sobrescribir guardas propias. Derrotarlo cancela los bombardeos pendientes.
- Los prismas mantienen una víctima hasta diez segundos y después rotan entre humanos y bots próximos; abandonan objetivos muertos o demasiado alejados. El coloso persigue al defensor más cercano, se mueve un 37,5 % más rápido y reduce su preparación de 1,6 a 1,05 s y su pausa de ataque un 35 %.
- Los aliados buscan puestos al menos a 3,5 m de los humanos y se apartan al coincidir. Piromantes, criomantes, tempestarios y geomantes priorizan su afinidad, con repertorios adecuados al alcance. Contra un prisma usan su punto débil; los atacantes eléctricos se secan antes de disparar.
- Las reliquias bajan del 2 % al 0,5 % por baja y defensor, con una tirada que conserva la precisión decimal. El equipo, oro y auras de los bots mantienen su funcionamiento previo; su reparto de equipo compartido se analiza por separado.
- La generación empieza con un presupuesto de 48 enemigos y mide ventanas activas de 2 s. Reduce inmediatamente al caer por debajo del margen de 30 FPS y recupera una banda tras cinco ventanas estables. Los refuerzos llegan en paquetes menores con presupuestos bajos, las apariciones tienen un intervalo mínimo y se limita la presencia natural a dos oráculos y ocho prismas.
- Modelos compartidos entre combate y bestiario: guardián con casco, espada y escudo; coloso con cabeza, cuernos y grandes puños; prisma de seis colores y oráculo con órbitas. Se precargan sus representaciones, escudos y bombardeos. El catálogo pasa a quince enemigos con imágenes, estadísticas y contadores.
- Protocolo 29: cliente y servidor deben actualizarse juntos. Los puntos débiles, escudos y fijaciones viajan también en las deltas y se conservan al guardar localmente. El formato de guardado sigue admitiendo las versiones anteriores.

## 0.27.3 — Amenaza de la horda y recuperación tras congelarse

- Los ataques de supervivencia ganan daño: el minion básico pasa de 20 a 48, el corredor de 14 a 32 y la flecha de 18 a 40. Los golpes de área de brutos, detonadores, guardianes, comandantes y colosos castigan permanecer dentro del aviso; se conservan sus radios y tiempos de preparación.
- Los enemigos se acercan más rápido desde el comienzo. La vida y el daño crecen un 18 % en las primeras cuatro subidas de dificultad y un 22 % en las siguientes, hasta el mismo límite de ×1000. La vida inicial y el escalado por número de defensores se conservan.
- La potencia de los magos pasa de 0,22 a 0,42. Sostienen haces y relámpagos durante 1,2 s y conos durante 0,9 s, para que sus ataques alcancen una potencia significativa.
- El frío no renueva una congelación activa sobre los defensores. Tras descongelarse disponen de tres segundos sin acumulación de frío ni nuevas congelaciones; los impactos siguen causando daño. Se aplica también a los aliados IA y el fuego permite descongelar antes. El comportamiento del hielo en PvP se conserva.
- El bestiario refleja las nuevas cifras y explica la recuperación. Esta se conserva al guardar localmente, continuar o reconectar; los guardados anteriores siguen siendo compatibles.
- Al volver a una supervivencia online, la fase de carga respeta la invulnerabilidad y recuperación que tenía el personaje. Se evita que la protección de entrada de un jugador nuevo sobrescriba un estado restaurado.
- Se añaden regresiones de presión frente a escudos regeneradores, daño real del haz, varios magos helados, guardado y PvP. La simulación de equilibrio compara desplazamiento por el anillo con permanecer inmóvil.

## 0.27.2 — Estadísticas de los monstruos en el bestiario

- Las trece fichas incluyen tablas con vida máxima, daño, velocidad, distancia para atacar y pausa entre ataques. Los enemigos especiales muestran también radio y preparación de sus áreas, curación, escudo, refuerzos y debilidad elemental.
- El bestiario permite elegir dificultad y número de defensores, incluidos los bots, para consultar el escalado real. Las cifras se actualizan sin reconstruir los retratos ni modificar los contadores de bajas.
- Los magos desglosan sus hechizos con daño por segundo, por impacto o por fragmento. Se muestran la carga de los haces, la duración de los canales y las quemaduras adicionales. Las recetas y los parámetros se comparten con la IA, conservando el equilibrio actual.
- Se comprueba que las cifras coinciden con impactos, curaciones, escudos y atributos de enemigos reales, además de validar las tablas y selectores en escritorio y móvil.

## 0.27.1 — Continuar supervivencia y retratos del bestiario

- El menú de supervivencia local permite guardar la partida o guardar y salir. El launcher ofrece continuarla después de cerrar el juego, con el mismo mago, aliados IA, vidas, salud, equipo, afijos, mejoras, reliquias, enemigos, botín y relojes de dificultad y jefes.
- El guardado permanece en el dispositivo, con escritura atómica y una copia de respaldo recuperable. No se sube a Firestore ni transforma una sala online en una partida local. Reanudar conserva el identificador y los acumulados del bestiario para evitar bajas duplicadas.
- Las trece fichas del bestiario incluyen una imagen con el modelo, los colores y los sprites utilizados durante el combate. Los retratos se renderizan una sola vez para reducir su coste.
- Los conos recuperan su efecto visual anterior, sin el borde y relleno que marcaban toda la zona de alcance. Se mantienen las diferencias entre una, dos y tres cargas.
- Se añaden pruebas de guardado y recuperación tras un reinicio, copia dañada, continuidad del combate, aliados, afijos y contadores, además de verificaciones de los botones y retratos en escritorio y móvil.

## 0.27.0 — Bestiario, reliquias y efectos especiales del equipo

- El perfil incorpora un bestiario de los trece enemigos de supervivencia, con sus habilidades y las bajas causadas por cada jugador. Los contadores se guardan en Firestore mediante registros por partida que evitan duplicados al reintentar. Las partidas locales con una cuenta guardada conservan sus datos pendientes hasta recuperar el servidor de cuentas.
- Las reliquias recogidas se consultan desde el menú de partida y desde su pestaña en la tienda de supervivencia. El contador deja libre el lateral del mapa y cada reliquia incluye la descripción de su efecto.
- Los nuevos objetos Diamante y Míticos incorporan un efecto especial: penetración de haces de hasta tres enemigos adicionales, hasta dos saltos eléctricos adicionales, doble daño periódico de quemadura o un charco de agua al crear un escudo de agua. El charco dura cinco segundos, tiene tres metros de radio y moja a quienes entran, incluido el lanzador.
- Las piezas, la tienda, las comparaciones y la inspección de otros magos muestran estos efectos. Al combinar varias piezas con el mismo efecto se aplica el valor más alto.
- Se integran los nuevos enemigos de supervivencia, los hechizos variados de la IA, los grandes refuerzos del comandante, el crecimiento moderado del escudo por nivel y los límites de enemigos según rendimiento.
- Se integran los escudos y runas de estilo B. Los conos distinguen visualmente una, dos y tres cargas tanto en escritorio como en móvil, con un máximo de seis filamentos en escritorio y tres en móvil.
- La sincronización del bestiario intercambia solo sus contadores para reducir consultas y tráfico.

## 0.25.4 — PvP adaptable y correcciones de sincronización

- Los campos de creación de salas agrupan etiqueta y control en una misma columna flexible. La rejilla pasa de una a tres columnas según el espacio disponible, sin estrechar los textos a una letra por línea. Las acciones se compactan en pantallas pequeñas para dejar espacio útil al formulario.
- La sala de espera PvP ocupa el ancho disponible, adapta equipos y acciones y permite desplazar el contenido sin perder el botón de salida. Los nombres y avisos conservan un ancho legible. Los controles táctiles se ocultan durante la espera y vuelven al iniciar el combate, sin acumular gestos detrás de la modal.
- El selector de objetos comparte el área central del inventario: la cabecera y las ocho opciones inferiores permanecen visibles y pulsables. Tarjetas, filtros y acciones se desplazan juntos; la rejilla admite una columna cuando es necesario.
- Cerrar Google invalida las respuestas pendientes, tanto en el plugin Android como al recibir una sesión en el cliente. Un callback anterior ya no vuelve a iniciar sesión.
- Las lecturas de inventario y tienda iniciadas antes de una compra, venta o cambio de equipación confirmado se descartan. Guardar equipación conserva el oro y objetos de operaciones simultáneas.
- Los controles táctiles reconcilian los elementos con la secuencia de acciones procesadas por el servidor, conservando las pulsaciones pendientes. Un lanzamiento rechazado ya no deja una combinación distinta en el joystick.
- La configuración OAuth de escritorio se valida con un único lector que comprueba los tipos JSON y el proyecto. Los archivos corruptos se rechazan sin cerrar el juego y la configuración importada tiene prioridad al reiniciar.
- Atrás de Android y Escape cierran Mi perfil y conservan la pantalla de origen. Defensa arranca con cuatro plazas por defecto, admite una plaza y limita la configuración a cuatro defensores, también desde Docker.
- Se retiran la barra móvil antigua, las cabeceras ocultas y validaciones duplicadas sin uso. Se actualiza documentación obsoleta de versiones, servidor y pruebas.
- Se añaden regresiones de concurrencia, OAuth, selección táctil y configuración del servidor, además de comprobaciones gráficas y pulsaciones reales de navegación en 640×450, 720×540, 1024×640 y 1440×900.

## 0.25.3 — Navegación coherente y ajustes en Mi perfil

- La tienda utiliza la misma cabecera y navegación inferior que Inicio, Perfil, Inventario y Ayuda tanto en móvil como en escritorio. Se eliminan los accesos duplicados a Inicio e Inventario de su barra de acciones.
- El catálogo y sus acciones se desplazan dentro del área central, conservando las ocho opciones del menú siempre visibles. La distribución de ofertas se adapta al ancho disponible; compra, comparación, reconexión y mensajes sin servidor mantienen su funcionamiento.
- «Configurar joystick y botones» pasa a Mi perfil, disponible sin sesión Google ni conexión con Firestore. Se retiran los accesos del menú y la ayuda de la partida; la ayuda del menú indica su nueva ubicación.
- Los ajustes táctiles comparten la navegación del resto de menús. Guardar o cancelar vuelve al perfil; cambiar de sección descarta los cambios sin guardar. El editor de posiciones conserva el lienzo completo y los márgenes de ocho píxeles.
- Perfil y ajustes permiten desplazamiento en pantallas pequeñas sin tapar las barras comunes. Las verificaciones comprueban las ocho opciones inferiores, los límites del contenido, compras y apertura/guardado/cancelación de controles desde Mi perfil.
- En 640×450 la navegación utiliza cuatro columnas y dos filas. Los formularios de IA y creación de salas se ajustan al ancho; Ayuda usa su selector de temas compacto y el inventario conserva espacio para desplazarse.

## 0.25.2 — Controles y minimapa sin superposiciones

- Margen mínimo de ocho píxeles de interfaz entre botones, joysticks, minimapa y zonas de información, además del borde de pantalla. Al arrastrar un control sobre otro elemento se ajusta al hueco libre más cercano sin desplazar el resto.
- Las posiciones guardadas se corrigen al cargar; cambiar tamaño, separación, modo zurdo o resolución vuelve a comprobar el espacio. Los paneles se escalan completos para que el área pulsable coincida con lo que se dibuja.
- El minimapa busca una posición libre y mantiene su zona protegida en el editor. La vista de edición muestra el HUD y reserva también vida, conexión/FPS, selección, avisos y los laterales de cada modo.
- El joystick flotante conserva el origen del gesto y coloca su dibujo en un hueco libre. Sus rótulos quedan dentro de la zona reservada y tocar un panel informativo no inicia movimiento.
- Vida y clasificación se adaptan a pantallas estrechas. El Laboratorio conserva sus controles en una columna con desplazamiento, sin que el selector de hechizos ensanche el panel; avisos y bonificaciones tienen una altura limitada.
- Pruebas de geometría y entradas en los siete mapas, tamaños extremos, modo zurdo, posiciones apiladas, arrastres sobre elementos protegidos y pantallas de 640×450 a 1440×720. La distribución se recalcula al cambiar la geometría, no en cada dibujo.

## 0.25.1 — Correcciones de controles personalizados y ventas

- Los joysticks de movimiento y lanzamiento responden en su posición personalizada, aunque estén en la mitad contraria de la pantalla. Se respeta el modo zurdo y los dedos conservan su función al mover y apuntar simultáneamente.
- Un segundo dedo en un joystick ocupado ya no activa el otro control. Soltar movimiento y soltar lanzamiento siguen siendo acciones independientes.
- Cuando se pierde la respuesta de una venta o llega un error de servidor, se sincroniza el inventario para recuperar los objetos vendidos y el oro real antes de permitir un reintento. Si tampoco hay conexión para leerlo, se muestra «Venta sin confirmar» y se bloquean nuevas ventas hasta actualizar. Una lectura antigua o una copia local no elimina ese bloqueo.
- La venta múltiple muestra el progreso y el oro confirmado. La recuperación no vuelve a enviar las ventas ya aplicadas; el bloqueo no se transmite a otra cuenta al cambiar de sesión.
- Pruebas de pérdida de respuesta tras confirmar la venta, errores 503, lectura sin conexión, respuestas antiguas, recuperación manual, cambio de cuenta y controles personalizados con varios dedos.

## 0.25.0 — Inventario y controles configurables

- Comparación del resultado real del equipo completo al sustituir una pieza: valores antes y después, diferencias, límites aplicados y activación o pérdida de conjuntos. Disponible en el selector y en «Comparar con mi equipo» de cada oferta de tienda.
- Favoritos protegidos frente a venta individual, doble clic/toque y selección múltiple. Preferencias permanentes separadas por cuenta y guardadas en el dispositivo; los favoritos de Defensa se mantienen durante la sesión de partida.
- Venta múltiple con selección entre posiciones, selección de piezas visibles sin equipar, suma de oro y una sola confirmación. Cada venta se confirma en el servidor; una interrupción conserva las piezas pendientes y permite reintentarlas sin repetir las ya confirmadas. También funciona con el inventario temporal de Defensa.
- Motivos visibles de acciones bloqueadas: falta de oro, inventario lleno, desconexión, favoritos, mago caído y límites de mejora. Las magias táctiles indican si falta energía o el mago está congelado.
- Orden descendente por cualquiera de los 22 atributos, con calidad como desempate y piezas sin ese atributo al final. Se mantienen los filtros de elemento, calidad y atributo.
- Hasta ocho equipos permanentes con nombre por cuenta en este dispositivo. Aplicar restaura las cuatro posiciones y conserva el guardado al salir; un equipo con piezas vendidas muestra el motivo y no se aplica parcialmente.
- Controles táctiles desde Ayuda → Controles y desde el menú de partida: tamaño, separación, zona muerta, sensibilidad del alcance, modo zurdo y editor para arrastrar cada botón y los dos joysticks. Guardado local y cancelación; los marcadores de agua, hielo y rocas conservan su distancia fija.
- Verificación de lógica, recorridos nativos en escritorio y móvil, recuperación tras fallo parcial de venta, protección de favoritos, persistencia, equipos, editor táctil y regresiones de doble toque, tienda y Supervivencia.

## 0.24.3 — Doble clic y doble toque en objetos

- Doble clic en la papelera para vender directamente la pieza y recibir su oro, sin abrir la confirmación. En móvil funciona con doble toque; un clic o toque conserva la modal de venta.
- El botón de mejora conserva la mejora inmediata con un clic. Un doble clic o toque aplica una sola mejora y cobra una vez, respetando el oro disponible, el máximo de diez mejoras y los límites de atributos.
- Los gestos identifican la pieza aunque se reconstruyan las tarjetas y evitan que el segundo toque pulse otra acción si aparece un mensaje de estado. Salir del selector, cambiar de posición, filtrar o cambiar de cuenta cancela la venta pendiente.
- Verificación con eventos reales de ratón y tacto: clic simple, venta directa única, selección pendiente conservada, salida del selector, mejora única y límites. Se mantienen las verificaciones de inventario, tienda, mapas y Supervivencia.

## 0.24.2 — Confirmación de venta y correcciones de inventario

- Nueva modal de venta con tarjeta por rareza, valores de cada atributo, oro que se recibe y botones «Conservar objeto» y «Vender». Fondo atenuado y diseño adaptado a escritorio y móviles pequeños.
- El aviso solo indica que se desequipará la pieza cuando está equipada. Se distinguen el oro permanente de cuenta y el oro temporal de Supervivencia; cancelar conserva el selector y el objeto.
- Corregida la venta que permanecía desactivada si la conexión automática llegaba con el selector abierto.
- Corregida la confirmación de venta que quedaba abierta al cerrar sesión o cambiar de cuenta; la confirmación queda vinculada a la cuenta que la abrió.
- Guardar equipación directamente en Firebase conserva el oro y los objetos actualizados por una compra simultánea, en lugar de restaurar una copia antigua en la memoria del cliente.
- Regresiones de concurrencia y venta, doble confirmación, cancelación y cierre de sesión. Capturas y límites verificados a 1440×900, 720×540 y 640×450, además de la venta temporal de Supervivencia.

## 0.24.1 — Tienda en el menú principal y conexión automática

- Acceso directo a Tienda junto a Inventario en la navegación principal. La tienda permite consultar las seis ofertas personales y comprar por oro permanente sin modificar la equipación elegida.
- Eliminados el botón Servidor, la dirección editable y la configuración local del inventario. Las compras y ventas usan la dirección oficial publicada y validada automáticamente.
- La tienda mantiene su interfaz visible sin servidor o sin sesión, con seis espacios vacíos y un aviso. Retira ofertas y precios al perder la conexión o cerrar sesión y permite volver a consultar cuando regresa el servicio.
- Renovación por anuncios desactivada: botón «Renovar · próximamente», sin simulación y con los dos endpoints publicitarios bloqueados. Se mantienen las ofertas y el estado de las compras al cerrar y abrir.
- Verificaciones de navegación desde Inicio, compra única, inventario, saldo insuficiente, desconexión, reconexión, cierre de sesión y anuncios inactivos en móvil y escritorio.

## 0.24.0 — Tienda de cuenta, economía de mejoras y sesiones

- Tienda permanente en Inventario → Tienda, disponible con Google y servidor Firebase conectado. Seis ofertas personales con atributos y precios por rareza; se compra por oro de cuenta sin cambiar el equipo elegido.
- Catálogo, compras y recibos persistentes. La transacción cobra y entrega una sola vez, incluso con reintentos o compras simultáneas. Rechaza saldo insuficiente, inventario lleno y ofertas de otro catálogo o cuenta.
- Renovación gratuita mediante anuncio simulado de tres segundos, cancelable y claramente identificado. El servidor comprueba la espera y guarda una sola renovación por anuncio. No incorpora SDK publicitario ni pagos reales.
- Mejoras temporales de Anillos más caras según rareza y escalón: diez mejoras cuestan 17.600 / 26.400 / 39.600 / 57.200 / 88.000 de oro desde Plata hasta Mítico. Conservan el incremento relativo de diez puntos porcentuales desde la tirada inicial.
- Las respuestas tardías de inventario, equipación, ventas y tienda se descartan al cerrar sesión o cambiar de cuenta. Un cierre o 401 antiguo tampoco borra la sesión nueva; las operaciones aceptan las URL HTTPS del servidor configurado.
- Salir voluntariamente y volver a Anillos conserva vida, energía, estados, escudos, posición, reaparición y recargas. No concede invulnerabilidad adicional.
- Pruebas de lógica, HTTP, transacciones Firestore y tienda en móvil/escritorio. Protocolo de combate 27; la tienda y los costes nuevos requieren actualizar el servidor a 0.24.

## 0.23.0 — Áreas y pasivas con identidad visual

- Fuego con grietas y llamas, agua con ondas y gotas, hielo facetado con seis brazos. Las minas distinguen daño y curación y mantienen visibles sus dos radios al armarse.
- Auras de fuego, electricidad y regeneración con formas y ritmos propios. Se reconocen al superponerse y acompañan al mago con su radio real.
- Berserker deja marcas de furia, el pulso glacial expande un cristal y el meteorito pasivo termina en grietas incandescentes. Los relámpagos unen los objetivos realmente alcanzados.
- Mallas y materiales compartidos: una superficie por área y dos por mina, sin luces nuevas ni sombras decorativas. Se mantienen los límites de 320 partículas y 24 ondas y la reducción de detalle.
- Radios y presupuestos verificados; capturas en móvil y escritorio. Daño y recargas conservados. Los enlaces nuevos necesitan servidor 0.23; protocolo 27 compatible.

## 0.22.0 — Presión continua, equipo y ayuda directa

- Los marcadores de hielo y rocas permanecen a 7 m del mago tanto con ratón como con joystick, independientemente de la distancia del cursor.
- Supervivencia mantiene apariciones regulares de 0,32–0,48 s antes del escalado por dificultad y grupo. Prioriza los alrededores de los defensores; los enemigos a más de 60 m de todos pueden regresar fuera del radio seguro, conservando salud y estados. Se mantienen los límites de rendimiento, los avisos y las dos vidas.
- Nombres de atributos en la última columna de las tablas del inventario, inspección de magos y estadísticas de Supervivencia.
- Combinaciones integradas en Ayuda: selección directa y cifras al lado, sin buscador ni segunda ventana o escena 3D en el menú. La ayuda de combate conserva su demostración. Retirado el botón redundante del laboratorio; el mapa de práctica sigue en el selector.
- Cada pieza temporal admite diez incrementos de un punto porcentual sobre su tirada inicial: −6 % llega a +4 % y +10 % a +20 %. La cuenta y las tiradas del equipo permanente se conservan. Protocolo 27.

## 0.21.1 — Marcadores de apuntado para hielo y rocas

- Agua con frío y sus variantes muestran un círculo con cruz y una guía de dirección mientras se apunta. Las rocas también lo muestran durante la carga, incluidas las combinaciones elementales.
- En móvil el marcador sigue el joystick; en ordenador sigue el cursor. Se oculta al lanzar, cancelar el gesto, completar automáticamente la carga o abrir ayuda, pausa y menús.
- Traslación y Meteoro conservan sus marcadores de destino. La referencia de los proyectiles indica dirección, sin cambiar su alcance, daño ni tiempos de lanzamiento. Protocolo 27 compatible con 0.21.0.

## 0.21.0 — Red, hordas, reconexión e inventario

- Bases de red renovadas con confirmación y deltas independientes por identificador; inventario estable reutilizado, con los 20 estados por segundo y recuperación tras descartes.
- Vecindad espacial para separar minions con el mismo orden de cálculo; reutilización de botín y datos gráficos de magos.
- Reconexión automática durante 30 segundos con el mismo personaje, posición, vidas y equipo. La partida continúa: no concede curación, protección ni actividad artificial. Salir voluntariamente sigue siendo inmediato.
- Menos partículas decorativas ante caídas de rendimiento; el presupuesto incorpora el percentil 95 y la carga de simulación del servidor, conservando avisos de ataques.
- Inventario con filtros combinados de elemento, calidad y atributo, y comparación de cada pieza con la equipada sin efectos de conjunto.
- Chamán desde dificultad 3 (3:00), bruto desde 4 (4:30), capitán desde 5 (6:00) y mago desde 4, con probabilidades ajustadas.
- Medición local integral de Supervivencia con IA, inventario, habilidades y portales, disponible en Versus IA; informe con percentil 95.
- Docker incorpora el recurso de compilación que faltaba, conserva el diario de premios en un volumen y verifica x64/ARM64 en CI.
- Protocolo 27: actualizar cliente y servidor juntos.

## 0.20.2 — Controles comunes, más enemigos y precarga completa

- «Sobre mí» y «En área» ocupan la misma posición a la derecha en todos los modos, con la distribución de Supervivencia y escala adaptable a la pantalla.
- Supervivencia empieza con límites de 24, 32, 40 o 48 enemigos según los defensores y gana ocho plazas por dificultad. Entran grupos más frecuentes sin aumentar la vida ni el daño de los enemigos básicos.
- Tope adaptado a los FPS sostenidos: entre 32 y 192 enemigos, con recuperación gradual para evitar oscilaciones. Online cuenta el dispositivo más lento; nunca se ocultan amenazas cercanas para reducir la carga.
- Cuatro enemigos nuevos: corredor rápido y frágil, bruto con golpe de área anunciado, detonador con explosión anunciada y chamán que cura a otros enemigos. Aparecen progresivamente y tienen fichas en la guía.
- La conexión conserva la selección completa de las cuatro piezas del inventario temporal al enviarla al servidor.
- La carga recorre las zonas de cada mapa y prepara magos, animaciones, congelación, sonidos, proyectiles, barreras, meteoros, auras, botín e iconos antes de habilitar la partida.
- Anillos conserva sus 48 sectores preparados y reutiliza una reserva de enemigos. Solo se dibuja la zona cercana; atravesar portales no reconstruye sus sectores ni los enemigos ya preparados.
- Protocolo 26 para admitir la horda ampliada: actualizar cliente y servidor. Se mantienen las dos vidas, las compras, los premios pendientes y las actualizaciones diferenciales.

## 0.20.1 — Supervivencia estable y actualización con tiempo límite

- Supervivencia en Anillos integrada en main, con dos vidas, colosos, portales, tienda e inventario temporal y premios pendientes.
- Perfil → Buscar actualización permite consultar las novedades y descargar desde el aviso; la búsqueda termina con un error de conexión si la respuesta se bloquea, también durante la lectura.
- Las dos cuentas de pruebas reciben versiones estables posteriores y mantienen el acceso a futuras preliminares.
- La IA consume un 66 % menos de memoria temporal en la prueba de horda; el HUD evita recalcular la tienda cerrada y conserva todas sus funciones.
- Incluye el inventario abierto al equipar, el lateral con las mejoras recogidas y las correcciones de Traslación, Meteoro y Reanimar. APK con firma permanente y deltas verificados para Android y Windows.

## 0.20.0 — Supervivencia en main y búsqueda de actualizaciones

- Supervivencia en Anillos llega a la versión estable: mapa con pasillos y portales, dos vidas, colosos, botín temporal, tienda y premios pendientes cuando no hay conexión.
- Perfil incorpora «Buscar actualización»: muestra la versión instalada, consulta al pulsar e informa si hay novedades o falla la conexión. La descarga sigue requiriendo elegirla en el aviso.
- Las cuentas de pruebas también reciben una versión estable más reciente; conservan el acceso a futuras publicaciones experimentales.
- Reducida la memoria temporal de la IA de minions y evitados los cálculos de la tienda cerrada, conservando estadísticas, comportamientos y funciones del inventario.
- Incluye las correcciones de Traslación, Meteoro y Reanimar, el inventario que permanece abierto al equipar y el lateral dedicado a las mejoras recogidas. APK con firma permanente y actualizaciones diferenciales verificadas.

## 0.19.3 — Inventario abierto y hechizos con apuntado

- Equipar o quitar una pieza mantiene abierto el selector y conserva el desplazamiento; la selección se guarda al salir del inventario.
- Retirado «Volver al menú» de la tienda de Supervivencia.
- El lateral muestra solo las mejoras recogidas; el equipo conserva sus estadísticas en el inventario. Los elementos preparados quedan más cerca de los hechizos.
- En móvil, Traslación y Meteoro permiten ajustar dirección y distancia con el joystick derecho y se lanzan al soltar, con destino visible y cancelación sin gasto. Las magias interrumpen el ataque anterior; Traslación respeta los muros y Meteoro protege a los defensores aliados.
- Reanimar indica cuándo falta un aliado caído a menos de 8 m, busca suelo libre si un muro ocupa su posición y respeta el máximo de dos vidas.

## 0.19.2 — Publicaciones de pruebas con deltas

- Los tags de pruebas adjuntan los deltas Android y Windows de las tres versiones anteriores disponibles de su misma rama.
- A igual versión base se utiliza la última compilación verificable, sin mezclar paquetes de main ni de otras ramas.
- Cada delta se comprueba antes de publicar y se incluye en los manifiestos SHA-256; la APK reconstruida coincide exactamente con la firmada.
- Se mantienen la APK directa con firma permanente, el paquete Windows completo y el resumen de cambios de la publicación.
- Se conserva Supervivencia en Anillos de 0.19.1; el actualizador de pruebas sigue usando paquetes completos.

## 0.19.1 — Supervivencia y todas las reliquias visibles

- Supervivencia continua en Anillos: mapa de 192 × 144 m con pasillos y portales, sin castillo ni oleadas.
- Dos vidas por jugador; en cooperativo termina cuando todos los humanos las agotan.
- Colosos a intervalos variables y minions que persiguen al jugador, con impulsos ocasionales de velocidad.
- Se cargan solo los sectores cercanos; las bonificaciones y todas las reliquias aparecen en vertical a la izquierda.
- La energía de las reliquias también aparece en los totales; se mantienen el inventario compartido, la pausa en solitario y los premios pendientes.

## 0.19.0 — Supervivencia en Anillos

- Defensa pasa a supervivencia continua: se retiran las oleadas, el castillo y las reparaciones.
- Nuevo mapa Anillos de 192 × 144 m, con pasillos, salas y dos parejas de teletransportes.
- Dos vidas por jugador; la primera muerte reaparece a los 4,5 s y la segunda elimina. En cooperativo termina al agotar las vidas de todos los humanos.
- Los minions aparecen en todo el mapa fuera de 16 m de los defensores y los persiguen por los pasillos. Algunos aceleran un 60 % durante unos segundos.
- Colosos grandes y resistentes a intervalos variables: primero entre los minutos 2 y 3; después, de 90 a 150 s tras derrotar al anterior.
- Se crean y retiran solo los sectores cercanos, junto con sus enemigos, botín y efectos visibles. La simulación continúa fuera de cámara.
- Bonificaciones a la izquierda en vertical, vidas en el HUD y tienda adaptada al nuevo objetivo, manteniendo inventario compartido, pausa y premios pendientes.
- Protocolo 25 para vidas, colosos e impulsos de velocidad; cliente y servidor deben actualizarse juntos.

## 0.18.9 — Tres acciones por pieza

- Cada pieza muestra tres iconos en columna: vender, equipar o quitar y mejorar estadísticas; retirados los botones inferiores de equipación y mejora.
- Las mejoras de Defensa actúan sobre la pieza indicada, aunque no esté equipada, y actualizan atributos, oro y siguiente coste dentro del selector.
- Los iconos respetan operaciones pendientes, falta de oro, diez mejoras y máximos de atributos; la venta conserva su confirmación.
- Sobre mí y En área vuelven al lado derecho en Defensa, junto a los controles de combate.

## 0.18.8 — Papeleras y controles de combate

- Cada tarjeta del selector de botas, gorro, pecho y arma tiene su papelera y el oro de venta; permite vender sin equipar la pieza.
- Retirado el botón de venta de la pantalla principal del inventario; las ventas confirmadas actualizan el selector y conservan el desplazamiento.
- Retirados los botones Bastón y Vaciar de los controles táctiles de todos los modos.
- Sobre mí y En área se sitúan en la izquierda en Defensa, sin tapar las estadísticas ni el joystick.
- Se conserva el desglose de las cuatro piezas, los conjuntos y el total aplicado de la versión anterior.

## 0.18.7 — Bonificaciones por pieza

- La suma de bonificaciones separa las aportaciones de botas, gorro, pecho y arma, y muestra después el conjunto, la suma y el valor aplicado.
- El desglose se adapta a móvil y escritorio, incluye penalizaciones y posiciones vacías y cuenta solo las piezas equipadas.
- Las estadísticas de Defensa muestran el equipo por pieza y, a continuación, el total efectivo con nivel, reliquias y poderes.
- La consulta de equipamiento de otros magos utiliza el mismo desglose.
- Porcentajes con hasta dos decimales para que las aportaciones y el total no discrepen por redondeo.

## 0.18.6 — Novedades y guía de Defensa

- Las actualizaciones muestran un resumen breve de los cambios antes de descargar y durante la descarga.
- Nueva pestaña Guía en la tienda de Defensa con porcentajes de monedas, equipo, calidades, ranuras y reliquias.
- Bestiario con vida, ataque, alcance y velocidad del último ejemplar de cada tipo descubierto, además de aparecidos y derrotados.
- El bestiario se comparte con los aliados, conserva los enemigos derrotados y empieza de cero en cada partida.
- Se mantiene el inventario compartido con main, la dificultad por número de defensores y los sprites de botín.
- Protocolo 24 para las fichas de enemigos; notas de publicación de la versión exacta y reintento de publicación ante fallos temporales.

## 0.18.5 — Inventario compartido con main

- Integración de main en la rama experimental, conservando dificultad, bots y sprites de Defensa.
- La tienda de Defensa reutiliza la pantalla de inventario, sus cuatro ranuras, el selector por calidad y las tablas de atributos y conjuntos de main.
- Selección provisional y aplicación de las cuatro posiciones al volver a la tienda; permite quitar piezas y espera la confirmación de la partida.
- Compras, mejoras y ventas con oro temporal, sin modificar la cuenta ni su caché permanente.
- Atrás recorre selector, inventario, tienda y combate; la pausa con un solo humano continúa hasta cerrar la tienda.
- Protocolo 23 para la selección atómica del equipo temporal y validación de propiedad de todas las piezas.
- Comprobaciones de selector móvil, reapertura, compras, mejoras, ventas y aislamiento del inventario permanente.
- Empaquetado Windows sin el wrapper de consola que retenía procesos de compilación tras cerrar Godot.

## 0.18.4 — Base del inventario de main

- Selector de piezas por calidad en dos o tres columnas y guardado de la equipación de cuenta al salir.

## 0.18.3 — Botín reconocible

- Moneda pequeña con canto y grabados en ambas caras, girando sobre su eje vertical.
- Botas, gorro, pechera y bastón con sprites propios; halo según la calidad del equipo.
- Espada para potencia, escudo para protección, hoja con flecha para regeneración, corazón para vitalidad, ala para agilidad, rayo para energía y cruz para sanación.
- Símbolos orientados a cámara, contornos y flotación suave; atlas y mallas compartidos.
- Capturas de todas las recogidas en móvil y escritorio, giro de moneda y comprobación de privacidad y retirada.

## 0.18.2 — Inicio proporcional y bots distribuidos

- Vida inicial de minions normales de 340 a 442 entre uno y cuatro defensores; el grupo aumenta sobre todo la cantidad.
- Límite de enemigos simultáneos de 8 a 20 al comenzar, con aumento gradual; grupos pequeños y pausas variables.
- Dificultad temporal cada 90 s, crecimiento inicial más suave y tanques menos frecuentes desde dificultad 5.
- Bots con objetivos distintos, puestos de guardia, separación de aliados y reparto de curaciones.
- Los bots mantienen la carga del rayo al continuar un ataque y liberan los objetivos derrotados.
- Pruebas de tiempo de muerte con hechizos reales y supervivencia durante tres minutos, con uno a cuatro defensores.

## 0.18.1 — Defensa: dificultad, tienda y premios pendientes

- Pausa de toda la simulación al abrir la tienda con un solo humano, también con bots aliados y por red.
- Minions con más vida, tanques lentos desde la tercera oleada y apariciones en grupos de ritmo variable.
- Meteoritos cada 3 s, berserker periódico, pulso glacial y cadena de relámpagos: compra y cinco niveles por partida.
- Tienda por pestañas con efectos, niveles, coste y oro faltante; estadísticas de los poderes activos.
- Diario de premios persistido antes de confirmar el hito, recuperación tras caídas y recibos idempotentes.
- Protocolo 22 para el nuevo estado de pausa y habilidades.

- Dos UID autenticados reciben actualizaciones de ramas experimentales; las otras cuentas conservan las versiones estables.
- Selección independiente de la lectura del perfil Firestore, comprobación de la rama, SHA-256 y certificado permanente de Android.
- Publicación de preliminares verificadas con APK directa, sin cambiar la última Release estable.
- Cambio de sesión invalida avisos y descargas pendientes; compilaciones de igual versión se distinguen por su número de Actions.

## 0.18.0 — Defensa del bastión (rama experimental)

- Modo cooperativo local y online, de uno a cuatro defensores, con oleadas o ataques continuos.
- Mapa propio con bastión estático, perímetro seguro y minions de cuerpo a cuerpo, arqueros, magos y capitanes.
- Experiencia, equipo, reliquias y oro temporales; tienda, mejoras, tres auras y reparación compartida de coste creciente.
- Conservación de mejoras al morir; reaparición progresiva y reinicio completo de la partida.
- Premios permanentes cada cinco minutos activos, hasta cuatro, con recibos y reintentos independientes de PvP.
- Interfaz de estadísticas e inventario, totales con bordes y controles de defensa adaptados al móvil.
- Protocolo 21 y distribución independiente por artefactos de Actions; sin publicación ni actualización estable.
- Pruebas de reglas, red, persistencia, controles táctiles y carga con 64 minions.

## 0.17.24 — Canal de pruebas por cuenta

- Actualizador por UID autenticado: versiones preliminares para las dos cuentas autorizadas y estables para las demás.
- Publicación verificada de ramas, conservación de firma e integridad, identificación de cada compilación y limpieza al cambiar de sesión.

## 0.17.22

- Aura visible alrededor de las rocas imbuidas: fuego rojo, agua azul, frío cian, vida verde y arcano rosa.
- Las mezclas de dos elementos secundarios conservan ambos colores en el halo, las grietas, los anillos, la estela y los fragmentos del impacto.
- Mallas y materiales de aura reutilizados; efectos contenidos en los presupuestos de partículas existentes.
- Comprobación de todas las recetas válidas de roca, colores y mezclas, estelas e impactos; capturas reales de las cinco auras y una mezcla en móvil y escritorio.
- Versión Windows y Android sincronizada en 0.17.22; código Android 17022.

## 0.17.21

- Piedra con siluetas irregulares y caras facetadas, grano mineral y grietas; las rocas imbuidas conservan su forma y muestran el elemento secundario en las vetas.
- Icono de Tierra con volumen y contorno, muros con superficie pétrea y armadura con placas visibles que disminuyen al perder escudo.
- Fragmentos de roca y polvo en impactos, estelas y carga; mallas compartidas y límites de partículas conservados.
- Comprobaciones del radio visual, normales, reutilización, combinaciones con frío y ciclo de la armadura; cuatro capturas reales adicionales de piedra en móvil y escritorio.
- Versión Windows y Android sincronizada en 0.17.21; código Android 17021.

## 0.17.20

- El aviso de actualización espera a que se elija Descargar actualización o Ahora no; al aceptar, la instalación continúa automáticamente.
- Reintentos limitados con peticiones nuevas ante fallos temporales de GitHub, desconexiones, descargas cortadas y tiempos de espera.
- Los fallos de red no se marcan como incompatibilidad del parche ni fuerzan la descarga completa; solo un fallo al aplicar el parche activa esa alternativa.
- Temporales independientes por intento, cancelación sin reintentos y reutilización de archivos ya verificados.
- Regresiones del primer intento, reintento manual, integridad, cancelación y elección en el aviso real; errores diferenciados entre descarga e instalación en los registros.
- Versión Windows y Android sincronizada en 0.17.20; código Android 17020.

## 0.17.19

- Desplazamiento táctil sobre el contenido en todas las pantallas con scroll, también sobre tarjetas y fondos de la ayuda y los formularios.
- Los controles decorativos dejan pasar los gestos al scroll nativo, incluidas las secciones creadas dinámicamente; botones, campos, listas y barras mantienen su entrada.
- Regresión con gestos táctiles emulados sobre las pantallas reales del menú, ayuda, inventario y suma de bonificaciones.
- Versión Windows y Android sincronizada en 0.17.19; código Android 17019.

## 0.17.18

- Bordes visibles entre filas y columnas en las tablas de equipación, bonificaciones, clasificación y ayuda, con cabeceras diferenciadas y texto adaptable.
- La suma propia y la consulta de otros magos muestran solo los efectos aplicados: se ocultan valores cero, efectos anulados y conjuntos sin activar; se conservan penalizaciones y límites reales.
- Resumen compacto con desglose completo en móvil, aviso sin bonificaciones y comprobaciones de equipo vacío, cancelaciones y consulta pública.
- Versión Windows y Android sincronizada en 0.17.18; código Android 17018.

## 0.17.17

- Mensaje del actualizador más claro: la nueva versión se descarga e instala automáticamente.
- Actualizaciones automáticas y diferenciales en Windows y Android, con verificación SHA-256 y descarga completa si el parche no es compatible.
- Versión Windows y Android sincronizada en 0.17.17; código Android 17017.

## 0.17.16

- Integración en main de las ramas de mejoras generales, correcciones LAN/PvP/recompensas e interfaz responsive.
- Marco principal centrado con contenedores flexibles, inventario adaptable y barras de desplazamiento vertical visibles.
- Información de cuenta y resultados concentrada en Perfil; configuración opcional del servidor de ventas en Inventario.
- Ayuda con explicaciones de los 22 atributos de equipación y ejemplos que distinguen defensa de absorción del escudo.
- Avisos de victoria y derrota al terminar una ronda para ambos equipos, con marcador de serie y cuenta atrás sincronizados.
- Versión C#, Windows y Android sincronizada en 0.17.16; código Android 17016.

## 0.14.0

- Equipación ampliada a los ocho elementos: Agua, Vida, Escudo, Frío, Rayo, Arcano, Tierra y Fuego.
- Once atributos nuevos: potencia de los cinco elementos incorporados, duración de mojado, enfriamiento, congelación, Rayo contra mojados y rapidez de carga del haz y de rocas.
- Cada familia tiene un conjunto Mítico de dos o tres piezas y otro de cuatro; se conservan los bonos de Vida, Escudo y Fuego.
- Daño elemental aplicado por componente en fusiones, barreras, reflejos y estados; las cargas aceleradas conservan la potencia máxima.
- El selector muestra los atributos usados por las piezas y los conjuntos, incluidos los que se compensan hasta cero. Un botón permite consultar los ocho conjuntos en móvil y escritorio.
- Catálogo de generación 2 y esquema de cuenta 1: las tiradas, IDs, mínimos, máximos y selecciones existentes no se modifican. Protocolo 13 para impedir partidas con clientes que desconocen las familias nuevas.

## 0.13.1

- Selector independiente de botas, gorro, pecho y arma mediante flechas izquierda/derecha, con opción Sin equipo y selección circular.
- Atributos individuales con valor actual e intervalo; tabla de piezas, conjunto, suma y valor aplicado con límites compartidos con la simulación.
- Guardado automático de cada selección en la cuenta o en la caché local para IA; comprobación de recuperación al cambiar de partida y al volver a abrir el juego.
- Se retiran los adornos de equipación del mago: los sprites permanecen como estaban. La configuración de alojamiento y de Firebase no cambia.
- Pruebas de flechas reales en Godot, caché, dos partidas IA, sumas y límites, y recuperación de las cuatro posiciones desde Firestore y una nueva sesión.

## 0.13.0

- Cuentas Google verificadas por Firebase Admin y persistencia en Cloud Firestore, con UID independiente de la versión del juego.
- Objetos procedurales en cinco calidades; velocidad, alcance, defensa y potencia por posición; familias Vida, Escudo y Fuego en los dos niveles superiores.
- Mínimo, máximo y valor actual conservados por atributo. Conjuntos Míticos de dos o cuatro piezas, utilizables en PvP y contra IA.
- Inventario y personalización desde launcher y sala; caché de práctica sin conexión, vista previa, comparación de límites y totales.
- Recompensa por victoria PvP entre humanos activos, diario recuperable, recibos sin duplicados y aviso al cliente.
- Eliminar objetos abona oro y desequipa mediante una transacción; el oro queda reservado para futuras mejoras.
- Pruebas de Firestore con emulador oficial, sesiones, concurrencia, fin de partida y conservación de tiradas. Protocolo 12, Android 13000.


## v0.12.24

- Una tabla común relaciona los ocho elementos con una ventaja y una debilidad. Daño y presión contra protecciones: ×1,5 favorable, ×0,5 desfavorable y ×0,18 contra el mismo elemento; mezclas calculadas por componente. Los bonus de clase y objetos se conservan y se aplican además de la tabla.
- Vapor y hielo reparten su daño entre sus dos elementos y conservan el aporte de un tercer elemento. Las recetas directas resuelven las mismas cancelaciones y el mismo límite que el teclado.
- Escudos básicos frontales y en anillo permiten pasar a los hechizos de su propietario. Se mantiene la colisión del movimiento y el bloqueo de hechizos ajenos y de barreras imbuidas. Incluye emisión, rayos, conos, cadenas, proyectiles, áreas y splash; un rayo combinado con otros propietarios no hereda ese permiso.
- Agua y Vida conservan sus efectos de estado y curación y aplican sus relaciones al desgaste de barreras. Ayuda con tabla de los ocho elementos. Protocolo 11: actualizar cliente y servidor juntos. [Reglas y validación](docs/RELEASE-v0.12.24.md).

## v0.12.23

- Selector de equipo en las salas: Azul y Rojo se respetan al crear y entrar; Automático equilibra. Corregido el identificador de Automático, que coincidía con Azul. Un duelo conserva oponentes en equipos distintos y permite elegir el lado frente al bot.
- Preparación gradual del mapa, atlas, sonidos, partículas, proyectiles, rayos, conos, barreras y efectos con barra de progreso. Los recursos se dibujan detrás de la pantalla de carga antes de habilitar los controles, para preparar también las variantes gráficas de OpenGL.
- Una sala nueva espera la confirmación de carga de todos sus clientes. Cancelar libera la espera; una entrada tardía conserva la partida y protege al nuevo jugador hasta terminar. Protocolo 10: actualizar cliente y servidor juntos.
- Regresiones de equipos y sincronización, carga y cancelación en Godot, y creación de sala desde el formulario con dos clientes y OpenGL. [Detalles y validación](docs/RELEASE-v0.12.23.md).

## v0.12.22

- Optimización de simulación, colisiones, capturas, canales, recetas y copias de estado: un 68,7 % menos de asignaciones temporales en el recorrido medido de los seis mapas respecto a v0.12.21. Los estados finales conservan exactamente la misma huella SHA-256.
- JSON enviado y leído directamente en UTF-8, con buffers de recepción reutilizados por conexión. Se mantienen los límites, la fragmentación, las colas y el protocolo 9; los snapshots conservan copias independientes de todas sus matrices mutables.
- Rayos con longitud aplicada mediante escala, sin reconstruir sus vértices, y barreras de piedra agrupadas en tres mallas por material, conservando geometría, normales y sombras. Unas 840 llamadas de dibujo menos por fotograma en la escena medida de combate máximo.
- Cachés de atlas, colores, textos y protecciones para evitar cambios repetidos en el motor y la interfaz cuando el estado no ha variado. Las comparaciones visuales de barreras, rayos y variantes elementales coinciden píxel a píxel con la base.
- Medidor `--profile-protocol`, informes de simulación y protocolo en CI y regresiones de colisiones, copias independientes y recepción de mensajes fragmentados. [Cambios, mediciones y validación](docs/RELEASE-v0.12.22.md).

## v0.12.21

- Verificación local: 142 pruebas de reglas, 20 de red y comprobaciones Godot de mapas, laboratorio, efectos y sprites. Entre un 73,2 % y un 77,7 % menos llamadas de dibujo en la escena fija de decoración medida.
- Navegación: busca primero el nodo más cercano y solo examina alternativas ordenadas si está bloqueado. Conserva la ruta, los desempates y las colisiones con obstáculos y muros temporales.
- La cuadrícula de navegación se prepara al cargar cada mapa; el primer recálculo ya no la construye en medio de la simulación.
- Decoración opaca agrupada por material y zonas de 12 unidades. Mantiene sus vértices, transformaciones y sombras y conserva el descarte por zonas fuera de cámara.
- Una pasada de sombras direccionales para la vista ortográfica, con alcance de 70 unidades; se conservan las sombras durante la partida.
- Limpieza de portales y respawns visuales al cambiar al laboratorio para evitar referencias a nodos liberados.
- Perfilado específico de decoración con `--profile-scenery` y medición de simulación de los seis mapas con `--profile-maps`, incluyendo huella del estado final para comparar comportamiento.
- Comprobaciones de los seis mapas, agrupación sin pérdida de vértices y transición Frente → Laboratorio → Ciudadela, más una prueba de navegación con el nodo más próximo bloqueado. [Mediciones y validación](docs/RELEASE-v0.12.21.md).

## v0.12.20

- Laboratorio local sin captura, tickets ni victoria; un único punto de reaparición del jugador, al cabo de un segundo.
- Catorce objetivos quietos: sin estados, mojado, quemado, enfriado, congelado, celeridad, escudo, armadura, regeneración y guardas de agua, frío, rayo, arcano y fuego. Reciben daño normal, reaparecen en su sitio al morir y recuperan su estado inicial. Los estados no caducan por tiempo, pero conservan las interacciones elementales.
- Las 32 combinaciones de barreras y minas del catálogo, con teclas y elementos señalados. Resistencia y efectos normales, restitución tras destrucción/activación a los dos segundos y espera si un jugador ocupa una pared.
- Lanzador configurable de dirección fija: rayos, conos y cadenas sin cortes; rocas cargadas y fragmentos cada 120 ticks. Pausar o cambiar receta cancela el lanzamiento anterior.
- Medición independiente de daño a vida, absorción y curación efectiva, incluido daño y curación simultáneos. Restablecer objetivo o preparar al jugador con el 40 % de vida para medir curación.
- Trece pruebas de reglas del laboratorio y comprobación Godot de estaciones, geometría sin puntos, controles reales del selector y curación del lanzador. Incluido en CI de Linux y del ejecutable Windows. [Detalles](docs/RELEASE-v0.12.20.md).

## v0.12.19

- Corregida la discrepancia entre VERSION y los presets Windows/Android que seguían en 0.12.14. C# deriva su versión de VERSION; los empaquetadores sincronizan los metadatos antes de exportar y restauran los presets originales al terminar.
- Código Android creciente derivado de la versión y comprobado junto al nombre del paquete. Los errores finales muestran las versiones esperada y encontrada; se conserva la validación del ejecutable o APK real.
- Pruebas de presets antiguos, versión futura, PowerShell/Python equivalentes, lectura real de MSBuild, saltos CRLF y metadatos inválidos. Exportación Android simulada en ambas arquitecturas que conserva APK, checksum y presets si fallan la versión, el código, la firma o Godot.
- Comprobaciones de versiones incorporadas a CI, incluidas Windows PowerShell 5.1. [Causa y validación](docs/RELEASE-v0.12.19.md).

## v0.12.18

- Movimiento barrido hasta el primer contacto y deslizamiento según la normal de columnas, paredes y límites. El personaje aprovecha el avance disponible en lugar de descartar todo un eje.
- Rodeo asistido al mantener la dirección, también en choques frontales. El lado se conserva durante el contacto; en paredes planas se prefiere un extremo accesible y próximo.
- Presupuesto de desplazamiento conservado: el roce no aumenta la velocidad ni la intensidad del joystick. Soltar o cambiar el mando cancela el rodeo; el empuje y la congelación no lo activan.
- Estado de deslizamiento reiniciado al reaparecer o trasladarse. Se mantienen las colisiones de esquinas, zonas sin suelo y muros que sellan un pasillo completo.
- Quince regresiones de movimiento, incluidos los pilares de los cinco mapas, uniones de muros, paredes oblicuas, extremos cerrados, caminar al cursor y empujes. [Detalles y validación](docs/RELEASE-v0.12.18.md). Protocolo 9.

## v0.12.17

- Apuntado actualizado antes de ejecutar acciones del mismo lote: fragmentos, barreras, cuerpo a cuerpo y liberación de rocas usan el último destino recibido, sin esperar al siguiente paso de simulación.
- Origen del bastón limitado por paredes, obstáculos y fronteras. Acercarse a una pared ya creada no permite emitir rayos o chorros al otro lado.
- Navegación con aproximación al último lugar alcanzable cuando un muro temporal corta el pasillo. Los bots se acercan y abren un paso antes de su caducidad, conservando las colisiones.
- Bots con objetivo de muro conservado durante la carga y eliminado al cancelar el hechizo o perder la pared. Solo atacan paredes que bloquean el objetivo; no inician ataques contra enemigos ocultos sin línea de visión.
- Límite ambiental de doce segmentos comprobado antes de añadir cada grupo de paredes, además del máximo global de cuarenta. Diez regresiones nuevas de acciones inmediatas, colisiones, rutas y bots.
- [Revisión y validación](docs/RELEASE-v0.12.17.md). El protocolo sigue siendo 9.

## v0.12.16

- Orientación de marcha según el desplazamiento interpolado, aunque el ratón o el joystick de apuntado sigan señalando en sentido contrario. Al terminar un hechizo, el sprite recupera la dirección y los pasos del movimiento.
- Dirección de apuntado conservada durante la carga, la canalización y las poses de lanzamiento instantáneo; las reglas de combate y el protocolo 9 se mantienen.
- Regresión reproducida antes de la corrección y comprobaciones en Godot de las cuatro direcciones a 30/60/120 FPS, interpolación local/de red, lanzamiento, fin de lanzamiento y parada. Galería de los cinco atlas adaptada al recorrido de cada orientación.

## v0.12.15

- Nuevo Frente de conquista: pasillo irregular de 480 unidades y cinco puntos consecutivos. Cada equipo solo captura el primer punto libre o enemigo desde su base; el servidor bloquea saltos e infiltraciones posteriores.
- Reaparición y entradas tardías detrás del último punto conectado a la base. El frente retrocede al perder o neutralizar terreno. Victoria al controlar los cinco puntos, sin bajas, tickets ni contador de puntuación.
- Tres pares de portales bidireccionales, con enfriamiento y salidas transitables. Meteoros aleatorios con aviso previo y muros de piedra destructibles de 30 segundos; daños ambientales independientes del fuego amigo.
- Bots que persiguen el objetivo permitido, evitan muros en sus rutas y atacan las paredes ambientales. Minimapa con portales, avisos, muros y reapariciones avanzadas.
- Eliminadas las etiquetas de los círculos de captura y las mallas negras que bordeaban los escudos.
- Apuntado ampliado para cubrir los extremos del mapa. Validación de red con el número de puntos de cada mapa y protocolo 9; cliente y servidor deben actualizarse juntos.
- Regresiones de captura, neutralización, disputa, victoria, respawn, portales, peligros, reinicio, red y cliente Godot.
- [Detalles de la versión](docs/RELEASE-v0.12.15.md).

## v0.12.14

- Seis círculos mágicos transparentes para los puntos de control, alternados por mapa y con sus colores originales; eliminados el disco, el borde oscuro y el cristal central.
- Anillo de progreso luminoso firmado por el servidor. La disputa pulsa en brillo sin alterar el porcentaje; reset de material, etiqueta y estado al reiniciar.
- Licencias Android con consola heredada y comprobación final no interactiva del mismo SDK. Se rechaza continuar con licencias pendientes incluso si sdkmanager devuelve código cero.
- Regresiones de progreso, neutralización, disputa y limpieza en Godot y de aceptación/comprobación de licencias en Windows PowerShell 5.1.
- [Detalles de la versión](docs/RELEASE-v0.12.14.md).

## v0.12.13

- Joystick móvil flotante desde cualquier punto libre de la mitad izquierda. Prioridad de botones, intensidad proporcional, propiedad de cada dedo y cancelación al soltar, morir o usar Traslación.
- Bloqueo de compilación compartido entre PowerShell y Bash/Python para proteger presets y herramientas. Pruebas de concurrencia, liberación y fallos.
- Snapshots acotados por contorno, tamaño y valores de los hechizos antes de representar estado remoto; pruebas con dimensiones gigantes y combate en los cuatro mapas.
- Coordinador de CI, escenas, capturas y verificaciones extraídos de `Main` a `Scripts/Verification`, conservando una interfaz interna de acceso al juego.
- Perfil gráfico de 16 magos y efectos máximos: CPU/GPU, draw calls, primitivas y memoria, con procedencia y contadores no disponibles explícitos. Acceso desde Práctica, exportación a diagnóstico y script ADB para medir en móvil.
- Perfiles automáticos en OpenGL por software y Android 15 emulado. La medición de un móvil físico requiere ejecutar la escena en ese dispositivo. [Detalles](docs/RENDIMIENTO.md).

## v0.12.12

- Traslación calcula dirección y orientación con el último destino aunque la intención llegue justo antes del salto. Mantiene los límites, colisiones y cancelación de la ruta anterior.
- Joystick con intensidad proporcional; diagonales limitadas. Rutas de ratón y bots conservan su velocidad.
- Arco de barreras sin piedra agrupado por material: 40 mallas pasan a 2. Búsqueda del nodo de navegación más próximo sin ordenar el mapa.
- Compilación Android Bash con validación de importación/exportación, aviso de EditorSettings solo con su traza y APK temporal. Un fallo conserva APK, checksum y presets.
- Regresiones de salto, joystick y exportación; comprobación nativa de geometría de barreras. Texto F1 y arquitectura actualizados.
- [Auditoría del proyecto y mejoras pendientes](docs/AUDITORIA-v0.12.12.md).

## v0.12.11

- Morir y reaparecer cancela el destino anterior del ratón y los dedos del joystick. Mantener pulsado no restaura la orden; el movimiento nuevo parte de la reaparición. También se detecta otra vida si se pierde el snapshot de muerte.
- Mago y cámara se ajustan al reaparecer en los tres modos, sin interpolar la posición de la vida anterior.
- Escudo básico amarillo más saturado, superficie más opaca, retícula definida y contorno oscuro. Puntos de captura y campos del suelo con bordes oscuros, colores sin lavado por la iluminación, rellenos más visibles y etiquetas con contraste.
- Paleta más nítida en PC y móvil; iluminación neutra y terreno menos brillante para distinguir los efectos.
- Herramientas Android extraídas archivo por archivo a `cmdline-tools/latest`, creando sus carpetas explícitamente. Validación de ZIP, sdkmanager y bibliotecas; reparación de instalaciones parciales y descarga de nuevo si el ZIP guardado está dañado.
- Los scripts Windows y APK muestran como aviso la consulta conocida de EditorSettings al cerrar Godot .NET. Rechazan otros errores incluso con código de salida cero y verifican los archivos generados.
- Comprobaciones de entrada tras muerte/reaparición y de cámara, pruebas de extracción en PowerShell 5.1 y capturas OpenGL de escudo, campos y captura sobre hierba y piedra.

## v0.12.10

- Traslación cancela el destino del clic anterior en el cliente, evitando que los siguientes paquetes de entrada lo restauren después del salto.
- Mantener el botón izquierdo pulsado no reactiva ese destino; una nueva orden de movimiento continúa desde el punto de llegada.
- La ruta de navegación anterior se descarta al saltar. Tecla 2 y botón táctil comparten el tratamiento de la entrada.
- Comprobaciones en Godot de clic previo, clic mantenido, parada después del salto y movimiento desde la llegada con un nuevo clic o el joystick.

## v0.12.9

- Movimiento del mago interpolado entre posiciones confirmadas, con 75 ms de margen en red y un paso de simulación en práctica. La cámara sigue la posición visible.
- Pasos y oscilación del cuerpo ligados a la distancia recorrida; adaptados a lentitud, carga y celeridad. Parada sin arrastre, reposo estable y ajuste inmediato tras congelación, reaparición, teletransporte o pausa prolongada.
- Paleta móvil más saturada y diferenciada: agua azul, frío turquesa, rayo violeta y arcano magenta. Controles con fondo más opaco, bordes de tres píxeles y símbolos elementales.
- Iluminación móvil más neutra y terreno menos brillante. Barreras, rayos y partículas conservan más color en sus reflejos.
- Comprobaciones de movimiento a 30, 60 y 120 FPS con snapshots a 20 Hz y fluctuación de llegada, paradas, pérdidas de paquetes y giros. Contraste de texto móvil y revisión OpenGL de controles, barreras y rayos.

## v0.12.8

- Barreras con intensidad visible según las cargas. Una piedra y escudo conservan las dos hiladas; dos piedras y escudo añaden una tercera hilada, grosor y contrafuertes. Las capas elementales se adaptan al volumen.
- Escudos personales con cero, una o dos bandas según los elementos del lanzamiento, incluidos los repetidos. La intensidad disminuye al recibir daño. Los hechizos de un solo elemento conservan su aspecto.
- Todos contra todos: entradas, reapariciones y reinicios en posiciones aleatorias por el mapa, independientes del equipo, con separación de jugadores y evitando obstáculos, muros y campos peligrosos. Semilla opcional para reproducir pruebas.
- Un segundo de invulnerabilidad en Arena, visible mediante una envoltura dorada, anillo y texto INMUNE; bloquea daño, estados y empujes y caduca a los 60 pasos.
- La cámara se centra inmediatamente al reaparecer en todos contra todos, incluso al cambiar de extremo del mapa.
- Campos opcionales de intensidad del escudo y segundos de invulnerabilidad en el protocolo 8. Los modos por equipos conservan sus bases y protección.
- Se conservan capacidad de escudos, vida de muros, daño y alcance. Movimiento y paleta móvil evaluados, pendientes de decisión.
- Pruebas de distribución en los cuatro mapas, duración de la protección, replicación y validación de intensidad; comprobación Godot del volumen reforzado, bandas y caducidad del halo.

## v0.12.7

- Muros de piedra con bloques irregulares en dos hiladas, juntas alternadas y caras rugosas.
- Llamas con turbulencia y puntas; cascadas continuas con espuma en el suelo; arcos eléctricos ramificados y chispas en la superficie. Los elementos combinados se superponen a la piedra.
- Meteorito con 80 facetas, basalto, grietas incandescentes y estela de dos capas orientada según el descenso. Brasas y humo dentro del presupuesto existente.
- Mejora visual limitada a barreras y meteorito; se conservan los valores de combate y el protocolo 8.
- `Compilar-APK.cmd` para generar una APK ARM64 desde Windows con doble clic. Prepara las herramientas, conserva la firma local y verifica la APK antes de sustituir la anterior.

## v0.12.6

- Barreras frontales y anillos con superficies elementales animadas: fuego, agua descendente y espuma, arcos eléctricos y chispas, cristales de hielo y vapor. Las mezclas muestran sus componentes; las barreras de tierra conservan la roca con la capa elemental encima.
- Meteorito con roca facetada incandescente, estela y brasas, caída acelerada sincronizada con el aviso del servidor y círculo de 4 m. Impacto con fragmentos, humo, dos ondas y un destello breve a ras del suelo.
- Materiales y nodos reutilizables, efectos dentro de los presupuestos existentes. Comprobaciones de las 18 combinaciones de barrera, caducidad, caída, radio, impacto y saturación; los controles móviles ejercitan piedra con fuego y meteorito.
- Capturas OpenGL de barreras combinadas y etapas del meteorito. Los valores de daño, curación, colisión y duración del combate se conservan; parámetros del meteorito compartidos con la simulación.
- Liberación explícita de variantes de textura, materiales y atlas de los magos al retirarlos de la escena y al cerrar.

## v0.12.5

- Daño directo de los láseres progresivo por tiempo de canalización: del 30 % del daño original al 150 % en 2 s; permanece al máximo hasta soltar o interrumpir. No modifica curación ni efectos de estado.
- La carga se calcula en el servidor y la práctica; se reinicia al soltar, sustituir, autocast, timeout, congelación, muerte o reaparición. Cada rayo conserva su progresión al cruzarse y reflejarse.
- La barra inferior muestra del 0 al 100 % de carga. Núcleo, halo, filamentos y pulsos ganan grosor e intensidad moderada, conservando el presupuesto gráfico.
- F1 muestra daño inicial y máximo; tres arcanos pasan de 103,5 a 517,5 daño/s. Captura OpenGL de cinco etapas de carga y pruebas de límites, interrupciones, cruces, reflexión y curación.

## v0.12.4

- Ajustes tras revisar capturas: láser de fuego con tono cálido, frío azul y electricidad con filamentos quebrados; núcleo menos blanco para distinguir combinaciones.
- Barreras de tierra con rocas de cinco caras, alturas y orientaciones irregulares.
- Ocultación de todos los marcadores de control cuando la vista no los utiliza, incluidas las demostraciones de hechizos.

## v0.12.3

- Muros y anillos de barreras con superficie de energía arqueada, trama sutil, bordes, glifos y aparición/desaparición gradual. El daño se refleja en la superficie y con un destello breve. Muros de tierra con bloques de roca y sombras.
- Láseres con núcleo y halo animado; flujos propios para arcano y curación, modificadores de fuego, frío y electricidad. Filamentos y pulsos aumentan con una, dos y tres cargas, con iluminación moderada.
- Todos los canales se dibujan antes de los detalles, manteniendo el límite de 512 segmentos sin ocultar rayos principales por adornos. Capturas OpenGL de barreras y cinco variantes de láser.
- Se conservan el escudo personal, las colisiones, el daño y los alcances del protocolo 8. El alcance queda pendiente de elegir un nuevo modelo.

## v0.12.2

- Tres casillas de elementos preparados y barra de carga móviles justo encima de los cuatro hechizos, junto al borde inferior. La zona central queda libre para ver la partida.
- Indicador de mejoras alineado con el mismo bloque inferior. Los elementos siguen en el borde derecho y los joysticks conservan su posición.

## v0.12.1

- Ocho elementos móviles en dos columnas y cuatro filas junto al borde derecho, encima del joystick de lanzamiento. Botones ampliados de 90 × 62 a 108 × 72.
- Minimapa móvil desplazado a la izquierda para dejar libre la selección de elementos.

## v0.12.0

- APK Android ARM64 firmada para instalación directa, con práctica sin conexión y conexión a salas de PC por IP/LAN.
- Joysticks multitáctiles, ocho elementos, ataques frontal/sobre ti/área/bastón y cuatro magias especiales. Interfaz horizontal y acceso táctil a ayuda, combinaciones, clasificación y menú.
- Cancelación de dedos al pausar, abrir ayuda o pasar a segundo plano; botón Atrás y liberación fuera del control.
- CI: pruebas móviles Godot, capturas OpenGL, verificación de firma/contenido de APK y ejecución de la app en Android 15.

## 0.11.1

- Revisión de cliente, servidor, simulación, protocolo, interfaz y distribución.
- Cancelación de cargas centralizada: muerte, congelación, falta de entrada, cambio de hechizo y final de partida limpian la carga. El cliente ignora valores residuales de servidores anteriores cuando el mago no está lanzando.
- Cada descarga de hielo emite un único evento de lanzamiento. Se conservan todos sus fragmentos, con menos sonidos y efectos duplicados.
- Colisión continua de empujes y proyectiles con obstáculos y muros. Los muros destruidos o caducados dejan de aplicar daño en ese mismo paso.
- Los rayos combinados aplican a cada componente la bonificación de daño de su autor. Reaparición y reanimación evitan posiciones cubiertas por muros.
- Cancelación de conexiones pendientes, rechazo de callbacks obsoletos y cierre de conexiones sustituidas. Volver al launcher permanece disponible durante la carga de salas; las búsquedas LAN canceladas no alteran operaciones posteriores.
- Validación compartida de servidores y salas por HTTP, WebSocket y LAN; reglas, aforo y mapas completos. Snapshots con identificadores repetidos, eventos desordenados o vida/carga inválidas se rechazan antes de dibujar.
- F1 reutiliza su receta y snapshot, calcula el filtro una vez y mantiene pasos exactos de 60 Hz. Costes, nombres y esperas de las magias comparten una única tabla con la simulación.
- Retirados un campo y parámetro de launcher sin uso, estado redundante del renderer, un helper antiguo de aparición, uniformes de terreno y cálculos de escudo sobrescritos. Corregido el zoom de las capturas generales.
- Versiones de Windows, Docker, Compose y LEEME actualizadas. La compilación verifica los metadatos del ejecutable; el servidor alojado no hereda un mapa externo.
- Protocolo 8. 99 pruebas de combate, HTTP, diagnóstico y red, regresiones de conexiones en Godot y comprobación del paquete Windows, mapas y efectos de una/dos/tres cargas.

## 0.11.0

- Efectos según el número de elementos del hechizo, incluidos los repetidos. Una carga es visible y compacta; dos añaden detalle; tres aumentan más la densidad y los acentos.
- Estelas de una, dos o cuatro partículas, aros finos de proyectil, más hebras en rayos continuos, ramificaciones cortas de electricidad y trazos de sprays. Muros, minas y áreas incorporan marcas discretas.
- Lanzamientos e impactos más densos por nivel y segunda onda interior breve en novas/explosiones de tres cargas. Emisión luminosa moderada y partículas cortas; las respuestas de daño continuo y rotura se mantienen breves.
- Límites de 320 partículas, 24 ondas, 64 partículas nuevas por fotograma y 512 segmentos de rayos. Pools reutilizables y limpieza al salir.
- El canal activo conserva sus elementos aunque se prepare otra combinación. Las demostraciones F1 usan los mismos efectos; daño, alcance y radios pertenecen a la simulación.
- Protocolo 8 compatible con v0.10.0. 88 pruebas de combate, diagnóstico y red; comprobaciones Godot de progresión, repetición de elementos, saturación y reutilización; capturas comparables de una/dos/tres cargas y ejecución del paquete Windows.

## 0.10.0

- Coliseo de la ascensión con catorce objetos, dos por cada tipo de mejora, en posiciones aleatorias calculadas por el servidor. Cada objeto cambia de sitio al reaparecer tras doce segundos y al reiniciar la partida.
- Las posiciones evitan límites, pilares, apariciones, puntos de control, otros objetos, jugadores y muros activos. La distribución cambia entre salas; una semilla opcional permite reproducir pruebas y capturas.
- Al morir se pierden las mejoras de objetos y su vida máxima temporal. La vida permanente obtenida por bajas en Ascensión se conserva; reanimar no devuelve las mejoras perdidas.
- Panel de vida y escudo restaurado a su tamaño y márgenes originales. Los demás elementos del HUD mantienen la escala reducida.
- Arena clásica como bosque con senderos, Ciudadela como calles urbanas con casas y tejados, Coliseo como ruinas con vegetación y Santuario como bosque con menhires. Suelo, decoración, obstáculos visibles e iluminación propios por escenario; plantas agrupadas en MultiMesh.
- Los objetos y el minimapa siguen las nuevas posiciones. La decoración de gran tamaño queda fuera del suelo transitable; las reglas de colisión y victoria siguen compartidas con el servidor.
- Protocolo 8. 88 pruebas de combate, diagnóstico y red; 80 distribuciones de objetos, conservación del bonus permanente, escala del panel y comprobaciones gráficas OpenGL de los cuatro mapas y del paquete Windows.

## 0.9.0

- F1 muestra el daño base de las 120 combinaciones en sus tres modos: impacto, daño/s, intervalo de carga, daño por fragmento o mina, quemadura, bonus contra mojado y curación. Las cifras comparten constantes con el combate.
- Interfaz de partida al 75 % del tamaño anterior, conservando las posiciones de los paneles en los bordes de la pantalla.
- Tres casillas encima de cada mago, rellenadas con el color y la tecla de los elementos elegidos. La combinación sigue visible mientras se carga una roca o se mantiene un canal; se sincroniza también para los rivales.
- Cuarto mapa: Santuario de las reliquias, con catorce objetos y siete mejoras acumulables: daño, protección, regeneración de vida, vida máxima, velocidad, energía y curación.
- Recogida y reaparición de objetos a los doce segundos calculadas por el servidor. Todas las acumulaciones se pierden al morir, incluidos suicidios; reanimar no las restaura. La siguiente partida reinicia los objetos y mejoras.
- Objetos visibles en el escenario y minimapa, acumulaciones en el HUD y ayuda F1 → Objetos. Los bots buscan y recogen reliquias.
- Protocolo 7 con objetos, mejoras y elementos del lanzamiento activo. 82 pruebas de núcleo, diagnóstico y red; comprobaciones Godot y del paquete Windows de los cuatro mapas, cifras F1, escala de UI y casillas.

## 0.8.0

- Tercer mapa seleccionable: Coliseo de la ascensión, con suelo en cruz, cuatro pilares y tres puntos de control.
- Solo en Ascensión, cada baja de otro mago suma 10 a la vida máxima y a la actual si el autor sigue vivo. Incluye bots, fuego amigo y bajas póstumas; el suicidio no da bonus y una baja póstuma no reanima al autor.
- La vida ganada se conserva al morir y al ser reanimado. Curación propia, rayos, regeneración y minas de vida respetan la capacidad individual.
- Cada muerte suma 0,5 segundos al tiempo base del modo hasta 10 segundos. Primera muerte: 3 segundos en duelo y 4,5 en arena/guerra. La siguiente partida reinicia vida, bajas, muertes y penalización.
- HUD, marcador, ayuda y selección de salas muestran las reglas y la vida acumulada. Reaparición con décimas en el nuevo mapa.
- Protocolo 6: actualizar cliente y servidor juntos. Pruebas de geometría, bajas, curación, reanimación, aislamiento de mapas, tiempos reales de reaparición y replicación de salas.

## 0.7.0

- Selector de Arena clásica y Ciudadela de los pasillos en práctica y al crear salas. El mapa elegido se replica al conectar.
- Ciudadela no cuadrada: tres salas de 22×64 y dos pasillos de 22×32, con 5.632 unidades² de suelo frente a las 1.408 de la arena clásica.
- En guerra de puntos, Ciudadela exige controlar los tres puntos sin disputa durante 15 segundos consecutivos. Disputa o neutralización reinicia el contador; las bajas y los tickets no deciden esta partida.
- HUD con puntos por equipo y tiempo de control, minimapa con la silueta real y cámara que sigue al jugador por todo el escenario.
- Movimiento al cursor y bots con rutas por pasillos. Límites compartidos bloquean movimiento, rayos, rocas y traslación.
- Protocolo 5: cliente y servidor deben actualizarse juntos. Pruebas de superficie, geometría, rutas, victoria a 900 ticks, interrupción, reinicio, red y cambios de mapa en Godot.

## 0.6.1

- El bastón respeta la configuración de fuego amigo: protege aliados cuando está desactivada y mantiene el daño a rivales.
- El límite de salud del saludo solo se ocupa durante la negociación; más de 32 jugadores pueden conectarse a salas con plazas.
- Las pruebas smoke rechazan excepciones de motor aunque se haya impreso un marcador de éxito y la CI conserva los logs de Windows cuando falla.
- 55 pruebas de reglas/diagnóstico/red, seis modos smoke del cliente y comprobación negativa de recursos ausentes.

## 0.6.0

- Rocas imbuidas con splash y caída por distancia; fuego en el suelo, hielo y charcos con duración finita. Tierra pura y fragmentos de hielo conservan impacto directo.
- Escudo de vida con 38/76 puntos de regeneración por segundo, burbuja verde y tasa en el HUD. Termina al romperse, expirar o sustituirse.
- Minas ofensivas activadas por enemigos; minas de vida activadas por aliados heridos, incluido el dueño. Radios separados de activación y explosión/curación, demora de armado y caducidad.
- Rayos resueltos después del movimiento: unión de componentes, bisectriz o desvío perpendicular en choques frontales, reflexión y refracción elemental con máximo dos rebotes.
- Catálogo F1 con 120 combinaciones y 360 demostraciones, controles de reproducción y escenas de interacciones. Simulaciones aisladas de la partida.
- Círculos de captura con relleno progresivo por equipo, porcentaje, disputa y progreso en el minimapa.
- Protocolo 4, validación de nuevos campos y copia independiente del estado. Cliente y servidor deben actualizarse juntos.
- 53 pruebas con servidor protegido y comprobación del catálogo en Godot y en el paquete Windows.

## 0.5.0

- Arte original para los cinco magos: atlas transparentes, cuatro orientaciones, pasos y poses de lanzamiento.
- Tela por equipo mediante shader; colores y siluetas de especialidad conservados. Anclajes de pies por fotograma y cinco texturas compartidas.
- Barras y nombres alineados con la cámara sobre el personaje; escudo adaptado, sombras del HUD eliminadas y destello de daño.
- Carga de atlas registrada en logs. Nueva comprobación de sprites en Linux y Windows exportado, con 120 combinaciones y estados de combate.
- Capturas reales, documentación y prompts del arte. Ejecutable, compilación de doble clic y servidor incluidos; protocolo 3 compatible con v0.4.0.

## 0.4.0

- `Compilar-Juego.cmd`: configura las herramientas, compila cliente y servidor y sustituye la carpeta `Juego/` tras validar el paquete. Herramientas reutilizadas, registro y conservación del juego anterior ante errores.
- Instrucciones de victoria en F1, salas, HUD y documentación, con límites reales de la simulación.
- Tipos equilibrado, piromante, criomante, tempestario y geomante. +25 % de daño de afinidad y +25 % de daño recibido del contrario, por componente, también en quemaduras.
- Selección recordada en el launcher, validada al entrar y conservada al reaparecer. Sombrero, gema y anillo distinguen las afinidades.
- Efectos de lanzamientos, estelas, ondas, impactos, explosiones, rotura de escudo y estados. Reutilización de nodos; máximos de 384 partículas y 32 ondas. Impactos continuos limitados en la simulación.
- Protocolo 3: sincroniza tipo, límite de bajas y tickets iniciales; actualizar cliente y servidor juntos.
- 43 pruebas de núcleo/diagnóstico/red. CI valida compilación automática y reemplazo en Windows PowerShell 5.1, Docker y cinco modos del ejecutable.

## 0.3.1

- Paleta de elementos y equipos con más contraste, paneles opacos y textos con contorno. Cada elemento tiene símbolo, tecla, nombre y borde de color.
- Combinaciones conservan el color de su forma principal; el suelo y la iluminación usan tonos más neutros para que destaquen magos y hechizos.
- Vida verde, escudo amarillo y energía violeta, con cantidades legibles dentro de sus barras.
- El escudo utiliza su capacidad real: 420 normal, 180 elemental y 650 de tierra. Muestra puntos restantes y segundos de duración; queda inactivo al agotarse o caducar.
- Barra y cantidad de escudo sobre cada mago, junto a la vida; indicadores de equipos en clasificación, marcador y minimapa.
- El estado del servidor transmite capacidad/duración, incluidas absorción de daño, reaplicación, muerte y reaparición. Campos adicionales compatibles con protocolo v2.
- 35 pruebas con servidor protegido y comprobación de seis estados del HUD en Linux y en el ejecutable Windows.

## 0.3.0

- Logs JSONL de cliente, servidor y proceso alojado: fecha UTC, versión, sesión, conexión, sala, jugador, tick y traza de excepción.
- Captura de errores/avisos Godot y excepciones de proceso; métricas periódicas y fallos de conexión/salas/simulación.
- Exportar diagnóstico desde launcher y menú ESC, con ZIP de logs y contexto. Rotación de 2 MiB, retención de cinco archivos por componente, ocultación de contraseñas y continuidad ante fallos del disco.
- Logs de Docker persistentes en volumen; el arranque local no trunca el registro anterior.
- Corrección del cierre simultáneo con envíos y del saludo pendiente; las entradas se copian para evitar cambios por referencias compartidas.
- Entradas inválidas no consumen secuencias; snapshots con recetas independientes y validación antes del renderer.
- Respuestas HTTP/WS acotadas; IPv6 y rutas por defecto corregidas. JSON malformado devuelve 400 y formato incorrecto 415.
- Identidad única del servidor alojado, doble inicio bloqueado, arranque cancelable y aviso de salida inesperada. Configuración del servidor valida modos y acepta booleanos sin distinguir mayúsculas.
- Bots pausados en salas vacías; recetas reutilizadas y búsqueda de objetivos sin ordenar. HUD y clasificación reutilizan objetos; materiales y limpieza no asignan colecciones cada fotograma. Audio detenido y liberado al cerrar.
- 33 pruebas de núcleo, diagnóstico y red; dos pruebas de paquete Windows. CI comprueba Docker persistente y la exportación real de diagnóstico en Linux y Windows.

## 0.2.0

- Paquete Windows x64 autónomo: doble clic en `ArcanaArena.exe`, sin instalar herramientas.
- Pestaña Alojar: servidor incluido, nombre, puertos, contraseña, inicio, parada y registro.
- Configuración de reglas LAN privadas mediante botón y permiso UAC de Windows.
- Directorio, selección y creación de salas con modo, aforo, bots, fuego amigo y victoria.
- Salas independientes, contraseñas propias y limpieza después de 15 minutos vacías.
- Protocolo v2; cliente y servidor deben actualizarse juntos.
- Pruebas de aislamiento, aforo, contraseñas y paquete autónomo en Windows.
- Exportación reproducible, prueba del juego distribuido y publicación automática del ZIP solo tras validación.

## 0.1.0

Primera versión del proyecto:

- Cliente Godot .NET en C# y escenario 3D procedural con recursos propios.
- Ocho elementos, tres posiciones de selección y resolución de combinaciones.
- Rayos, chorros, electricidad, roca cargada, hielo, vapor, escudos, muros y minas.
- Curación, guardas, estados elementales, colisiones y fuego amigo configurable.
- Duelo, arena y guerra por puntos de control. Reaparición y reinicio de partida.
- Bots, práctica local, HUD, minimapa, clasificación y ayuda integrada.
- Launcher con conexión IP/dominio, búsqueda UDP LAN, contraseña y preferencia de equipo.
- Servidor ASP.NET Core autoritativo, colas acotadas y protocolo JSON versionado.
- Dockerfile multietapa, Compose normal y variante de red host para LAN en Linux.
- Documentación de ejecución, red, arquitectura y exportación.
- Pruebas de combate y conexiones reales; workflow de compilación y comprobación del contenedor.

Los recursos visuales, el mapa y los valores de balance son una primera aproximación. Esta versión no incorpora las estadísticas de equipo y tablas exactas del original ni los servicios de cuentas, matchmaking y persistencia.
