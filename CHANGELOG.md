# Cambios

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
