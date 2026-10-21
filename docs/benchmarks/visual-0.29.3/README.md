# Comparación visual de 0.29.3

Rama `codex/visual-performance`, basada en `82b2e01605166e4bb1778e2720b0f884812b31d0` (0.29.2). La referencia conserva el renderizador de ese commit; únicamente se le añadió la misma separación de tiempos del perfilador para comparar `sceneCpuMs` y `renderUpdateCpuMs`.

Entorno local del 21-10-2026: Godot 4.5.2 .NET, runtime 8.0.32, Linux, OpenGL Compatibility y llvmpipe LLVM 20.1.2. Viewport real de 1600 × 720, interfaz móvil, semilla 271, cinco segundos de calentamiento y veinte de muestras por ejecución. `DOTNET_PROCESSOR_COUNT=2` y `LP_NUM_THREADS=2`. No es un teléfono ni una GPU física.

`before-1`/`after-1` y `before-2`/`after-2` son dos comparaciones válidas de la escena sintética de carga máxima. La segunda utiliza directorios de proyecto independientes; se comprueba la versión cargada. No se incluyen repeticiones en las que no coincidía la compilación esperada. La tabla usa la media de las dos ejecuciones de cada variante, sin combinar sus percentiles.

| Escena de carga máxima | Referencia | Optimizada |
|---|---:|---:|
| Llamadas de dibujo por fotograma | 3394 | 2739 |
| Nodos | 4879 | 3860 |
| Primitivas por fotograma | 475128 | 434183 |
| Actualización visual en CPU | 8,31 ms | 4,32 ms |
| Tiempo total por fotograma | 164,01 ms | 149,48 ms |
| Pico de partículas / ondas | 320 / 24 | 320 / 24 |

Se reducen un 19,3 % las llamadas de dibujo, un 20,9 % los nodos, un 8,6 % las primitivas y un 48,0 % el tiempo de actualización visual en este entorno. El tiempo total baja un 8,9 %. Las figuras, colores, radios, cantidades y presupuestos permanecen; se agrupan instancias y se eliminan subdivisiones de cilindros rectos que no cambian su superficie.

Las cifras no garantizan una mejora equivalente de FPS en un móvil físico. Los JSON conservan tiempos, percentiles, memoria, resolución y procedencia. En este backend el dibujo se hace por software y compite con la simulación por CPU.

`survival-before` y `survival-after` contienen la prueba integrada con cuatro defensores, equipo y portales. Su población y pasos simulados varían: 181/177 minions finales y 1152/1197 pasos. No son dos estados idénticos. La preparación visual baja de 1,43 a 1,17 ms, mientras que la simulación/creación del estado ocupa 139,09/138,53 ms de tiempo transcurrido por fotograma. Los FPS son 5,60/5,68 y el percentil 95 total empeora de 236,53 a 255,77 ms. No se afirma una mejora del rendimiento integral de Supervivencia a partir de esa pareja: la simulación domina esta prueba.

La comprobación funcional ejecuta efectos, enlaces, 120 combinaciones y presupuestos. La nueva prueba de buffers usa un viewport OpenGL real, verifica la lectura de transformaciones/datos de MultiMesh y los extremos/radios de tres orientaciones, vaciado y reutilización. El renderer Dummy de `--headless` no ofrece esa lectura; allí se comprueban cantidades, colores, caducidad y reutilización. CI exige además la prueba con viewport real en escritorio y móvil.
