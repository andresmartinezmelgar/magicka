# Directorio de servidores y tráfico PvP

## Activación en Firebase

1. En el proyecto `arcana-arena-f2997`, publica [las reglas de este repositorio](../../firebase/firestore.rules). Mantén las reglas de cuentas, inventario y estadísticas junto al bloque de PvP.
2. En la base `(default)` crea la colección `publicConfig` y el documento `pvpServer`. Este documento admite un único campo llamado `url`, de tipo cadena, con el valor `wss://familiamelgar.duckdns.org/play`. Se permite leer ese documento individual. Las escrituras quedan reservadas a operadores con permisos en el proyecto.
3. El cliente, las operaciones de tienda y la entrega de estadísticas locales usan esa dirección al actualizar el juego. El panel web alojado en la Raspberry usa su propio origen. El panel que se abre en el PC lee el documento. La caché del directorio dura 60 segundos. Una partida abierta conserva su conexión.

Si se arranca el servidor integrado actualizado con sus credenciales habituales y una `ARCANA_PUBLIC_URL` HTTPS estable, crea el documento únicamente cuando falta y tras comprobar que la dirección pública lleva a su propia instancia. Conserva las ediciones hechas desde Firebase. El anuncio anterior de los túneles temporales mantiene su vencimiento y su ruta independiente. Un operador también puede publicar o reemplazar la URL desde el contenedor actualizado con las credenciales del proyecto.

```bash
docker compose exec server dotnet Arcana.Server.dll --publish-pvp-server wss://familiamelgar.duckdns.org/play
```

## Medición por jugador

Sesiones locales reproducibles de 60 segundos contra la simulación de la versión 0.29.70. El mismo estado se entregó a ambos codificadores. Cada sesión incluye 1.800 entradas de movimiento y apuntado, hechizos, ping, rendimiento, 1.200 estados y 30 envíos de base. Se perdió deliberadamente una confirmación de base. Los receptores publicado y actual reconstruyeron todos los estados con los mismos valores. Se comprobaron también las once acciones binarias y las secuencias hasta `long.MaxValue`.

| Partida | Subida anterior | Subida nueva | Bajada anterior | Bajada nueva | Ahorro total |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 contra 1 | 48.962 B | 37.574 B | 369.478 B | 353.631 B | 6,51 % |
| 2 contra 2 | 48.962 B | 37.574 B | 638.492 B | 630.674 B | 2,79 % |
| 4 contra 4 | 48.962 B | 37.574 B | 1.058.369 B | 1.045.532 B | 2,19 % |

Las cifras corresponden al contenido de los mensajes WebSocket. Inicio de sesión, saludo inicial, informes de estadísticas, cabeceras de red y retransmisiones quedan fuera de la medición. El consumo de datos móviles depende también de esos conceptos y de cómo se desarrolle la partida.

El número de secuencia ocupa entre uno y diez bytes en las acciones, según su magnitud. Cliente y servidor acuerdan este formato al conectar. Las combinaciones con versiones anteriores conservan el formato original. Los campos de apuntado y movimiento mantienen su precisión anterior. Los estados pequeños se comprimen cuando el ahorro compensa la cabecera. Las bases periódicas omiten campos de otros mensajes. Las listas cortas de valores que cambian juntas pueden enviarse completas para ahorrar índices.

En la muestra 4 contra 4, el percentil 95 del tiempo de codificación por estado pasó de 0,41 a 0,49 ms en el equipo de prueba. La simulación mantuvo 20 estados y 30 entradas por segundo. La medición en la Raspberry y en un Android físico queda pendiente.

Para repetir la medición se utiliza `Arcana.Core.dll` de la versión anterior y el SDK .NET 10. El comando construye únicamente la herramienta de medición.

```bash
dotnet run -c Release --project tools/PvpWireReplay -p:BaselineCore=/ruta/0.29.70/Arcana.Core.dll
```

## Consolidación de dependencias

Se conservaron los commits de todas las ramas de prueba y de las propuestas automáticas recibidas durante la integración. Se aplicaron doce propuestas de dependencias. Las versiones de Firebase coinciden en los dos servidores y las del complemento Android coinciden con las que declara el exportador. Las pruebas de reglas usan Node 24 y Java 21.

Las propuestas individuales para Godot Android 4.7.2 y Android Gradle Plugin 9.5.2 están en el historial de `main`, en `299ffc6` y `9971ce7`. Se conserva el conjunto de herramientas de publicación que requiere Godot .NET 4.5.2 y Gradle 8.9. Cambiar solo estas dos referencias dejaría desalineados el cliente, las plantillas de exportación y los scripts del APK.
