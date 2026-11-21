# Actualización del servidor sin cortar partidas

El supervisor comprueba la última Release estable cada 30 minutos. Cuando hay
una versión superior, bloquea las partidas nuevas en la generación actual,
prepara otro contenedor y cambia la entrada pública al nuevo cuando está sano.
Las partidas iniciadas y sus reconexiones permanecen en su generación original.

## Funcionamiento

1. La entrada pública mantiene su URL y puerto. El HTTPS/WSS de nginx sigue
   apuntando al mismo puerto; no hace falta cambiar DuckDNS ni el router.
2. Al detectar una Release superior, el proxy muestra «Iniciando servidor con
   la nueva versión…» en la consulta de salas. El servidor anterior confirma
   que ha dejado de admitir salas, nuevas entradas, revanchas y reinicios.
3. Se descarga el tag en una copia independiente y se compila una imagen.
   El contenedor anterior continúa simulando las partidas que ya había iniciado.
4. El candidato arranca sin admitir partidas. Se comprueban su salud, versión,
   identidad y control privado antes de habilitarlo y publicar su ruta.
5. Las conexiones existentes conservan su destino. Una reconexión utiliza
   la generación incluida en el identificador de su sala. La serie completa
   cuenta como una partida; una pausa entre rondas no permite cerrar el proceso.
6. Cada cinco segundos se revisa la generación anterior. Las reservas de
   reconexión conservan la partida durante su plazo habitual de 30 segundos.
   Cuando no quedan partidas ni escrituras pendientes, se dejan diez segundos
   para entregar el resultado y se detiene y elimina únicamente ese contenedor.
   No se borran volúmenes.
7. Si falla la descarga, compilación o arranque antes del cambio público, la
   generación conocida vuelve a admitir partidas. Después de publicar la nueva,
   una incidencia del supervisor se recupera desde su estado persistente sin
   destruir las partidas nuevas.

Hay como máximo dos generaciones de juego y un contenedor de entrada ligero y
permanente. Si llega otra Release mientras todavía termina una generación,
queda en espera. Al liberarse la plaza se vuelve a comprobar la última Release
estable. Se ignoran borradores, versiones de pruebas, descensos y cambios de
main que no estén publicados.

El cliente y el servidor mantienen la comprobación habitual de versión exacta.
Las partidas antiguas siguen con su cliente original. Para jugar en la generación
nueva se necesita su versión del juego. La instalación automática del cliente ya
se pospone mientras hay una partida o reconexión en curso.

## Primera instalación

El servidor anterior a este cambio no tiene control de admisión. La activación
inicial necesita una pausa entre partidas: el instalador exige que el contenedor
Compose anterior esté detenido y no lo detiene por su cuenta. Los cambios
posteriores utilizan el relevo descrito arriba.

Requiere Linux de 64 bits, Python 3, Git, Docker y Docker Compose. Conserva el
contenedor original, sus volúmenes y sus archivos de configuración. No ejecutes
down -v.

Primero elimina la tarea cron antigua que ejecutaba git pull y docker compose
up/restart para este juego. Conserva las otras tareas. El instalador desactiva el
temporizador systemd antiguo de Arcana; detecta también las entradas directas
habituales de crontab. Revisa los scripts intermedios y cron de sistema si se usaban.

Preparar la imagen mientras el servidor anterior sigue funcionando:

~~~bash
cd ~/magicka
git pull --ff-only origin main
sudo python3 scripts/raspberry-generations.py prepare --checkout "$PWD"
~~~

Cuando hayan terminado sus partidas, detener solo el servicio anterior e instalar:

~~~bash
docker compose stop server
sudo python3 scripts/raspberry-generations.py install --checkout "$PWD"
~~~

Si se utiliza compose.lan.yaml, los dos últimos comandos son:

~~~bash
docker compose -f compose.lan.yaml stop server
sudo python3 scripts/raspberry-generations.py install --checkout "$PWD" --compose-file compose.lan.yaml
~~~

prepare e install compilan el código del checkout. La segunda compilación
reutiliza la caché de Docker. Después, las actualizaciones automáticas solo
descargan Releases estables.

