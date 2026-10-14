# Actualizaciones automáticas mediante GitHub Releases

## Cliente

Windows x64 y Android ARM64 consultan la última Release estable al arrancar y cada minuto. Seleccionan exclusivamente el archivo de su plataforma y de la versión anunciada. Si una Release todavía solo contiene Android, Windows espera a que se publique su ZIP. No se descargan borradores, previews, versiones iguales, versiones anteriores ni paquetes sin SHA-256.

La descarga empieza automáticamente en el menú, muestra progreso y puede cancelarse. El tamaño y el SHA-256 deben coincidir con la Release. Un archivo completo que ya se haya descargado y verificado se reutiliza; una descarga incompleta o alterada se rechaza. El instalador nunca se inicia durante una partida, una conexión en preparación o un servidor local activo. Sin red o si falla la descarga, el juego sigue disponible.

Cada plataforma descarga su paquete completo: esto no implementa parches diferenciales por archivo. Las sesiones, inventario y preferencias siguen en el almacenamiento de usuario.

## Windows

El instalador está integrado como recurso del núcleo. Se ejecuta fuera de la carpeta del juego, sin consola visible. Antes de cerrar el cliente vuelve a verificar el ZIP, sus rutas, los archivos necesarios, VERSION.txt y UPDATE-MANIFEST.json. El paquete Windows incluye un manifiesto con tamaño y SHA-256 de cada archivo. También comprueba que la carpeta admite escritura; un fallo de preparación deja el juego abierto.

Una señal de preparación confirma que el paquete es válido. El cliente se cierra y el ayudante espera a que termine ese proceso concreto, identificado por PID y hora de inicio. Después sustituye únicamente los archivos del paquete, conserva la configuración local de cuenta/OAuth/servidor y los archivos ajenos a la distribución, y reinicia ArcanaArena.exe. No hace falta abrir el ZIP, extraerlo, copiar archivos ni iniciar manualmente el juego.

Los archivos sustituidos se guardan fuera de la instalación. Un fallo de sustitución revierte los cambios ya aplicados. Si el ejecutable nuevo falla al arrancar, se recupera la versión anterior. La descarga instalada se limpia y se conserva la última copia de recuperación. Una carpeta sin permisos, una política del sistema que impida PowerShell o archivos bloqueados por otros procesos pueden impedir la operación; el error queda registrado y se mantiene la versión anterior. No se solicitan privilegios de administrador automáticamente.

## Android

El plugin ArcanaUpdates entrega el APK directamente a PackageInstaller desde el almacenamiento privado. Antes de crear la sesión vuelve a comprobar SHA-256, nombre de paquete y código de versión creciente. El instalador de Android comprueba además la compatibilidad de la firma con la aplicación instalada. No se usan file://, un explorador de archivos ni la apertura manual de la APK.

Android puede exigir una autorización inicial para permitir actualizaciones de Arcana Arena. Se abre su pantalla de permisos y el proceso continúa al volver. En Android 12 o posterior se solicita USER_ACTION_NOT_REQUIRED para actualizar la propia aplicación y se declara UPDATE_PACKAGES_WITHOUT_USER_ACTION. Android sigue siendo quien decide: si devuelve STATUS_PENDING_USER_ACTION, se abre automáticamente su confirmación. En versiones anteriores o en dispositivos que la exijan habrá que aceptar esa ventana del sistema.

El resultado se entrega a una actividad explícita que permanece disponible en la versión nueva y vuelve a abrir el juego cuando Android confirma la instalación. No se cambia la firma, no se desinstala la aplicación y no se borra su almacenamiento. Una APK con otra firma se rechaza y conserva la instalación existente.

Referencia del API: https://developer.android.com/reference/android/content/pm/PackageInstaller.SessionParams#setRequireUserAction(int)

## Publicación y primera instalación

VERSION y los presets Windows/Android se sincronizan con scripts/version-support.py. Las Releases se generan con los flujos actuales de main o de un tag vVERSION, después de sus comprobaciones de compilación, cuentas y empaquetado. La firma Android usa los secretos permanentes ya configurados. No debe publicarse una APK firmada con una clave temporal.

El flujo automático existe a partir de una versión que incluya este cambio. Una instalación anterior, incluida v0.17.16, conserva su actualizador antiguo hasta instalar por primera vez una distribución con el instalador integrado. Los cambios de esta rama no publican por sí solos una Release ni modifican main.

## Verificación

Las pruebas del núcleo comprueban la selección exacta de plataforma/versión, Releases incompletas, URLs y tamaños inválidos, manifiestos ausentes o duplicados, y reutilización de caché solo cuando coincide su integridad. Las pruebas PowerShell ejecutan el instalador con ZIP reales: instalación, conservación de configuración, recuperación, hashes, rutas inseguras, versión y runtime ausentes. CI Windows añade una sustitución fallida por un PCK realmente bloqueado. CI Android compila el plugin e inspecciona que el instalador y los permisos estén presentes en la APK final. Las pruebas Godot existentes comprueban los botones y la geometría del aviso en móvil y escritorio.

La autorización del instalador y la reentrada tras sustituir la propia APK deben verificarse en un dispositivo Android físico; compilar y revisar el manifest no equivale a instalar sobre una app en ejecución.
