# Escudos estilo B: revisión 0.25.8

Rama: `feature/escudos-visuales-estilo-b`.

## Escudo sobre mí
Se ha restaurado **exactamente** el código original de `WizardVisual.Protection.cs`: burbuja 3D, anillos de cargas, protecciones originales y armadura de piedra. No hay sprites personales superpuestos ni adornos de cristales u órbitas nuevos. Se ha retirado también el archivo de atlas de escudo personal, que ya no es necesario.

## Escudos de área y sobre el terreno
Se mantienen los atlas y efectos nuevos de `area-shields-style-b.webp` y `wall-shields-style-b.webp`; los iconos del HUD siguen usando el estilo B. Las combinaciones de afinidades compatibles se conservan para los escudos de área y muros.

## Minas: mostrar la zona donde tienen efecto
El `FieldSnapshot.Radius` de minas ofensivas y curativas es **2,4 m** de alcance del efecto. `TriggerRadius` es **0,85 m** para el disparador y no debe confundirse con su alcance. El límite exterior en `AreaBoundary` tiene escala `Radius` y la imagen del suelo cubre exactamente el diámetro `2 × Radius`, sin presentar el radio de activación como segundo círculo. El núcleo queda reducido a un pequeño marcador central.

No cambian los valores de activación, duración, daños, curación, físicas, reglas, servidor ni red.

## Pruebas de regresión
Los smoke tests visuales verifican la ausencia de adornos personales nuevos y que el límite exterior y el atlas de la mina muestren el radio real de efecto. GitHub Actions valida compilación y exportaciones Android y Windows.
