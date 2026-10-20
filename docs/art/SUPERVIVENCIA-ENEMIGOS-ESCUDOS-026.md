# Supervivencia — oleadas élite, escudos y conos (0.26.0)

Rama de desarrollo `feature/supervivencia-enemigos-escalado`, independiente de `main`.

## Errores corregidos

1. **Conos de fuego, agua y frío:** mismo ángulo calculado a partir de `SpellBalance.ChannelConeDot` en servidor y renderizador. El contorno enseña todo el sector de efecto, incluidas bonificaciones de alcance. Se elimina el volumen duplicado (el cliente tiene dos pasadas de render).
2. **Hechizos de magos enemigos:** el antiguo mago invasor alterna ataques de fuego, descargas eléctricas, chorros de fuego y rayos arcanos. Nunca selecciona el rayo eléctrico si está mojado. Los magos aliados conservan la nueva rotación y la prioridad de curación.
3. **Proyectiles enemigos bloqueados por aliados:** en Supervivencia se ignoran los contactos de roca/flecha con miembros del mismo equipo; conservan muros, hitboxes del rival, física y daños originales.
4. **Regresiones de pruebas:** la aparición de refuerzos del coloso ya no se interpreta como múltiples colosos; las simulaciones usan el rango válido de invulnerabilidad.

## Nuevas familias de élite

Se añaden sin cambiar los identificadores existentes de la red:
- **Conjurador ígneo**: rocas arcanas y de fuego con daño de zona y suelo incendiado.
- **Tejedor glacial**: rocas de frío con suelo helado, fragmentos de hielo y cono de frío.
- **Guardián rúnico**: escudo de tierra temporal recargable, vida elevada y golpe de área telegráfico.

Los arqueros y magos mantienen distancias y flanquean lateralmente. Los brutos y detonadores conservan sus advertencias. **El comandante** (antes capitán), el coloso y el guardián disponen de ataques circulares con advertencia anticipada; siguen usando daños y protección del motor existentes. El comandante y el coloso potencian otros enemigos cercanos.

## Horda del comandante y FPS

Al aparecer un comandante o coloso se inicia un refuerzo masivo: hasta **84 / 112** enemigos adicionales, respectivamente, según cupo real. Su llegada se fracciona en paquetes de **máximo 3 cada 0,15 s** para evitar picos de CPU/GC. El techo total continúa siendo **192 enemigos**; el límite de población de la sala es siempre el mínimo del presupuesto del anfitrión y de los FPS notificados por los participantes. Con 20 FPS la sala cae al límite de 48 y con 15 FPS a 32; puede ampliar gradual si los 5 s de telemetría indican estabilidad. El bonus temporal de población máximo es de 112; fuera del refuerzo se recupera el límite normal. No se añaden recompensas por retirar minions por motivos de rendimiento.

## Escudos por nivel

Cada nivel añade **1,25 %** a la capacidad defensiva base, con un máximo de **+35 %**. Se aplica sobre la bonificación de equipamiento; al recalcular un escudo ya activo se conservan proporcionalmente sus daños, no se rellena de golpe ni se acumula de manera repetida. Afecta solo al modo Supervivencia/Defensa; no altera PvP.

## Cobertura

Regresiones del motor para variedad elemental, proyectiles de área, centinela/guardián, advertencias, límites FPS, horda, progreso de escudos, snapshots y IDs de red. GitHub Actions debe aprobar la compilación y pruebas, los render tests de móvil y Windows y las exportaciones antes de usar la publicación preliminar.
