# Tipos de mago

Elige el tipo en el launcher antes de entrar en una sala o comenzar práctica. Se recuerda al cerrar el juego y se conserva al reaparecer y al reiniciar la partida. Todos tienen 1000 puntos de vida, la misma energía y acceso a los ocho elementos.

| Tipo | Ventaja | Debilidad |
|---|---|---|
| Equilibrado | Daño normal. | Ninguna adicional. |
| Piromante | **+25 %** daño de fuego. | Recibe **+25 %** daño de frío. |
| Criomante | **+25 %** daño de frío. | Recibe **+25 %** daño de fuego. |
| Tempestario | **+25 %** daño de rayo. | Recibe **+25 %** daño de tierra. |
| Geomante | **+25 %** daño de tierra. | Recibe **+25 %** daño de rayo. |

Los bonus afectan al componente de su elemento, también en combinaciones, rocas, campos, meteoros y quemaduras. No aumentan los otros componentes del hechizo, la curación, el golpe de bastón ni los puntos de escudo. El vapor usa daño de fuego y el hielo usa daño de frío.

Ejemplo: un hechizo con fuego y arcano aumenta **solo el fuego** si lo lanza un piromante. Si lo recibe un criomante, ese componente combina el bonus del atacante y la debilidad del defensor: `1,25 × 1,25 = 1,5625`, un **56,25 %** más que el daño normal. El componente arcano conserva su daño.

Las guardas siguen reduciendo el daño de su elemento. La vulnerabilidad aumenta el daño que recibe el escudo antes de afectar a la vida. Agua y vida mantienen sus funciones de estado y curación; sus combinaciones y cancelaciones no cambian.

El sombrero, la gema del bastón y un anillo exterior identifican la afinidad. La túnica y el anillo interior identifican el equipo. La clase aparece en el HUD y sobre el mago; **F1 → Tipos de mago** muestra sus características.

La selección se valida y el daño se calcula en el servidor. Cambiar el tipo requiere salir y volver a entrar. Esta versión usa **protocolo 4**: actualizar cliente y servidor juntos a v0.6.1. Los sprites de v0.5.0 conservan los colores de especialidad y cambian la tela según el equipo.
