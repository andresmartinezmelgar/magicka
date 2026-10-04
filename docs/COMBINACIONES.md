# Combinaciones, efectos e interacciones

Abrir **F1 → Combinaciones** desde el launcher o durante una partida. Hay **120 selecciones válidas** de uno, dos o tres elementos, sin duplicar permutaciones. Buscar por teclas (por ejemplo `EW`) o por elemento, elegir lanzamiento **Frontal**, **Sobre ti** o **Pulso**, y ver su demostración animada. El orden de las teclas no cambia la receta.

La demostración se repite, se puede pausar y reiniciar. Enseña el combate con personajes, proyectiles, campos, barras y efectos; usa las mismas reglas que el juego en un mundo separado. Consultarla no crea minas ni modifica la partida. En partidas online el servidor sigue avanzando mientras se consulta F1.

Grabaciones del juego: [efectos y minas](videos/combination-effects.mp4), [cruce de rayos](videos/beam-interactions.mp4), [refracción en escudos](videos/shield-refraction.mp4) y [captura progresiva](videos/point-capture.mp4). El catálogo integrado permite ver también las demás combinaciones.

## Cifras de daño en F1

Cada selección y modo muestra **DAÑO BASE** y, cuando corresponde, **CURACIÓN**. Las cifras suponen magos equilibrados, un objetivo seco sin escudo ni guarda y ninguna mejora de objeto. Afinidades, defensas y objetos modifican el resultado.

| Caso | Cifra base |
|---|---|
| `SSS` frontal | 345 daño/s. |
| `FFF` frontal | 174 daño/s y quemadura de 28 daño/s, hasta 4 s después del último golpe. |
| `AAA` frontal | 300 daño/s; 570 contra un objetivo mojado. |
| `DD` frontal | 220–500 por impacto, según la carga. |
| `QR` frontal | 40,8 por fragmento; lanza cuatro. |
| `ESF` frontal | 311,4 por mina y quemadura adicional. |
| `WWW` frontal / sobre ti | 420 vida/s / 435 de vida instantánea. |
| `EWW` sobre ti | 0 de daño; 76 vida/s y 180 de escudo durante 12 s. |

El pulso sin escudo indica daño por impacto. El pulso con escudo crea seis minas o barreras y presenta la cifra de una. El catálogo distingue daño de rocas, splash, suelo y quemadura; no suma como un único golpe todos los fragmentos o los efectos con duración. La curación se limita a la capacidad individual del objetivo.

## Formas distintas de lanzar

| Combinación | Lanzamiento | Comportamiento |
|---|---|---|
| `FFF`, `QF` | Mantener derecho | Chorro o vapor en cono; solo afecta delante del mago. |
| `SSS`, `WWW` | Mantener derecho | Rayo continuo sobre el primer objetivo; arcano daña, vida cura. |
| `AAA` | Mantener derecho | Descarga que salta hasta tres objetivos. |
| `QR`, `QRR` | Derecho | Abanico de fragmentos de hielo con impacto directo. |
| `DDD` | Cargar y soltar derecho | Roca con impacto directo, sin splash. |
| `DF`, `DS`, `DQ`, `DR` | Cargar y soltar derecho | Roca imbuida con splash al impacto. |
| `E` | Derecho | Barrera frontal. |
| `ES`, `ESF` | Derecho | Mina ofensiva. |
| `EW`, `EWW` | Derecho | Mina de vida. |
| `EW`, `EWW` | Central | Escudo regenerativo. |
| `ES`, `EW` | Mayús + derecho | Seis minas en anillo. |
| Elementos sin escudo | Mayús + derecho | Pulso de radio 4 m alrededor del mago. |

Los elementos incompatibles se anulan al seleccionarlos: fuego/frío, vida/arcano, agua/rayo, tierra/rayo y escudo/escudo. En una receta normal se admiten como máximo tres elementos.

## Splash y suelo

Solo determinadas rocas salpican. La combinación con arcano tiene radio **2,8 m**; con fuego, **2,5 m**; con agua, **2,7 m**; con frío, **2,1 m**. La prioridad para recetas con varios componentes es arcano, fuego, agua y frío. El objetivo directo recibe el golpe completo una vez; los vecinos reciben hasta el 65 % de la potencia, con caída lineal según la distancia. Los obstáculos y las barreras bloquean la salpicadura.

