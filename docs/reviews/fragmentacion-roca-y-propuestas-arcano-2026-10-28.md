# Fragmentación de roca y opciones para Arcano

Rama `pruebas/supervivencia-procedural-autoguardado`, después de la corrección de nombres de atributos. El usuario solicita implementar la fragmentación de roca y proponer opciones de afijos para Arcano. Esta petición no solicita APK, tag ni ejecución de compilaciones.

## Roca: implementación

El afijo `RockFragmentation` se añade al final del enum, sin alterar los identificadores anteriores. Requiere catálogo 10; el protocolo pasa a 42 para que un cliente antiguo no reciba un tipo de afijo desconocido. Es uno de los trece afijos de combate de Diamante/Mítico, salvo el anillo mágico. Repetirlo aplica solo el rango mayor y conserva la probabilidad de afijos económicos del anillo de oro.

| Parámetro | Valor inicial |
|---|---|
| Diamante | 4 fragmentos |
| Mítico | 6 fragmentos |
| Daño de impacto por fragmento | 20 % de la potencia de impacto de la roca lanzada |
| Dispersión | Radial, hasta 6 m; sin buscar objetivos |
| Colisiones | Enemigos, obstáculos y barreras; los escudos básicos mantienen la reflexión |
| Áreas secundarias | Fuego, Frío o Agua según la mezcla; aparecen al chocar o al terminar el recorrido |
| Radio y duración del área de fragmento | 1 m y 2 s |
| Daño directo del área de fragmento | 20 % del suelo normal de la misma mezcla |
| Estados | Conserva los de la receta original |
| Recursión | Una división; los fragmentos no generan otros fragmentos |

La roca principal conserva su golpe y su área anteriores. Tierra pura solo produce fragmentos físicos; Tierra + Arcano conserva la explosión sin inventar un suelo persistente de Arcano. Se reutilizan las mallas pequeñas y los materiales de roca existentes: tamaño y mezcla ya viajan en el snapshot. No necesita una segunda simulación del cliente ni nuevos recursos gráficos.

La tirada se captura al lanzar y se guarda en la roca. Los fragmentos guardan su identidad de fragmento; las áreas guardan su escala independiente, sin reinterpretar el campo Power de guardados anteriores. Las propiedades nuevas tienen valores neutros al leer puntos antiguos. La recuperación valida rangos, recetas, radios, duración y velocidad de los fragmentos.

Los fragmentos se incorporan después de iterar los proyectiles, evitando modificar la colección durante el recorrido. Se conservan los límites globales de 160 proyectiles y 40 áreas. La dispersión es determinista y no consume azar de terreno, botín o horda.

## Arcano: propuestas pendientes de elección

El rayo continúa recto, sin encadenar enemigos. La penetración sigue su trayectoria y los escudos conservan sus interacciones. Ya existen penetración de haces y Remate arcano; estas propuestas aportan funciones adicionales.

| Afijo propuesto | Función |
|---|---|
| Marca arcana | Mantener el haz sobre un enemigo carga una marca que provoca una explosión pequeña en su posición. Aporta daño de área sin desviar el rayo. |
| Eco arcano | Un segundo pulso más débil repite la trayectoria original poco después. Premia mantener alineados a los enemigos y permite esquivar el segundo golpe. |
| Fractura de escudos | Aumenta el daño del componente Arcano contra escudos, conservando sus reflejos. Especializa el haz contra enemigos protegidos. |
| Haz ensanchado | Aumenta el grosor del haz recto para facilitar la puntería, especialmente con joystick. La penetración mantiene sus reglas. |
| Sifón arcano | Recupera energía a partir del daño real causado a enemigos, con un máximo por segundo. Favorece mantener el ataque. |

Recomendación de diseño: Marca arcana para dar al haz una función de control de grupos y Haz ensanchado para mejorar su uso en móvil. Los valores de activación, daño, anchura y recuperación requieren elegir primero el afijo. Estas propuestas no se implementan junto a la roca.

## Verificación

Se añaden cinco regresiones de motor: cantidades, daño y una sola división; mezclas y áreas; reflexión y bloqueo; límites globales; guardado durante el vuelo, durante los fragmentos y con áreas, incluyendo lectura anterior y rechazo de rangos imposibles. Se amplían las pruebas existentes de catálogo, acumulación y color del nombre.

La revisión estática comprueba sintaxis C#, metadatos de exportación, referencias, integridad del diff y ausencia de cambios en los activadores de publicación. Las nuevas pruebas de ejecución y la inspección Godot quedan pendientes de la siguiente compilación expresamente solicitada, conforme a AGENTS.md. Los valores son iniciales; aún no hay medición de equilibrio de esta mecánica en partidas.
