# Arcana Arena v0.12.20

## Laboratorio de hechizos

En **Práctica**, pulsa **Entrar en laboratorio de hechizos** o selecciona **Laboratorio de hechizos** como mapa. No requiere servidor. Las partidas contra bots siguen disponibles.

El mapa tiene un único círculo azul de reaparición del jugador. No hay puntos de control, puntuación, tickets, objetos ni condiciones de victoria. Al morir, vuelves a ese círculo tras un segundo.

La zona de estados contiene catorce objetivos enemigos inmóviles: sin estados, mojado, quemado, enfriado, congelado, celeridad, escudo E, armadura ED, regeneración EW y guardas QE, ER, EA, ES y EF. Reciben daño y curación normales; pueden morir y reaparecen en su estación con su estado y protección iniciales. El paso del tiempo no elimina el estado de exposición; agua, fuego y frío pueden neutralizarlo. El escudo pierde resistencia y puede romperse. **Restablecer objetivo** recupera vida, estado y escudo y reinicia su medición.

La zona de barreras expone todas las 32 recetas del catálogo que contienen escudo, incluidas minas ofensivas y minas de vida. Cada estación muestra tipo, teclas y elementos. Usa las mismas barreras y minas de una partida: resistencia, daño por contacto, refracción, curación y activación normales. Permanecen mientras no se destruyan o activen y se reconstruyen dos segundos después; una pared espera si hay un mago sobre su segmento. Las minas de vida pertenecen al jugador para que pueda probar su curación.

El mago lanzador dispara siempre hacia la derecha del mapa, por la calle señalada. El selector permite elegir cualquier combinación frontal sin barrera, además del empuje. Las barreras se prueban en sus propias estaciones. Rayos, conos y cadenas mantienen el canal, incluida la progresión de carga del rayo. Las rocas se cargan hasta su potencia máxima y se liberan cada dos segundos; los fragmentos también se lanzan cada dos segundos. Puedes pausar al lanzador o cambiar receta, cancelando canales, carga y proyectiles anteriores.

Apunta a un objetivo para consultar su vida y escudo y los totales de daño a vida, daño absorbido y curación efectiva. La medición usa las cantidades aplicadas por el combate; no confunde daño y curación simultáneos ni cuenta sobrecuración o reaparición como curación. **Vida al 40 %** prepara al jugador para entrar en la trayectoria de un hechizo curativo. Las mediciones se conservan al morir y se reinician mediante el botón de restablecer.

El laboratorio es local y no se anuncia ni acepta como sala multijugador. El protocolo de red permanece en 9. Windows y Android siguen tomando su versión de `VERSION`.

## Validación

Comprobaciones locales: **141 pruebas de reglas y 20 pruebas de red correctas**, más las verificaciones Godot de laboratorio, mapas, HUD y combinaciones. Los paquetes Windows y Android no se han exportado localmente.

- Trece pruebas específicas del laboratorio: estaciones y recetas completas, estados prolongados e interacciones, muerte y reaparición fija, absorción, curación simultánea, regeneración, minas, reconstrucción de barreras y ocupación, canales continuos, cadencia exacta de 120 ticks, todas las recetas del lanzador y exclusión de salas online.
- Compilación C# de núcleo, servidor y pruebas en Release y del cliente en Release/Debug con los generadores oficiales de Godot 4.5.2.
- Comprobación Godot `--practice-smoke` de las estaciones, único respawn, ausencia de puntos, selector de combinación, cadencia y curación efectiva. Incorporada a CI Linux y al ejecutable Windows.
- No se han exportado ejecutables Windows ni APK en este entorno Linux. La publicación de binarios y la ejecución de CI remoto son comprobaciones independientes del commit de código.
