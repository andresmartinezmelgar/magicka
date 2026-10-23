# Actualizaciones automáticas mediante GitHub Releases

## Cliente

Windows x64 y Android ARM64 consultan la última Release estable al arrancar y cada minuto. **Perfil → Buscar actualización** permite consultar de inmediato y muestra el resultado sin iniciar la descarga. Las consultas simultáneas se comparten. Las cuentas autorizadas también pueden recibir preliminares según las reglas de [canales](CANALES-ACTUALIZACION.md). Seleccionan exclusivamente el archivo de su plataforma y de la versión anunciada. Si una Release todavía solo contiene Android, Windows espera a que se publique su ZIP. Se rechazan borradores, versiones anteriores y paquetes sin SHA-256; solo una compilación de pruebas más reciente puede actualizar la misma versión base.

El aviso permite elegir descargar o continuar jugando. La descarga muestra progreso y puede pausarse. Se conservan un archivo `.part` y su identidad (canal, compilación, URL, tamaño, SHA-256 y ETag cuando existe) en el almacenamiento privado. Cerrar el aviso, salir del juego, perder la conexión o terminar el proceso no descarta los bytes guardados. Al volver a abrir el aviso aparece «Continuar descarga» con el progreso conservado; requiere pulsarlo y no inicia una descarga sin elegirlo.

La continuación pide `Range` y, cuando hay un ETag fuerte, `If-Range`. Se valida `Content-Range` antes de añadir bytes. Un servidor que responde con el paquete completo reemplaza el parcial; un rango rechazado, un cambio de publicación o un prefijo corrupto obliga a empezar desde cero. El tamaño y el SHA-256 del archivo completo deben coincidir con la Release antes de instalarlo. Un archivo completo ya verificado se reutiliza, incluso si el proceso terminó antes de promover el parcial. El instalador nunca se inicia durante una partida, una conexión en preparación o un servidor local activo. Sin red o si falla la descarga, el juego sigue disponible.

Conservar y reanudar el archivo no mantiene vivo el juego cuando Android lo cierra. La transferencia sigue perteneciendo al proceso de la aplicación; no hay un servicio Android de descarga persistente en segundo plano.

Al arrancar una versión instalada se retiran sus paquetes completos, parches, parciales y metadatos, además de los de versiones anteriores y los temporales antiguos no reanudables. Se respeta una descarga de una versión o compilación de pruebas aún futura. Al iniciar otra descarga solo se conserva el destino seleccionado y su alternativa completa. La limpieza se limita a los archivos reconocidos de actualizaciones; no recorre carpetas ni afecta a partidas, perfiles o copias de recuperación Windows.

El cliente prefiere un parche de su plataforma y de la versión que tiene instalada cuando reduce al menos un 15 % el tamaño de descarga. Las sesiones, inventario y preferencias siguen en el almacenamiento de usuario.

## Descargas diferenciales

Windows recibe un ZIP con los archivos modificados, UPDATE-MANIFEST.json completo y UPDATE-DELTA.json. Antes de cerrar el juego, el ayudante verifica versión y manifiesto y compone el paquete nuevo reutilizando los archivos locales que ya coinciden en tamaño y SHA-256. La configuración local protegida siempre tiene una copia de distribución en el parche; se conserva la configuración del usuario al aplicar la instalación. Se retiran archivos obsoletos únicamente cuando coinciden con los bytes antiguos publicados; sus copias participan en el rollback.

Android recibe un archivo .delta comprimido con instrucciones de copia y datos nuevos. El generador reconoce las entradas ZIP cuyo contenido comprimido no ha cambiado y también busca fragmentos reutilizables en entradas grandes modificadas. Los offsets pueden cambiar: no se depende de que las dos APK tengan los mismos bloques en las mismas posiciones. La APK instalada se lee mediante applicationInfo.sourceDir y no se modifica.

El cliente comprueba versión, tamaño y SHA-256 de la base, reconstruye en un archivo temporal y verifica los bytes finales contra el SHA-256 de la APK completa publicada. Solo entonces pasa la APK al instalador. Se conservan exactamente los bytes de la firma; el cliente nunca vuelve a firmar la APK.

Si falta un parche de esa versión, no reduce suficientemente la descarga, falta la base, se ha alterado un archivo, falla la reconstrucción o falla la preparación Windows, se utiliza automáticamente el paquete completo. Un parche rechazado no vuelve a descargarse en los siguientes arranques para esa versión de destino. Los archivos completos ya descargados y verificados siguen utilizándose sin descargarlos otra vez.

Los nombres de los parches indican ambas versiones: ArcanaArena-vDESTINO-Windows-x64-from-vBASE.zip y ArcanaArena-vDESTINO-Android-arm64-from-vBASE.delta. Los paquetes completos conservan sus nombres anteriores para que los clientes antiguos sigan funcionando.

## Windows

El instalador está integrado como recurso del núcleo. Se ejecuta fuera de la carpeta del juego, sin consola visible. Antes de cerrar el cliente vuelve a verificar el ZIP, sus rutas, los archivos necesarios, VERSION.txt y UPDATE-MANIFEST.json. El paquete Windows incluye un manifiesto con tamaño y SHA-256 de cada archivo. También comprueba que la carpeta admite escritura; un fallo de preparación deja el juego abierto.

