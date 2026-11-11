# Observatorio de Arcana Arena

Dashboard web independiente y táctil para Surface con Windows 10/11 y Edge o Chrome actualizados. No necesita Godot, .NET SDK, Node ni compilar el juego. El servidor local usa PowerShell 5.1 y escucha únicamente en `localhost`.

## Qué muestra

- **Resumen:** actividad diaria, tiempo de juego, daño elemental, clases y mejores actuaciones.
- **En directo:** partidas con señal de menos de 90 segundos, participantes, bajas y daño acumulado.
- **Partidas:** historial ampliable por bloques y detalle con el total y las actuaciones individuales cuando se registraron al cierre.
- **Jugadores:** clasificación de actuaciones individuales dentro de cada partida.
- **Elementos y ataques:** daño de vida y escudo, curación, absorción, ataques y origen del daño.
- **Conexión:** ping medio ponderado, mínimo, máximo, muestras, FPS y tiempo de simulación.

Filtros de últimos 7 o 30 días UTC inclusivos, fechas personalizadas dentro de la cobertura disponible (30 días en el servicio público; hasta 367 días en archivos importados), modo, estado y bots. Exportación CSV de todos los resultados filtrados, aunque la tabla se muestre por bloques. Los nombres se escapan en HTML y las celdas CSV se protegen frente a fórmulas.

La página conecta automáticamente con el servidor, sin iniciar sesión. Cuando el servidor no permite cargar una sección, aparece «Datos no accesibles» y la consulta se reintenta. También se pueden importar informes JSON como una lista de informes, un informe, sobres con `payload`, o `{ "reports": [...], "telemetry": [...] }`. La importación permanece en memoria y no sube archivos a Firebase.

El detalle de una partida muestra el total y las cifras de daño a vida y escudo, bajas y muertes de cada participante. Los informes nuevos numeran los jugadores y bots dentro de la partida. Los informes antiguos conservan sus totales y pueden mostrar un desglose si existe una captura del mismo cierre.

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

Las estadísticas públicas se comprueban cada 15 segundos mientras la pestaña está visible. Los 30 minutos corresponden al **código del dashboard**. La página comprueba el commit cada minuto y se recarga cuando cambia, conservando pestaña y filtros básicos. La pestaña debe seguir abierta. El equipo debe estar encendido y con sesión iniciada; al recuperar una ejecución perdida, la tarea usa `StartWhenAvailable`.

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

## Datos reales sin iniciar sesión

La página consulta `GET /api/dashboard/history` y `GET /api/dashboard/live` en su propio dominio. Si se abre con el servidor local de Windows, consulta `https://familiamelgar.duckdns.org/api/dashboard`. No usa Firebase Authentication, SDK de Firebase, API keys ni cuentas del navegador.

El backend conserva dos listeners compartidos de Firebase Admin. Entrega exclusivamente informes validados, alias del juego, actuaciones y ping. Las cuentas, inventarios, UID e identificadores privados de documentos permanecen fuera de la respuesta. Las reglas de Firestore no se abren al público.

Ventana de 30 días UTC; límite de 500 documentos históricos y 250 señales de telemetría. La interfaz indica cobertura parcial si se alcanzan los límites o se omiten registros inválidos. Las respuestas tienen ETag y gzip; la ausencia de cambios devuelve 304 sin cuerpo. El histórico y las sesiones activas se consultan por separado.

La captura de ping y actuaciones sigue dependiendo de las versiones del cliente que publican telemetría y de los permisos existentes de escritura. Una web pública no habilita escrituras. Sin esas señales, aparecen los históricos disponibles y las vistas de telemetría quedan vacías.

## Raspberry y Nginx

La imagen Docker del servidor incluye `dashboard/web` y sirve la página en `/`. Los endpoints públicos están en el mismo proceso que Firebase Admin. Necesita las credenciales de servidor que ya se utilizan para tienda y recompensas, sin exponerlas al navegador.

Cuando se publique una versión que contenga estos cambios, el actualizador por tags reconstruirá la imagen con la web incluida. Esta modificación de código por sí sola no crea ni despliega una versión.

En el bloque HTTPS existente de Nginx, conserva certificados y la ruta WebSocket `/play`. Para que la web y su API procedan de la misma imagen:

```nginx
location / {
    proxy_pass http://127.0.0.1:27015;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-Proto $scheme;
}
```

No dupliques una directiva `location /` que ya exista: sustituye su contenido. Si Nginx sirve una carpeta estática antigua, esa carpeta seguirá mostrando la web anterior hasta cambiar el proxy o actualizar sus archivos.

Si se mantiene una carpeta estática en Nginx, añade solo este proxy para los datos:

```nginx
location /api/dashboard/ {
    proxy_pass http://127.0.0.1:27015/api/dashboard/;
    proxy_set_header Host $host;
}
```

Si se usa un servidor de cuentas separado con `ARCANA_ACCOUNT_SERVICE_URL`, los endpoints públicos deben apuntar a ese proceso, que dispone de Firebase Admin. El proceso de juego remoto sin repositorio administrativo devuelve 503; nunca se resuelve poniendo una clave privada en JavaScript.

Comprobaciones después del despliegue:

```bash
curl -i https://familiamelgar.duckdns.org/api/dashboard/history
curl -i https://familiamelgar.duckdns.org/api/dashboard/live
```

Un 200 devuelve estadísticas, incluso si todavía no hay partidas. Un 503 indica que el lector está arrancando o no dispone de Firebase Admin; la página reintenta sin pedir una cuenta de Google.

## Verificación sin compilar

```bash
node --check dashboard/web/app.mjs
node --test dashboard/tests/*.test.mjs
```

El análisis de tráfico y sus limitaciones están en [network-dashboard-2026-11-08.md](../docs/reviews/network-dashboard-2026-11-08.md).
