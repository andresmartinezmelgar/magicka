# Equipamiento compartido de aliados: análisis sin implementación

La base ya existe: cada bot tiene `DefenseProgress`, inventario temporal, cuatro ranuras, bonificaciones calculadas y serialización local. `GrantDefenseItem` equipa automáticamente una ranura vacía. Los drops de equipo ya se sortean por defensor y tienen propietario; un bot puede recoger los que le pertenecen. `AutoShopDefenseBot` solo compra auras: no compara piezas por afinidad ni copia las que recoge un humano.

El cambio solicitado consiste en que recoger una pieza humana genere una copia temporal para sus bots, y en que cada bot compare esa copia con lo equipado. Esta funcionalidad no se ha implementado en 0.28.0.

| Trabajo | Alcance | Estimación |
| --- | --- | ---: |
| Reparto | Vincular bots a un humano de forma estable, decidir qué ocurre en cooperativo y al reconectar, copiar piezas con identidad propia y evitar entregas duplicadas | 2–3 h |
| Elección | Valorar daño de afinidad, defensa, velocidad, alcance, conjuntos 2/4 y afijos; conservar la pieza actual si el cambio no mejora el conjunto | 2–4 h |
| Inventario y guardado | Política con 24 piezas, sustitución o descarte, inspección del equipo, guardado y continuidad de propietario | 1–2 h |
| Validación y equilibrio | Una y cuatro personas, tres bots, cambios de líder, inventario lleno, desconexión y guardado; revisar el aumento de poder del grupo | 3–5 h |
| **Total** | Estimación de trabajo de desarrollo y pruebas sobre el código actual | **8–14 h** |

El coste monetario depende de la tarifa: **8–14 horas × tarifa por hora**. Es una estimación, no un presupuesto cerrado.

La comparación puede ejecutarse únicamente al recibir equipo o cambiar de afinidad. Con tres bots y 24 piezas por inventario, no requiere revisar el equipo cada fotograma. El coste de CPU sería pequeño frente al combate; el mayor efecto sería de equilibrio, porque una pieza recogida beneficiaría a varios personajes.

Para cooperativo hace falta una regla de propiedad estable. Seguir al humano más cercano, como hace la escolta actual, no equivale a pertenecerle: usar esa proximidad para entregar equipo permitiría cambiar de receptor al moverse. La asignación debe sobrevivir a reconexiones y guardados antes de activar el reparto.
