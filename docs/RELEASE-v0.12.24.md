# v0.12.24 — Relaciones elementales y escudos propios

Las relaciones se definen en una única tabla del núcleo, compartida por el servidor y la práctica. La tabla también aparece en **Ayuda → Elementos**.

| Elemento | Supera a | Vulnerable a |
| --- | --- | --- |
| Agua | Fuego | Frío |
| Vida | Tierra | Arcano |
| Escudo | Arcano | Rayo |
| Frío | Agua | Fuego |
| Rayo | Escudo | Tierra |
| Arcano | Vida | Escudo |
| Tierra | Rayo | Vida |
| Fuego | Frío | Agua |

Contra una protección, cada componente del ataque causa **×1,5** si supera al elemento defensor, **×0,5** si el defensor lo supera y **×0,18** contra el mismo elemento. Una defensa mixta promedia sus relaciones; cada componente ofensivo conserva su propio daño. En una barrera imbuida, Escudo aporta la forma y sus otros elementos definen la defensa. El escudo básico usa Escudo como elemento defensor: Rayo lo desgasta más y Arcano menos. Los bonus y debilidades de clase y los objetos se aplican además de estas relaciones.

Agua pura sigue mojando y Vida sigue curando. Pueden desgastar protecciones enemigas con 35 puntos de presión por elemento, antes de las relaciones y los modificadores. Esa presión no causa daño a la salud al romper un escudo. Los rayos conservan la reducción de presión del 80 % al reflejarse. Curar a un aliado conserva su protección. Las barreras mantienen sus tasas de desgaste propias de cada forma de hechizo y usan la misma tabla elemental para calcular el resultado. Las barreras de agua también aplican mojado y presión al contacto.

Vapor e hielo reparten su daño entre sus elementos reales, y las repeticiones aumentan el componente repetido. Agua + Fuego suma 80 + 80 = 160; añadir otro Agua o Fuego eleva el total a 240. Agua + Frío suma 34 + 34 = 68; añadir otro Agua o Frío eleva el total a 102. El bonus del piromante solo aumenta el fuego del vapor. La ayuda muestra las cifras calculadas con las mismas funciones del combate. Las recetas construidas directamente aplican las cancelaciones y el límite de tres elementos del teclado.

El escudo básico frontal o en anillo permite atravesar los hechizos de **su propietario**. La excepción se comprueba al emitir, buscar visibilidad, trazar rayos y cadenas y colisionar proyectiles; también permite los pulsos y el splash propios. Un hechizo de otro jugador, incluso aliado, sigue bloqueado. Un rayo combinado con componentes de otros propietarios tampoco hereda el permiso. Las barreras imbuidas bloquean los hechizos propios y todas las barreras siguen bloqueando el movimiento.

Cliente y servidor deben actualizarse juntos: **protocolo 11**, versión **0.12.24**, código Android **12024**.

Las regresiones cubren la tabla completa, las 512 selecciones de tres elementos, fusiones y repeticiones, guardas mixtas, desgaste de escudos y barreras, curación, rotura sin daño de utilidad, paso de los cinco tipos de hechizo por el escudo propio, bloqueo de escudos ajenos, emisión cercana y rayos combinados. La validación se completa con el servidor real, el laboratorio y las combinaciones de la ayuda en Godot.

Validación local: 160 pruebas de reglas y transporte, 21 del servidor real y nueve comprobaciones Godot sin pantalla. Compilación de núcleo, servidor y pruebas; cliente en Debug y Release. Sincronización de versiones y seis pruebas de los scripts de versión, con dos comprobaciones PowerShell omitidas porque no está instalado aquí.

Con OpenGL se verificaron la carga móvil y su cancelación y se revisaron capturas reales de la tabla elemental, el vapor QFF y el hielo QQR. La prueba completa de las diez escenas con OpenGL por software superó su límite de 100 segundos; las diez escenas pasan en la comprobación sin pantalla. La revisión gráfica se realiza con Mesa/llvmpipe y no sustituye una medición en teléfonos físicos. No se ha generado aquí una APK ni un ejecutable Windows.
