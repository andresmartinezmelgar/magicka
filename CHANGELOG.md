# Cambios

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
