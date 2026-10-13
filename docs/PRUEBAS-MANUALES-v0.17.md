# Pruebas manuales — Arcana Arena v0.17

GitHub Actions **solo compila** los proyectos C# y **genera** los paquetes Android y Windows. La existencia de un APK o ZIP no demuestra que el juego funcione en dispositivos reales.

## Checklist de validación del usuario

- [ ] **Windows:** descomprimir ZIP en un equipo sin entorno de desarrollo, abrir el juego, comprobar menú, gráficos, audio, controles y cierre.
- [ ] **Android:** instalar APK ARM64 en móvil físico, abrir el juego, comprobar orientación, HUD, escudos direccionales, multitáctil y consumo de recursos.
- [ ] **LAN:** alojar partida en el ordenador, localizar la sala desde el móvil por Wi-Fi, entrar, jugar y cerrar correctamente.
- [ ] **Red externa:** conectar desde otra red con el servidor en el ordenador; comprobar puertos, desconexiones y reconexiones.
- [ ] **Google Android:** iniciar sesión, cerrar y reabrir app, recuperar la cuenta existente.
- [ ] **Google Windows:** iniciar sesión con la misma cuenta y verificar que se recupera el mismo UID y perfil.
- [ ] **Firebase:** verificar que nombre, estadísticas, inventario y equipación se leen correctamente en ambos clientes.
- [ ] **Offline:** abrir práctica sin servidor; usar equipación de caché; comprobar que los cambios locales no se presentan como guardados en la nube.
- [ ] **Seguridad:** comprobar que el cliente no puede escribir oro, victorias, recompensas ni inventario directamente en Firestore.
- [ ] **Venta y equipación:** con el servidor encendido, vender y equipar; comprobar transacciones y persistencia tras reiniciar.
- [ ] **Recompensas:** terminar PvP, verificar resultado y que no se abona dos veces.
- [ ] **Servidor apagado:** confirmar que se puede consultar el perfil online con Firebase y jugar offline, pero no vender ni iniciar PvP.
- [ ] **Servidor sin credenciales:** confirmar que no se expone acceso privilegiado a Firebase ni se conceden recompensas.
- [ ] **Actualización:** comprobar que la actualización instalada conserva datos y configuración local.

## Límites de CI

Las pruebas manuales anteriores no se ejecutan ni se marcan como correctas en GitHub Actions. Los artefactos de CI son compilaciones para revisión, **no versiones validadas**. La publicación y firma de distribución se realizan manualmente tras completar el checklist.
