# Publicación de pruebas de Supervivencia 0.29.30

Versión 0.29.30, código Android 29030 y protocolo 40. Se publica en `pruebas/supervivencia-procedural-autoguardado` con tag `preview-v0.29.30-r<compilación>`. La versión estable en main permanece separada.

## Comportamiento

Las partidas nuevas y reinicios utilizan el generador v2: salas de cantidad, forma, proporción y posición variables, con conexiones rectas, diagonales, curvas, acodadas, de varios giros o paseos irregulares. La anchura cambia entre 8 y 16 m. El presupuesto cooperativo de 10 segundos abarca la geometría, navegación y validación. El reloj solo cancela; no cambia el terreno entre dispositivos. Se conservan las versiones 0 y 1 para continuar sus guardados con el mapa original.

## Validación local

Core y Tests compilados en Release con Roslyn de .NET SDK 8.0.415. Resultado: **541 pruebas correctas, 0 fallidas**, incluidas diversidad, conexión, guardado, recuperación, semillas extremas, expulsión de caché y compatibilidad del generador v1. Los validadores Python de publicación y deltas pasan (10 y 6 pruebas).

Medición después de cuatro calentamientos, con 64 semillas nuevas para cada medida, sin reutilizar la caché:

| Medida | Mediana | P95 | Máximo |
| --- | ---: | ---: | ---: |
| Generación, navegación y validación | 29,072 ms | 54,122 ms | 58,591 ms |
| Creación del mundo, incluido el mapa | 30,878 ms | 43,840 ms | 56,550 ms |

Son dos muestras distintas, no tiempos acumulables. En la muestra de geometría aparecen entre 5 y 15 salas y las seis familias de sala y cinco de pasillo; esos extremos observados no son límites prefijados del generador. Entorno: .NET 8.0.21 x64. No incluye texturas, sonidos ni shaders de Godot, ni representa un Android físico. Datos: [medicion-mapas-supervivencia-0.29.30.json](medicion-mapas-supervivencia-0.29.30.json).

## Paquetes y publicación

CI vuelve a compilar cliente, servidor y pruebas; ejecuta las regresiones, red LAN, validación visual de Godot, reglas con emulador y Docker amd64/arm64. El publicador requiere que todos los trabajos hayan terminado correctamente.

La APK utiliza el certificado permanente. Windows se publica como ZIP y RAR 5: el RAR se prueba, se extrae y se compara cada archivo por SHA-256 contra el ZIP. La barrera de publicación comprueba la firma permanente declarada, los hashes de paquetes y deltas, y también la firma RAR 5 y su manifiesto. Los parches se generan solo contra versiones verificadas del mismo canal y rama, incluida la preliminar 0.29.29 cuando resulta elegible. Los manifiestos y `PREVIEW.json` identifican el commit y la compilación exactos.
