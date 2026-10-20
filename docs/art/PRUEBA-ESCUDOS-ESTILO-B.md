# Escudos estilo B: corrección de integración visual

**Rama de pruebas:** `feature/escudos-visuales-estilo-b` · **Versión:** 0.25.7

## Fallo corregido
La textura anterior incluía un mago ilustrado dentro de cada escudo, de modo que se duplicaba la figura sobre el jugador real. Se ha eliminado ese atlas del repositorio. Los nuevos efectos de escudo personal contienen **solo anillos, runas y partículas**, con una zona central totalmente transparente para mostrar al verdadero personaje. La geometría 3D original del escudo sigue siendo la única referencia volumétrica.

## Recursos reales
- `Assets/VFX/element-style-b.webp`: ocho iconos aprobados para el HUD; atlas 4 × 2, celda 96 px.
- `Assets/VFX/personal-shields-style-b.webp`: ocho escudos personales (E, EW, ER, ES, EF, EQ, ED, EA), sin personajes y con hueco central transparente; atlas 4 × 2, celda 160 px.
- `Assets/VFX/area-shields-style-b.webp`: ocho círculos de defensa en el suelo, alineados horizontalmente para minas y auras; atlas 4 × 2, celda 160 px.
- `Assets/VFX/wall-shields-style-b.webp`: ocho motivos para muros frontales y segmentos de barrera de área; atlas 4 × 2, celda 160 px.

**Correspondencias:** `E` escudo; `W` vida; `R` frío; `S` arcano; `F` fuego; `Q` agua; `D` tierra; `A` rayo.

## Cobertura sin combinaciones ficticias
`ArcanaSpriteAtlas.Defense(form, recipe, layer)` selecciona los componentes por orden canónico, sin depender del orden de pulsación. Las recetas E, EX y EXY combinan 0, 1 o 2 capas por familia, sin dibujar dos veces el mago ni degradarse a un sprite neutro. Las repeticiones (EWW, EDD, etc.) conservan una sola textura y el refuerzo visual de las cargas. Las incompatibilidades se resuelven previamente con `ElementBuffer.Opposite` en la simulación: fuego/frío, vida/arcano, agua/rayo, tierra/rayo y escudo/escudo.

Los escudos personales aparecen alrededor del mago real, los círculos de minas se colocan sobre el suelo y los muros siguen la orientación de sus segmentos 3D. Las barreras de área compuestas de seis muros heredan esos sprites. **No se modifica la física, el daño, las recetas ni el protocolo.**

## Validaciones necesarias
- Compilar C# y comprobar la importación de las tres texturas WebP nuevas.
- Probar escudos personales E, EW, ER, ES, EF, EQ, ED y EA; comprobar que el personaje no queda tapado.
- Probar variantes mixtas válidas como EWQ, ESD, EFD, EQR, con sus dos capas.
- Comprobar áreas (minas de vida y arcanas), muros frontales y segmentos de anillo en móvil y PC.
- Verificar texturas translúcidas en fondos claros, al sufrir daño, al caducar y con muchas barreras.

La imagen de referencia es arte conceptual; los efectos ejecutables se renderizan sobre la geometría existente.
