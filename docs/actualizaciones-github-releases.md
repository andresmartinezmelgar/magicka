# Actualizaciones de Arcana Arena mediante GitHub Releases

## Funcionamiento
El cliente Android y Windows consulta al arrancar la última Release pública de `andresmartinezmelgar/magicka`. Si su `tag_name` es una versión mayor que `BuildInfo.Version` y contiene la APK ARM64 o el ZIP Windows esperado, ofrece **Actualizar**. El botón abre la descarga oficial en el navegador. Android solicitará permiso para instalar el APK; **no existe instalación silenciosa**. Sin red o sin Release el juego funciona igual y la práctica offline permanece disponible.

El backend puede desplegarse sin nueva APK cuando conserva compatibilidad de protocolo. Cambios de C#, escenas o sprites empaquetados requieren exportar el cliente otra vez.

## Requisito imprescindible: firma Android permanente
Actualmente las APK Android de CI se firman con un almacén de depuración **nuevo en cada ejecución**. Estas APK no pueden actualizarse unas a otras ni actualizar una instalación firmada con otra clave. La Release vuelve a firmar la APK con la clave permanente, pero **la primera instalación de esta nueva línea de firma podría exigir desinstalar la APK anterior**. Guardar previamente los datos que no estén sincronizados.

Crear una sola clave de distribución y hacer copia de seguridad cifrada fuera de Git:
```bash
keytool -genkeypair -v -keystore arcana-release.jks -alias arcana -keyalg RSA -keysize 3072 -validity 10000
base64 -w0 arcana-release.jks
```
En GitHub, Settings → Secrets and variables → Actions, crear estos cuatro **repository secrets**:
- `ARCANA_KEYSTORE_BASE64`: salida Base64 de la clave .jks
- `ARCANA_KEYSTORE_PASSWORD`: contraseña del almacén
- `ARCANA_KEY_ALIAS`: alias (p. ej. arcana)
- `ARCANA_KEY_PASSWORD`: contraseña de la clave

**No compartir ni subir el JKS, sus contraseñas o su Base64 al repositorio.** En Windows, usar `[Convert]::ToBase64String([IO.File]::ReadAllBytes('arcana-release.jks'))` para generar Base64.

## Publicación
1. Incrementar `VERSION` y ejecutar `python3 scripts/version-support.py sync .` para actualizar los metadatos de Android y Windows.
2. Corregir todas las pruebas de CI: la Release exige que los cuatro trabajos principales terminen correctamente.
3. En Actions → Arcana Arena CI → Run workflow, seleccionar `publish_release=true`. El flujo valida, descarga los artefactos, exige los cuatro secretos, vuelve a firmar la APK con la misma clave permanente, verifica la firma y publica los binarios en la Release `vVERSION`.
4. Instalar la primera APK de Release. Las siguientes se podrán instalar encima si el identificador `com.arcana.arena`, la clave y `version/code` se mantienen.

**No publicar artefactos de prueba como Releases**: están firmados con claves efímeras. No cambiar nunca la clave de distribución sin planificar la migración. La Release no se publica automáticamente con cada commit, solo mediante el disparador manual autorizado tras CI verde.

## Pendiente
La descarga se abre en el navegador; no se instala directamente dentro de Godot. Windows descarga el ZIP y el usuario sustituye la carpeta del ejecutable. Google Play puede gestionar futuras actualizaciones si se migra a distribución mediante la tienda.
