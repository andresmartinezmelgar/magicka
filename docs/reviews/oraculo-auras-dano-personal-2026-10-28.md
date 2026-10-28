# Oráculo ocasional, auras y daño personal

Cambios en `pruebas/supervivencia-procedural-autoguardado`, sin compilar ni publicar.

## Supervivencia

- Oráculo desde dificultad 6: probabilidad del 3 % al 1 % por intento, límite natural de dos a uno e intervalo aleatorio de 90–150 s desde la aparición anterior. El intervalo no garantiza una aparición al terminar: se mantiene la tirada. Se guarda y continúa sin reiniciarlo.
- Vida base de 1100 a 1400 y cristales de 160 a 200 de daño base, multiplicados por la fuerza correspondiente. Capacidad, reparación, radio y duración del aura de escudos conservados.
- Aura arcana: `24 × nivel` de daño/s base a enemigos visibles, con absorción normal de daño por sus escudos.
- Aura de agua: `12 × nivel` de daño/s base, aplica Mojado hasta dos segundos después del último pulso, modificable por equipo, y apaga quemaduras enemigas. Solo afecta a minions visibles; respeta la inmunidad elemental del prisma y las protecciones contra Mojado. El lanzador y los aliados quedan fuera de la selección.
- Cinco auras con diez niveles, precio anterior y crecimiento del radio anterior. Los índices Fuego=0, Rayo=1 y Regeneración=2 se conservan; Arcano=3 y Agua=4. Berserker mantiene una visual separada.
- Dos patrones de geometría propios, colores elementales, una superficie por visual y mallas dentro del presupuesto de triángulos existente. La precarga y las escenas de verificación incluyen las cinco auras.

## Todos los modos

- Un acumulador personal por mago, independiente de clase, equipo y las estadísticas agregadas de humanos/bots. No requiere Google ni permisos del visor global.
- Daño efectivo a vida y a escudos, sin exceso, fuego amigo ni daño propio. Reparte las mezclas por sus componentes reales; incluye el daño efectivo a barreras enemigas, ponderado por resistencias y equipo. Los daños pasivos y reflejados mantienen su autor.
- Snapshots independientes con nueve categorías: ocho elementos y daño sin elemento. Validación de longitudes, valores finitos y límites; protocolo 43. Se añaden ArcaneAura/WaterAura al final del enum de ataques sin cambiar identificadores anteriores.
- Tabla, total y gráfica por elemento al morir y terminar, accesibles también desde la clasificación. Los datos y la gráfica se actualizan sin reconstruir el nodo ni perder el desplazamiento horizontal.
- Reaparecer conserva el acumulado; una partida nueva lo borra. El guardado y la reconexión de Supervivencia conservan los datos. Un guardado anterior empieza este contador a cero, ya que no registraba daño personal.
- La pantalla automática de muerte no activa la pausa de un humano, permitiendo avanzar la reaparición. La consulta manual estando vivo mantiene pausa o asistencia según el número de humanos.

## Validación

Se añaden ocho regresiones del motor para daño efectivo en los cuatro modos, mezcla y separación de autores, barreras, auras, inmunidades, compras, límite e intervalo del oráculo, JSON, datos inválidos y continuidad del guardado anterior. Se amplía la navegación visual para 640×450, 960×450 y 1440×900, comprobando apertura, cierre, reaparición y resumen final; las verificaciones de mallas y precarga incluyen las cinco auras.

Revisión estática de sintaxis, referencias, diferencias y versiones realizada. **La compilación, la ejecución de las regresiones y la comprobación visual en Godot quedan pendientes de una petición de compilación.** No se ha generado APK ni tag. Las acciones de la rama son manuales y no se modifican sus disparadores.
