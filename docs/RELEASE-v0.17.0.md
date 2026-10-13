# Arcana Arena v0.17.0

- Servidor independiente de cuentas Firebase con API de autenticación, perfil e inventario.
- El servidor de partidas deja de alojar endpoints de Google y de requerir credenciales Firebase.
- PvP requiere una sesión de Google válida; el servidor valida la sesión contra el servicio de cuentas y consulta equipamiento y recompensas mediante HTTPS autenticado.
- El modo práctica y el perfil guardado permanecen accesibles sin servidor de partidas.
- Versiones de cliente y servidor deben coincidir para jugar online.
- Comprobación de nuevas GitHub Releases al abrir el juego.

**Despliegue:** el servicio de cuentas necesita credenciales de Firebase, URL pública HTTPS y clave compartida de servicio. No se publica una URL hasta que el despliegue esté operativo.
