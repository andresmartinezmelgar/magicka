# Rediseño de interfaz y sala PvP — trabajo pendiente

## Navegación
Inicio: PvP, Versus IA y Perfil. Pantallas secundarias con Volver. Android sin scroll y respetando zonas seguras. Conservar descubrimiento LAN, servidor público, IP, alojamiento local y controles de combate.

## Perfil
Nombre persistente, clase de mago, inventario y equipamiento, inicio de sesión Google mediante Firebase Authentication y estadísticas. No exponer credenciales en el cliente.

## Sala de espera PvP
Mostrar equipos y participantes, indicador Listo/Esperando, botón «Estoy listo» reversible, y botón de inicio habilitado exclusivamente cuando ambos equipos tengan el mismo número de humanos (de 1 a 4) y todos estén listos. Toda incorporación, salida, cambio de equipo o adición de IA invalida las confirmaciones anteriores. Validar de nuevo en el servidor al iniciar; impedir dobles inicios. No comenzar automáticamente al marcar el último listo sin una decisión explícita de inicio del anfitrión, salvo que el diseño de sala acuerde autoarranque.

## IA y recompensas
Permitir añadir IA compañera, siempre indicando «Sin recompensas» si participa cualquier bot. Solo conceder recompensas a ganadores humanos en partidas PvP 1v1, 2v2, 3v3 o 4v4 sin bots. Registrar la recompensa de forma atómica e idempotente en el servidor, ligada a un identificador único de partida; no confiar en el resultado enviado por el cliente. Mostrar «Recompensa ganada. Revisa tu perfil» tras confirmación del servidor.

## Pruebas necesarias
Validar 1v1 a 4v4, equipos desiguales, jugador desconectado, reconexión, ready/unready, incorporación de bot, premio duplicado, victoria/empate, Android horizontal sin scroll, descubrimiento LAN Android/Windows, controles táctiles de escudo y exportación EXE/APK.

La clase BalancedMatchRules contiene únicamente las reglas puras de validación; todavía no está conectada a la sala, servidor ni sistema de recompensas.