Fuego, frío y agua dejan respectivamente **suelo ardiente, suelo helado o un charco**, durante **4 s** y con un radio del 85 % del splash. El suelo aplica los componentes secundarios de forma continua, sin repetir el golpe de tierra. Puede afectar al propietario y respeta la configuración de fuego amigo.

El anillo del impacto muestra su radio real. Una roca pura y un fragmento de hielo no crean una explosión circular.

## Escudo regenerativo

**EW + central** crea un escudo de 180 puntos y recupera **38 puntos de vida/s**; **EWW + central**, **76 puntos/s**. Dura hasta **12 s**. No cura instantáneamente y no supera la vida máxima individual. La curación termina al agotarse, caducar o sustituirse el escudo, y se limpia al morir y reaparecer.

La burbuja se vuelve verde, aparecen partículas de recuperación y el HUD indica la tasa. La barra amarilla sigue mostrando la protección restante.

## Minas

Una mina frontal se coloca a **3,2 m**, se arma tras **0,4 s** y dura hasta **12 s**. El círculo pequeño, de **0,85 m**, marca la activación; el grande, de **2,4 m**, el alcance del efecto.

- **Ofensiva:** la activa un enemigo. Los aliados no la consumen, aunque pueden recibir su explosión si el fuego amigo está habilitado.
- **De vida:** la activa un aliado herido, incluido el propietario. Recupera **252 puntos por elemento de vida** a aliados cercanos, sin superar el máximo. Nunca cura ni se consume por enemigos y no añade daño ofensivo.

Una mina actúa una vez. Las barreras bloquean su efecto y desconectar elimina las minas del propietario.

## Rayos y escudos

Los canales rectos se resuelven juntos, después del movimiento. Si dos rayos se cruzan antes de llegar a un obstáculo, una barrera o un personaje, las entradas terminan en el cruce y aparece una salida con **la suma de sus componentes**. Cada aportación conserva su daño, curación, estados y afinidad de origen. Los componentes opuestos de rayos diferentes se aplican como efectos separados; no se someten a la cancelación del teclado.

La salida sigue la **bisectriz** de las direcciones. En un choque frontal se desvía **90°**, con una orientación determinista. Se combinan dos canales por encuentro y cada canal participa como máximo en un encuentro por paso. El alcance restante es el menor de ambos; el cruce no crea alcance adicional.

Un escudo o una barrera devuelve un rayo por **reflexión**. Recibe el 20 % de su presión y devuelve el 80 % de la intensidad; el tramo devuelto se atribuye al propietario del escudo. Una guarda elemental añade sus componentes con intensidad 0,35 y desvía **20°** más la salida, produciendo **refracción**. Cada rayo tiene como máximo dos rebotes. Un rayo de vida que alcanza a un aliado protegido lo cura directamente.

Las franjas de color muestran los componentes de los tramos combinados. F1 incluye escenas específicas de **Cruce de rayos** y **Escudo prismático**.

## Captura de puntos

El círculo se rellena por sectores según el progreso real: **azul** para el equipo azul y **rojo** para el rojo. Se muestran la letra del punto y el porcentaje; el minimapa también indica el progreso.

Cuando entra el rival, primero retira el progreso del equipo anterior, pasa por neutral y empieza a rellenar su color. Si hay jugadores de ambos equipos dentro, el punto indica **DISPUTADO** y la captura se detiene. El relleno interpola los snapshots del servidor para evitar saltos visuales.

## Compatibilidad y comprobaciones

La versión **v0.9.0 usa protocolo 7**. Actualizar cliente y servidor juntos. La red replica regeneración, minas armadas y sus radios, campos persistentes, componentes/intensidad de rayos, disputa de puntos, cuatro mapas, elementos del lanzamiento activo y mejoras/objetos del Santuario.

Las pruebas recorren las 360 demostraciones, verifican destinatarios de minas, regeneración, splash, caducidad, cruces, escudos y captura. `--combinations-smoke` comprueba diez escenas en el cliente y en la distribución Windows, incluida la representación de los nuevos eventos y los límites de recursos.
