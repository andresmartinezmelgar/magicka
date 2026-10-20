# Escudos e iconos estilo B: integrados en rama de pruebas

Rama: `feature/escudos-visuales-estilo-b`. Prueba: `0.25.6`.

## Activos gráficos

- `src/Arcana.Client/Assets/VFX/element-style-b.webp`: atlas de 8 emblemas aprobados, 4 columnas y 2 filas de 96 × 96 px. Orden: agua, vida, escudo, frío, rayo, arcano, tierra, fuego.
- `src/Arcana.Client/Assets/VFX/shields-style-b.webp`: atlas de 12 propuestas de escudos recortadas de los bocetos, 4 columnas y 3 filas de 96 × 96 px. Se procesaron los fondos negros para transparencia y se redujo su resolución para la ejecución; no son imágenes originales a tamaño completo.
- Originales sin reducción conservados en la Biblioteca, `/Arcana Arena/Arte aprobado/Elementos/` y archivos de bocetos.
- `ArcanaSpriteAtlas.cs` almacena en caché las imágenes y cada `AtlasTexture`.

## Integración

- `ElementIcon.cs`: el HUD dibuja el emblema aprobado en vez de la forma vectorial; fallback a dibujo vectorial cuando falta el recurso.
- `WizardVisual.Protection.cs`: texturas personales E, EW, ER, ES sobre la burbuja existente y cristales animados; no cambian las colisiones o puntuaciones.
- `BarrierVisual.cs`: texturas de defensa de campo (ED, EDF, EQR y ES) y círculos elementales sobre geometría 3D; fade sincronizado con la vida restante.
- `AreaFieldVisual.cs`: adornos de textura sobre minas arcanas y de sanación; no modifican los radios reales.
- Los anillos de seis muros reutilizan las representaciones gráficas de sus segmentos.
- Se conserva íntegra la lógica, daño, duración, protocolo y cálculo de hechizos.

## Validaciones

La compilación Godot Mono, la importación WebP, los smoke tests y la exportación Windows/Android se comprueban en GitHub Actions para esta rama. Antes de fusionar, revisar visualmente la transparencia y visibilidad sobre fondos claros, tamaños de pantalla pequeños y rendimiento cuando coincidan múltiples barreras.
