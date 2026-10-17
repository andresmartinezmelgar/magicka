# Cambios

## 0.22.0 — Presión continua, equipo y ayuda directa

- Los marcadores de hielo y rocas permanecen a 7 m del mago tanto con ratón como con joystick, independientemente de la distancia del cursor.
- Supervivencia mantiene apariciones regulares de 0,32–0,48 s antes del escalado por dificultad y grupo. Prioriza los alrededores de los defensores; los enemigos a más de 60 m de todos pueden regresar fuera del radio seguro, conservando salud y estados. Se mantienen los límites de rendimiento, los avisos y las dos vidas.
- Retirado el botón redundante del laboratorio. El mapa de práctica sigue disponible en el selector.
- Nombres de atributos en la última columna de las tablas del inventario, inspección de magos y estadísticas de Supervivencia.
- Combinaciones integradas en Ayuda: ocho elementos, tres ranuras y tres formas de lanzamiento, con el efecto y sus cifras al lado; sin listas, buscador, segunda ventana ni escena 3D adicional en el menú. La ayuda durante el combate conserva la demostración automática.
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
