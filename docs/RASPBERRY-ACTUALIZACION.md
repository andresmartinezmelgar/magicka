# Actualización automática del servidor en Raspberry Pi

El actualizador consulta la última **Release estable publicada** de
`andresmartinezmelgar/magicka` cada 30 minutos. Solo instala tags `vX.Y.Z`
más recientes; ignora borradores, versiones de pruebas y cambios de `main`
que todavía no tengan una Release estable. Descarga el código de ese tag y
compila la imagen ARM64 en la Raspberry. No descarga la APK para ejecutar el servidor.

## Instalación única

Requiere Raspberry Pi OS/Debian de 64 bits con Python 3, Git, Docker y Docker
Compose. El servidor debe estar funcionando con el Compose de este repositorio.
Se admiten tanto `docker compose` como `docker-compose`.

Desde la cuenta que tiene el repositorio en `~/magicka`:

```bash
cd ~/magicka &&
git pull --ff-only origin main &&
sudo python3 scripts/raspberry-auto-update.py install --checkout "$PWD"
```

Si el servidor se inició con la variante LAN, añadir `--compose-file compose.lan.yaml`
al último comando. El instalador obtiene del contenedor el nombre del proyecto y
todos sus archivos Compose; así mantiene los mismos volúmenes, puertos, montajes
y opciones. Conserva el archivo `.env` y utiliza la configuración local al recrear
el servidor. Los valores que se hubieran exportado únicamente en una consola deben
estar en `.env` o en los archivos Compose para persistir en una ejecución desatendida.

El instalador configura `arcana-server-update.timer` con systemd y lo activa.
No necesita crontab. También comprueba novedades tras volver a encender la Raspberry
si se perdió alguna ejecución mientras estaba apagada. El programa instalado queda
en `/usr/local/lib/arcana-server-updater/update.py`, la configuración en
`/etc/arcana-server-updater.json` y su estado en `/var/lib/arcana-server-updater`.

## Aplicación de una versión

1. Consulta la Release estable y la versión del servidor actual. No hace descensos
   de versión ni arranca un servidor que se haya detenido voluntariamente.
2. Si detecta jugadores humanos en cualquier sala, aplaza la actualización.
3. Descarga el tag en una copia independiente y compila mientras el servidor
   anterior continúa funcionando. No modifica el repositorio de `~/magicka`.
4. Comprueba otra vez que no hayan entrado jugadores ni cambiado la instancia.
5. Guarda la imagen anterior, sustituye solo el servicio `server` y comprueba que
   `/health` responde con la versión esperada. Si falla, intenta restaurar la imagen
   anterior y verifica su respuesta. Solo registra la versión como aplicada tras
   superar la comprobación.

Recrear el contenedor conserva los volúmenes de datos y registros; no se ejecuta
`down -v` ni se eliminan imágenes o volúmenes ajenos. Las salas en memoria se
reinician. La comprobación de ocupación no bloquea conexiones nuevas: una conexión
que llegue justo durante la sustitución puede interrumpirse. Los clientes deben
tener un protocolo compatible con el servidor nuevo.

La comprobación de salud valida el arranque y la versión, no reproduce partidas
ni revierte posibles migraciones de datos de una versión futura. Si falla la
compilación o GitHub no está disponible, el contenedor actual sigue funcionando;
el temporizador volverá a intentarlo. Los registros indican si también falla la
recuperación de la imagen anterior.

## Estado y registros

```bash
systemctl list-timers arcana-server-update.timer
sudo journalctl -u arcana-server-update.service -n 60 --no-pager
```

Comprobar ahora, con las mismas reglas de ocupación y versión:

```bash
sudo systemctl start arcana-server-update.service
```

Desactivar comprobaciones automáticas:

```bash
sudo systemctl disable --now arcana-server-update.timer
```

Pruebas sin compilar ni sustituir servidores reales:

```bash
python3 scripts/test-raspberry-auto-update.py
```
