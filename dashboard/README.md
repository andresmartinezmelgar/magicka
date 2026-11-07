# Observatorio de Arcana Arena

Dashboard web independiente y táctil para Surface con Windows 10/11 y Edge o Chrome actualizados. No necesita Godot, .NET SDK, Node ni compilar el juego. El servidor local usa PowerShell 5.1 y escucha únicamente en `localhost`.

## Qué muestra

- **Resumen:** actividad diaria, tiempo de juego, daño elemental, clases y mejores actuaciones.
- **En directo:** partidas con señal de menos de 90 segundos, participantes, bajas y daño acumulado.
- **Partidas:** historial, detalle por partida, modos y estados.
- **Jugadores:** clasificación de actuaciones individuales dentro de cada partida.
- **Elementos y ataques:** daño de vida y escudo, curación, absorción, ataques y origen del daño.
- **Conexión:** ping medio ponderado, mínimo, máximo, muestras, FPS y tiempo de simulación.

Filtros de últimos 7 o 30 días UTC inclusivos, fechas personalizadas de hasta 367 días, modo, estado y bots. Exportación CSV de todos los resultados filtrados, aunque la tabla se muestre por bloques. Los nombres se escapan en HTML y las celdas CSV se protegen frente a fórmulas.

La página arranca sin datos. La demostración solo se abre al pulsarla y está identificada en todas las vistas y archivos exportados. También se pueden importar informes JSON: una lista de informes, un informe, sobres con `payload`, o `{ "reports": [...], "telemetry": [...] }`. La importación permanece en memoria y no sube archivos a Firebase.

## Instalar en la Surface

Requisito: Git para Windows instalado y accesible como `git`. Ejecuta en **Windows PowerShell**:

```powershell
$repo = Join-Path $env:USERPROFILE 'ArcanaArenaDashboard'
git clone --filter=blob:none --sparse --branch pruebas/dashboard-estadisticas https://github.com/andresmartinezmelgar/magicka.git $repo
if ($LASTEXITCODE -ne 0) { throw 'No se ha podido clonar el repositorio.' }
git -C $repo sparse-checkout set dashboard
if ($LASTEXITCODE -ne 0) { throw 'No se ha podido preparar el dashboard.' }
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$repo\dashboard\windows\Install-Dashboard.ps1" -Repository $repo
```

El instalador valida el remoto y la rama antes de crear las tareas del usuario actual:

1. `ArcanaArena-Dashboard-Actualizar`: `git fetch` y avance rápido **cada 30 minutos**, sin contraseña almacenada ni privilegios elevados.
2. `ArcanaArena-Dashboard-Iniciar`: servidor y navegador al iniciar sesión en Windows; también arranca inmediatamente.

Dirección: **http://localhost:8787/**. F11 o el botón de pantalla completa. Puedes fijar esta página como aplicación en Edge.

Las estadísticas se actualizan con los eventos de Firestore. Los 30 minutos corresponden al **código del dashboard**. La página comprueba el commit cada minuto y se recarga cuando cambia, conservando pestaña y filtros básicos. La pestaña debe seguir abierta. El equipo debe estar encendido y con sesión iniciada; al recuperar una ejecución perdida, la tarea usa `StartWhenAvailable`.

Se rechazan cambios locales, otra rama, otro remoto y divergencias. No se usa reset, limpieza, rebase ni push forzado. Un mutex evita actualizaciones simultáneas. Registro: `%LOCALAPPDATA%\ArcanaArenaDashboard\updates.log`, con rotación de 1 MiB.

Si usas un checkout existente, sustituye `$repo` por su ruta y pasa la rama exacta que contiene este dashboard. Cuando se integre en main, vuelve a ejecutar el instalador con `-Branch main` desde un checkout de main. La reinstalación actualiza las mismas dos tareas. El instalador no modifica la rama actual por su cuenta.

Arranque manual:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$repo\dashboard\windows\Start-Dashboard.ps1" -Repository $repo -OpenBrowser
```

Retirar tareas y detener el servidor, conservando repositorio y logs:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$repo\dashboard\windows\Uninstall-Dashboard.ps1"
```

## Conectar datos reales

La configuración pública del proyecto `arcana-arena-f2997` ya está en `web/firebase.mjs`. La API key identifica el proyecto; no concede lectura. No hay contraseñas, tokens de sesión, claves privadas o cuentas de servicio en el dashboard.

1. En Firebase Authentication → Configuración → Dominios autorizados, autoriza `localhost` y, para la página alojada, `arcana-arena-observatorio.andresmartinezmelgar.chatgpt.site`.
2. Abre el dashboard → **Configurar conexión → Conectar con Google** usando una cuenta ya habilitada en `statisticsReaders`. Se mantienen los permisos existentes; el sitio no permite concederse roles.
3. El historial funciona con la colección existente `matchStatistics`. Se consulta todo el intervalo con un listener, sin truncarlo a una primera página. No hay límite silencioso de documentos; intervalos extensos implican más lecturas y memoria.
4. Para nombres, partidas activas y ping, despliega las reglas/índices nuevos y utiliza una versión del cliente **y del servidor** que incluya `Main.Dashboard.cs` y el metadato `StatisticsRunId`. Windows y Android anteriores siguen enviando solo informes anónimos al cerrar.

