# v0.12.19 — Versiones coherentes al compilar

## Causa del error

`VERSION` y los ensamblados indicaban 0.12.18, pero los presets de Godot seguían exportando Windows con `application/file_version` y `application/product_version` 0.12.14.0 y Android con `version/name` 0.12.14. Windows detectaba la discrepancia después de exportar y Android podía rechazar el preset al empezar o el manifest al terminar.

La prueba anterior de exportación Android simulaba el manifest leyendo VERSION directamente, lo que ocultaba los presets desactualizados. Ahora el exportador de prueba genera el manifest con los valores del preset y la validación lo lee de la APK simulada.

## Corrección

- `Directory.Build.props` lee la versión C# directamente de VERSION mediante las [funciones de MSBuild](https://learn.microsoft.com/en-us/visualstudio/msbuild/property-functions).
- Los empaquetadores Windows, Android PowerShell y Android Bash sincronizan los metadatos de los presets desde VERSION antes de exportar. Se mantiene el bloqueo de compilaciones y se restauran los bytes originales del preset al terminar, también si falla.
- Windows conserva la comprobación de las versiones de archivo y producto del ejecutable generado. Android comprueba tanto `versionName` como `versionCode` en el manifest de la APK.
- El código Android usa `major × 1.000.000 + minor × 1.000 + patch`, con minor y patch entre 0 y 999 y resultado entre 1 y 2.100.000.000. V0.12.19 usa 12019, superior al 134 anterior; el siguiente minor conserva el orden. [Límites de Android](https://developer.android.com/studio/publish/versioning).
- Los errores de versión incluyen el valor esperado y el encontrado. Los presets del repositorio también se actualizan a 0.12.19.
- CI verifica versiones actuales y futuras, lectura de MSBuild, coincidencia entre PowerShell y Python, y los helpers en Windows PowerShell 5.1.

## Validación local

- Ocho pruebas Python correctas, incluidas ejecución real de MSBuild con VERSION 0.13.0 y comparación con PowerShell en tres versiones.
- Regresiones PowerShell correctas: preset antiguo 0.12.14, formato Windows, código Android, versión futura, CRLF, campos ausentes o duplicados y metadatos incorrectos rechazados.
- Empaquetado Android Bash con herramientas simuladas correcto en ARM64 y x86_64. Rechaza versión o código incorrectos, firma fallida y errores de Godot; conserva APK anterior, checksum y presets y elimina los temporales.
- Sustitución de la carpeta Juego y conservación del juego anterior ante paquete incompleto correctas.
- Sintaxis PowerShell y Bash, lectura de VERSION con MSBuild y coherencia de los presets comprobadas.

Las pruebas locales de PowerShell se ejecutaron con 7.4.6 en Linux; las de 5.1 y la exportación real Windows/Android están configuradas en CI. No se ha generado un ejecutable Windows ni una APK real en esta revisión.