La instalación copia entorno, usuario, red, límites y montajes del contenedor
anterior. Las colas de premios y estadísticas necesitan montajes persistentes
compartidos con escritura; el instalador lo comprueba. Los dos puertos privados
predeterminados son 27017 y 27019. Se pueden cambiar con
--backend-ports 28017,28019 al instalar. No se abren en el router.

Con la variante LAN, los procesos de juego escuchan solo en loopback. Con una
red Docker, conservan esa red y su DNS, y publican sus puertos únicamente en
127.0.0.1. Solo la entrada publica el puerto de juego y el descubrimiento UDP
original. Los registros de cada generación se separan dentro del volumen de logs.

El contenedor Compose anterior queda detenido como respaldo, con su reinicio
automático desactivado. Después de instalar, el juego lo gestionan los
contenedores con etiqueta io.arcana.rolling; docker compose stop server actúa
sobre el respaldo, no sobre las generaciones nuevas.

## Resultados y recuperación

Premios, bestiario y estadísticas pendientes usan los volúmenes compartidos.
Un bloqueo de archivo coordina los procesos para que una confirmación anterior
no borre datos más recientes. Las estadísticas usan una cola temporal que se
elimina después de confirmar el envío, no un historial permanente en el cliente.
Un proceso antiguo no lee los archivos de estadísticas de versiones posteriores.

Si Firebase no responde, el resultado se guarda y el servidor nuevo continúa sus
reintentos. La generación anterior no tiene que esperar a recuperar Internet para
liberar recursos. Si no se puede guardar en disco, se conserva hasta resolverlo.

El supervisor guarda las etapas del cambio y las identidades Docker. Tras un
reinicio completa el cambio o recupera la admisión anterior. Un contenedor con
otro ID o sin sus etiquetas no se elimina. No arranca una entrada o generación
publicada que el administrador haya detenido voluntariamente.

Una instalación inicial interrumpida conserva su estado. Repetir install
continúa la preparación sin sustituir una generación ya publicada. Reinstalar
los archivos del supervisor reinicia solo su servicio, conservando los procesos
de juego. La imagen de la entrada permanece estable durante los relevos.

## Control y registros

~~~bash
sudo systemctl status arcana-server-update.service --no-pager
sudo journalctl -u arcana-server-update.service -n 60 --no-pager
sudo python3 /usr/local/lib/arcana-server-updater/update.py status
~~~

Comprobar ahora, también si el supervisor ya está ejecutándose:

~~~bash
sudo python3 /usr/local/lib/arcana-server-updater/update.py run
~~~

Detener las comprobaciones sin detener las partidas:

~~~bash
sudo systemctl disable --now arcana-server-update.service
~~~

Si se detiene durante una preparación, el estado se conserva y se recupera al
volver a iniciar el supervisor. No elimina los contenedores por apagar el servicio.

Archivos instalados:

| Archivo | Uso |
|---|---|
| /usr/local/lib/arcana-server-updater/ | Programa y módulo del supervisor |
| /etc/arcana-server-updater.json | Configuración privada y plantilla Docker |
| /var/lib/arcana-server-updater/rolling.json | Etapa e identidades de los procesos |
| /var/lib/arcana-server-updater/routing/routes.json | Rutas sin credenciales |
| /var/lib/arcana-server-updater/nodes/ | Admisión persistente por generación |

El token de administración no aparece en las rutas públicas. Los endpoints
/internal/lifecycle e /internal/admission requieren ese token y un origen local
autorizado; el proxy público los bloquea y no reenvía la cabecera de control.

## Comprobaciones

Sin compilar ni modificar Docker:

~~~bash
python3 scripts/test-raspberry-auto-update.py
~~~

Tras una petición expresa de compilación, el flujo manual también ejecuta:

- La prueba de red --rolling-update-only: conexiones activas, arranque, salas
  bloqueadas, cambio de destino, reconexión, series y control privado.
- La recuperación de estadísticas y la exclusión entre procesos sobre el diario.
- Dos backends y la entrada real dentro de Docker para amd64 y arm64, con
  publicación privada, control a través de NAT y volumen compartido.

El control de salud comprueba arranque e identidad; no simula una partida con
las credenciales de producción.
