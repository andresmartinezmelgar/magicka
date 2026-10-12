# Arcana Arena 0.14.0

La equipación cubre Agua, Vida, Escudo, Frío, Rayo, Arcano, Tierra y Fuego. Las botas mantienen velocidad como principal, el gorro alcance y el pecho defensa. El principal del arma depende de su familia. Diamante y Mítico tienen secundarios positivos centrados en la familia; Plata, Oro y Platino mezclan cualquiera de los 22 atributos, con las compensaciones y rangos anteriores.

## Combate

- Agua: potencia aplicada a su componente de daño en vapor e hielo, empuje y presión sobre protecciones; duración de mojado. El agua pura mantiene el efecto de estado.
- Frío: daño elemental, acumulación de enfriamiento y duración de congelación. Fuego sigue limpiando el frío y descongelando.
- Rayo: daño eléctrico y una bonificación adicional solamente contra objetivos mojados.
- Arcano: daño elemental y velocidad de carga del haz ofensivo. La curación y la potencia máxima del haz se conservan.
- Tierra: daño elemental y velocidad de carga de las rocas. La carga completa conserva su potencia máxima.
- Vida, Escudo y Fuego conservan sus atributos y conjuntos anteriores.

Cada familia tiene un conjunto Mítico de dos o tres piezas y otro de cuatro. El de cuatro reemplaza al de dos. Las cifras, rangos y límites están en [PROGRESSION.md](PROGRESSION.md).

## Interfaz y persistencia

Las flechas mantienen el guardado automático. La tabla muestra las cuatro estadísticas generales y todos los atributos usados por las piezas o conjuntos, incluidos los que se compensan hasta cero. El botón Conjuntos permite consultar las ocho familias con desplazamiento en móvil y escritorio.

Se conserva el esquema de cuenta 1. Los IDs de familias y atributos antiguos mantienen sus valores. El catálogo de generación 2 publica y archiva las definiciones nuevas sin sobrescribir las de generación 1 ni volver a sortear las piezas existentes. Cada objeto mantiene su mínimo, máximo, valor actual, ID y versión de generación.

Cliente y servidor utilizan protocolo 13 y deben actualizarse juntos. El alojamiento, las credenciales Firebase, los sprites y los recursos de los magos no se han modificado. La mejora mediante oro continúa pendiente.

## Validación local

- Compilación Core, Server, pruebas y cuentas en Release; cliente en Debug y Release. Compilador Roslyn oficial del SDK .NET 8.0.400 y generadores Godot 4.5.2. La CLI MSBuild de este entorno falla al consultar los datos del proceso; los scripts de compilación del proyecto mantienen su flujo habitual.
- 179 pruebas generales: sin fallos. Incluyen 10 regresiones de los ocho conjuntos, generación, daño real por componente, agua y protecciones, frío y descongelación, rayo contra mojados, cargas aceleradas, límites y objetos antiguos.
- 14 pruebas con emulador Firestore y servidor real: sin fallos. Las cuatro piezas de cada una de las ocho familias conservan selección y tiradas al cambiar de repositorio; las definiciones del catálogo 1 no se sobrescriben.
- 21 pruebas de red: sin fallos, incluido descubrimiento UDP y rechazo de protocolos incompatibles.
- Godot: equipo y reapertura en proceso nuevo en escritorio y móvil; ocho familias, atributos, flechas, conjuntos, caché y ocho partidas contra IA; sprites, práctica y 120 combinaciones con diez escenas.
- Capturas OpenGL revisadas: Frío en disposición móvil y Arcano en escritorio; no sustituyen una prueba en un teléfono físico.
- Exportación: versión 0.14.0, Android 14000 y protocolo 13. YAML y script de pruebas comprobados. Soporte de versiones: seis pruebas correctas y dos omitidas por limitaciones del entorno.

La validación local no genera los paquetes Android ni Windows. Su exportación y publicación utilizan GitHub Actions.
