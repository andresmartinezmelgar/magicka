# Mediciones del renderer

En **Práctica → Medir rendimiento gráfico**, elegir el mapa y ejecutar la medición. La misma opción está disponible en PC y móvil. Calienta la escena durante 5 segundos y recoge muestras durante 20 segundos. Cancelar, pulsar Atrás o enviar la app móvil a segundo plano guarda un informe parcial, marcado `canceled`, y vuelve al launcher.

El informe se guarda en `user://profiles/latest.json`, dentro de los datos de la app; cada ejecución sustituye ese informe. El resultado también queda en el registro de diagnóstico y en el contexto de la siguiente exportación del ZIP de diagnóstico. El botón Diagnóstico del launcher permite extraerlo desde el propio móvil.

## Escena y alcance

Es una escena artificial, local y reproducible, con semilla 271. Utiliza `ArenaRenderer`, los cuatro mapas reales y sus efectos y materiales normales. Mantiene una carga alta de manera sostenida:

| Entidad | Cantidad |
|---|---:|
| Magos, con estados y escudos | 16 |
| Proyectiles | 128 |
| Muros, incluidos piedra, fuego, cascadas y electricidad | 40 |
| Campos y meteoritos | 40 |
| Tramos de canales | 64 |
| Eventos nuevos por fotograma | 160 |
| Partículas activas, límite del renderer | 320 |
| Ondas activas, límite del renderer | 24 |

Los eventos saturan los presupuestos normales; no se amplían sus límites. La escena usa un paso visual fijo de 1/60 s y desactiva el audio. Mantiene los objetos representados, pero no ejecuta la simulación, los bots o las conexiones. Los solapamientos y la carga continua son deliberados. El resultado sirve para comparar coste gráfico con una carga conocida; no representa los FPS de una partida completa.

Cada informe JSON incluye versión, fecha UTC, entorno, mapa, GPU, modelo, sistema operativo, backend y resolución física/lógica. Los tiempos y contadores guardan media, mediana, percentil 95 y máximo. `frameMs` mide tiempo real entre fotogramas; `clientCpuMs` mide creación del estado artificial y actualización de `ArenaRenderer`. Los tiempos del proceso, física y renderer proceden del motor. Los contadores de CPU/GPU de render requieren un viewport que dibuje: se rechaza `--headless`.

Los contadores de GPU, CPU de render y memoria que el backend o la compilación no ofrecen quedan en `null`, sin inferir valores. El calentamiento reduce el efecto de la primera creación de geometría y shaders. Los picos de partículas y ondas permiten comprobar que se conserva el presupuesto.

## Ejecutar desde el código

Después de compilar e importar el cliente con Godot .NET:

```bash
godot --path src/Arcana.Client -- \
  --profile-render --profile-map=Classic \
  --profile-warmup=5 --profile-seconds=30 \
  --profile-environment=pc-local \
  --profile-output=/ruta/informe.json
```

Mapas: `Classic`, `Citadel`, `Ascension`, `Relics`. El calentamiento se limita a 0–30 segundos y la recogida a 1–120 segundos. Sin ruta de salida se usa `user://profiles/latest.json`. El proceso termina después de guardar el informe; `PROFILE_OK` confirma la escritura. Se necesitan al menos tres muestras para una medición completa.

## Móvil físico mediante USB

El script usa ADB del SDK local preparado por `Compilar-APK.cmd`, requiere la APK de desarrollo instalada y un dispositivo con depuración USB autorizada:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/profile-android.ps1 -Map Classic -Warmup 5 -Seconds 30
```

Con varios dispositivos, especificar `-Serial`. Con otro SDK, especificar `-AndroidSdkPath`. El script reinicia la app para medir, espera la confirmación y copia el JSON a `artifacts/render-profile/Android-<mapa>-<fecha>.json`. Distingue móvil físico (`adb-android-physical`) de emulador (`adb-android-emulator`). Se puede repetir en los cuatro mapas y exportar el diagnóstico desde el launcher. La lectura por `run-as` requiere una APK de desarrollo; una APK de publicación requiere extraer el informe mediante Diagnóstico.

## Validación automática e interpretación

`scripts/test-render-profile.sh` ejecuta los cuatro mapas con un viewport OpenGL en Linux. La CI también instala la app en Android 15 y ejecuta la misma escena. `scripts/check-render-profile.py` comprueba formato, muestras, geometría presente, procedencia y presupuestos; no impone un umbral de FPS. Los informes y registros son artefactos de la CI, sin publicar una Release.

Los entornos se identifican como `ci-linux-software-opengl` y `ci-android15-emulator-swangle`. Ambos renderizan por software. Sus medidas comprueban el funcionamiento y permiten detectar cambios en esos entornos; no permiten fijar rendimiento para una GPU de PC o un móvil físico. La medición física necesita ejecutarse en el dispositivo. Para comparar resultados, conservar mapa, versión, resolución, backend y condiciones del dispositivo; una muestra de tres segundos de CI no describe una sesión larga.

## Medición integral de Supervivencia

En **Versus IA → Medir rendimiento de Supervivencia** se ejecuta una escena local con cuatro defensores (tres bots), hasta 192 minions de varios tipos, 96 piezas de equipo, poderes, botín y cruces de portales. Utiliza la simulación real además del renderizador. Los defensores tienen protección artificial para mantener la carga: no sirve para evaluar el equilibrio. No conecta a cuentas ni concede recompensas.

```bash
godot --path src/Arcana.Client -- --profile-render --profile-survival --profile-warmup=5 --profile-seconds=30 --profile-output=/ruta/supervivencia.json
```

El informe se identifica como `integrated_survival_scene` e incluye pasos simulados, cruces de portales, entidades, resolución y percentil 95. La cantidad de enemigos puede variar durante el combate. La CI ejecuta el escenario mediante OpenGL por software; para conocer el rendimiento del móvil hay que ejecutar la misma medición en ese móvil. La prueba integra IA, combate y representación local; los costes de codificación de red se miden aparte con `--defense-benchmark`.