Una señal de preparación confirma que el paquete es válido. El cliente se cierra y el ayudante espera a que termine ese proceso concreto, identificado por PID y hora de inicio. Después sustituye únicamente los archivos del paquete, conserva la configuración local de cuenta/OAuth/servidor y los archivos ajenos a la distribución, y reinicia ArcanaArena.exe. No hace falta abrir el ZIP, extraerlo, copiar archivos ni iniciar manualmente el juego.

Los archivos sustituidos se guardan fuera de la instalación. Un fallo de sustitución revierte los cambios ya aplicados. Si el ejecutable nuevo falla al arrancar, se recupera la versión anterior. La descarga instalada se limpia y se conserva la última copia de recuperación. Una carpeta sin permisos, una política del sistema que impida PowerShell o archivos bloqueados por otros procesos pueden impedir la operación; el error queda registrado y se mantiene la versión anterior. No se solicitan privilegios de administrador automáticamente.

## Android

El plugin ArcanaUpdates entrega el APK directamente a PackageInstaller desde el almacenamiento privado. Antes de crear la sesión vuelve a comprobar SHA-256, nombre de paquete y código de versión creciente. El instalador de Android comprueba además la compatibilidad de la firma con la aplicación instalada. No se usan file://, un explorador de archivos ni la apertura manual de la APK.

Android puede exigir una autorización inicial para permitir actualizaciones de Arcana Arena. Se abre su pantalla de permisos y el proceso continúa al volver. En Android 12 o posterior se solicita USER_ACTION_NOT_REQUIRED para actualizar la propia aplicación y se declara UPDATE_PACKAGES_WITHOUT_USER_ACTION. Android sigue siendo quien decide: si devuelve STATUS_PENDING_USER_ACTION, se abre automáticamente su confirmación. En versiones anteriores o en dispositivos que la exijan habrá que aceptar esa ventana del sistema.

El resultado se entrega a una actividad explícita que permanece disponible en la versión nueva. Solo después de que Android confirme una instalación correcta elimina la APK descargada y vuelve a abrir el juego; el arranque limpia también los parches, parciales y metadatos anteriores. Si Android cancela o rechaza la instalación, el paquete se conserva para reintentar. No se cambia la firma, no se desinstala la aplicación y no se borra su almacenamiento. Una APK con otra firma se rechaza y conserva la instalación existente.

Referencia del API: https://developer.android.com/reference/android/content/pm/PackageInstaller.SessionParams#setRequireUserAction(int)

## Publicación y primera instalación

VERSION y los presets Windows/Android se sincronizan con scripts/version-support.py. Los trabajos generan parches contra hasta tres versiones estables anteriores que tengan un paquete completo verificable. Cada archivo base se descarga de la Release oficial y se verifica con su SHA-256. Una base ausente, una incidencia de red o un parche sin ahorro conservan la publicación del paquete completo.

Los parches Android se generan y verifican de nuevo después de la última firma permanente del trabajo de publicación. No se reutiliza un parche cuyo destino haya sido firmado otra vez. Los manifiestos SHA256SUMS-Windows.txt y SHA256SUMS-Android.txt incluyen tanto el paquete completo como los parches y evitan que la publicación independiente de ambas plataformas mezcle hashes.

El generador y el cliente se prueban por separado: los parches creados en Python se reconstruyen con el lector C# del cliente. Las pruebas Windows ejecutan la instalación diferencial, retirada de archivos, conservación de datos, alternativa completa y recuperación con un PCK bloqueado. También simulan un proceso real que retiene el PCK hasta cerrar, para verificar la espera por PID y hora de inicio. Las Releases se generan con los flujos actuales de main o de un tag vVERSION, después de sus comprobaciones de compilación, cuentas y empaquetado. La firma Android usa los secretos permanentes ya configurados. No debe publicarse una APK firmada con una clave temporal.

Una instalación anterior, incluida v0.17.16, conserva su actualizador antiguo hasta instalar por primera vez una distribución con el instalador integrado. Supervivencia y el botón de búsqueda manual se distribuyen en la versión estable 0.20.0 y posteriores.

## Verificación

Las pruebas del núcleo comprueban la selección exacta de plataforma/versión, Releases incompletas, URLs y tamaños inválidos, manifiestos ausentes o duplicados, y reutilización de caché solo cuando coincide su integridad. Las pruebas PowerShell ejecutan el instalador con ZIP reales: instalación, conservación de configuración, recuperación, hashes, rutas inseguras, versión y runtime ausentes. CI Windows añade una sustitución fallida por un PCK realmente bloqueado. CI Android compila el plugin e inspecciona que el instalador y los permisos estén presentes en la APK final. Las pruebas Godot existentes comprueban los botones y la geometría del aviso en móvil y escritorio.

La autorización del instalador y la reentrada tras sustituir la propia APK deben verificarse en un dispositivo Android físico; compilar y revisar el manifest no equivale a instalar sobre una app en ejecución.