Desde un equipo autenticado con permisos administrativos en el proyecto:

```powershell
firebase deploy --project arcana-arena-f2997 --only firestore:rules,firestore:indexes
```

No se han desplegado esas reglas en producción: este entorno no dispone de sesión administrativa de Firebase. Las reglas nuevas son un prototipo de acceso restringido, validado en emulador; conviene revisarlas antes de ampliar los lectores. No se han cambiado las cuentas lectoras autorizadas.

## Contratos y alcance

Se conserva sin cambios `matchStatistics`: contrato anónimo y cerrado, sin nombres, UID ni ping. Se agrupan revisiones por `runId` siguiendo `MatchStatisticsReport.IsNewer`; un informe finalizado no se sustituye por uno no finalizado, y una revisión inferior no sustituye una superior. Cabeceras incompatibles se omiten y se informa del número. No se suman proyecciones alternativas del mismo daño ni exceso de daño.

Los históricos solo tienen día UTC. No se inventa una hora de inicio ni un orden exacto dentro del día. `humans`/`bots` son máximos simultáneos por partida: la suma se llama participaciones, nunca jugadores únicos. Los actores históricos agrupan clase/equipo, no personas. La clasificación con nombres muestra actuaciones por partida; nombres iguales no garantizan identidad entre partidas.

Nuevo canal `dashboardTelemetry/{runId}-{ownerUid}`:

- Solo escribe el cliente de una cuenta Google con permiso de lector; cada cliente escribe su documento. Solo leen las cuentas lectoras habilitadas.
- Cabecera validada: `schemaVersion=1`, `runId`, `ownerUid`, `day`, `revision`, `status`, `updatedAt` de servidor y `payload` JSON ≤64 KiB.
- Payload: modo, mapa, versión, estado, duración, ID local de remitente y hasta 32 jugadores con nombre de juego, clase, equipo, bot, vida, bajas/muertes y matrices de daño de nueve elementos. Sin correos, tokens, IP ni perfiles privados.
- Ping: suma, número, mínimo y máximo de respuestas WebSocket desde que se identifica la ronda. No se mide desde la Surface ni se cuenta una ausencia como 0 ms. Una partida local aparece sin ping.
- El cliente envía aproximadamente cada 15 s; el listener actualiza la web al recibir el documento. Pausas del SO, desconexión o fallos de red pueden impedir el heartbeat. Tras 90 s se marca sin señal.
- Varios clientes de la misma partida no duplican jugadores/partidas. Cada ping pertenece a su remitente; se combina por muestras. Un reinicio del cliente o reconexión inicia un nuevo tramo de muestras y puede sustituir las muestras anteriores de ese remitente. No se promete una media de conexión de toda la vida.
- La correlación `StatisticsRunId` evita asignar a una ronda el snapshot de la siguiente. Con un servidor antiguo sin ese metadato no se publica el canal nuevo.
- Cola en memoria de hasta ocho rondas pendientes. Reintentos mientras el juego siga abierto. Cerrar el proceso puede perder telemetría aún sin enviar; las cifras son observacionales, no premios autoritativos.

Vida y escudo se muestran como apoyo: curación y absorción. El daño elemental histórico de jugadores incluye sus eventos de daño propio y fuego amigo; el daño individual de `MatchDamageSnapshot` los excluye. Son alcances diferentes y no se mezclan para hacer rankings.

## Verificación

```sh
node --test dashboard/tests/model.test.mjs
npm install --prefix tests/firebase-rules
node tests/firebase-rules/node_modules/firebase-tools/lib/bin/firebase.js emulators:exec --only firestore --project demo-arcana-arena 'node tests/firebase-rules/verify.cjs && node tests/firebase-rules/verify-dashboard.cjs'
```

El emulador verifica también los permisos anteriores: no se conceden lecturas públicas, escritura de roles, acceso cruzado a perfiles, cambios de dueño, revisiones regresivas, timestamps inventados ni reapertura de una finalización. El JSON interno se valida en los lectores; las reglas solo admiten su envoltura acotada de remitentes autorizados.

Las comprobaciones de publicación incluyen compilación C#, regresiones del juego, reglas del canal privado y arranque HTTP del dashboard con Windows PowerShell 5.1. La ejecución del Programador de tareas en la Surface se verifica en ese equipo. Activar los datos nuevos sigue requiriendo desplegar las reglas en Firebase.

Referencias de implementación: [listeners Firestore](https://firebase.google.com/docs/firestore/query-data/listen), [Google Auth web](https://firebase.google.com/docs/auth/web/google-signin), [tareas programadas Windows](https://learn.microsoft.com/en-us/powershell/module/scheduledtasks/new-scheduledtasktrigger).
