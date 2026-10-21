# Bestiario, reliquias y afijos — 0.27.0

El perfil incorpora un catálogo de los 15 enemigos de supervivencia. Cada entrada describe su comportamiento y muestra las bajas guardadas de ese tipo. Solo recibe crédito el autor del golpe final, incluida la quemadura atribuida a ese jugador.

El servidor acumula las bajas por cuenta y partida. Conserva una cola en disco, la vacía al recuperar el servicio de cuentas y guarda el último acumulado en users/{uid}/bestiaryRuns/{runId}. Una transacción añade únicamente la diferencia positiva a users/{uid}.bestiary. Los reintentos, las peticiones antiguas y las reconexiones no vuelven a contar lo ya confirmado. El bestiario no modifica oro, victorias, inventario ni premios.

Las partidas locales con perfil guardado conservan su registro pendiente cada 10 segundos, al salir, al reiniciar y al suspender la aplicación. Se sincronizan con el servidor HTTPS ya configurado para operaciones protegidas de la cuenta. Si no hay servidor de cuentas disponible, permanecen pendientes en el dispositivo y la pantalla lo indica. El inicio de sesión y la lectura de los contadores desde Firestore siguen siendo independientes del servidor de juego. Los invitados no registran bajas en una cuenta ajena al iniciar sesión después.

No se cambian las reglas de Firestore: el propietario ya puede leer su cabecera, y las escrituras de contadores permanecen en el SDK Admin del servidor. No hay migración obligatoria: los perfiles anteriores comienzan con un mapa vacío. Los contadores históricos anteriores a esta versión no se pueden reconstruir.

Las reliquias desaparecen del HUD y de la columna izquierda de supervivencia. Se consultan en Menú de partida → Reliquias recogidas y, en supervivencia, en Equipo / tienda → Reliquias. La ficha detalla las acumulaciones recogidas y sus efectos.

Los nuevos objetos Diamante y Míticos tienen un afijo fijo, separado de sus porcentajes:
- Penetración de haces ofensivos: Diamante 1–2 enemigos adicionales; Mítico 1–3. Escudos y paredes detienen la penetración.
- Relámpagos encadenados: Diamante 1 objetivo adicional; Mítico 1–2. Cada salto busca un enemigo nuevo a 5 m como máximo con línea de visión.
- Quemadura intensa: doble daño por segundo, sin duplicar impacto ni duración.
- Escudo de agua: crea un charco visible de 3 m durante 5 s. Moja y apaga las quemaduras de quienes entren, también del lanzador. Un solo charco por lanzador, respetando el límite global de campos.

Los afijos iguales usan el valor más alto del equipo: no se suman. Se muestran en las piezas, la tienda, la comparación, los totales y la inspección de otros jugadores. Las mejoras porcentuales conservan el afijo. Los objetos existentes conservan sus valores originales y no se vuelven a sortear. Generación de objetos 3; protocolo de juego 28.

Se integran las ramas de escudos, conos e IA y enemigos de supervivencia. La limpieza de ramas solo elimina las ramas identificadas para esta integración cuando sus commits están contenidos en main y pasan las verificaciones.
