# Cuentas, objetos y oro

El código incluye la integración, pero no ha creado ninguna cuenta ni proyecto Firebase y no incluye contraseñas de Google. El acceso utiliza la cuenta Google del jugador. El alojamiento del servidor continúa sin cambios.

## Activar el servidor central

El cliente Godot de Android y Windows abre el navegador del sistema para entrar con Google. La página de acceso la sirve el propio servidor; utiliza Firebase Authentication y el SDK web fijado en 12.4.0. El servidor verifica la firma, caducidad, proyecto, revocación y proveedor Google mediante Firebase Admin 3.7.0. El juego recibe una sesión opaca de ocho horas, conservada solo en memoria. Al reiniciar el servidor o el cliente es necesario iniciar sesión otra vez; la UID y los datos permanecen.

1. Crea un proyecto Firebase con Cloud Firestore en modo nativo. Activa Google en Authentication → Sign-in method.
2. Registra una aplicación web de Firebase y copia su `projectId`, `apiKey` y `authDomain`. Añade el dominio público del juego a los dominios autorizados de Authentication.
3. Configura una cuenta de servicio del servidor con permisos de lectura de usuarios Firebase Authentication y de lectura/escritura Firestore. Utiliza credenciales de aplicación de Google Cloud o guarda su JSON privado fuera del repositorio y señala su ruta con `GOOGLE_APPLICATION_CREDENTIALS`. El cliente y la APK no incluyen esa credencial.
4. Publica el servidor detrás de un proxy HTTPS que también permita WebSockets. `ARCANA_PUBLIC_URL` debe contener la dirección HTTPS pública. El cliente utiliza su equivalente `wss://…/play`. El puerto HTTP interno del servidor se utiliza entre el proxy y el proceso del juego.
5. Despliega `firebase/firestore.rules`: las aplicaciones cliente no acceden directamente a la base de datos. La API del servidor administra las operaciones con su cuenta de servicio.
6. Mantén `ARCANA_REWARD_OUTBOX` en un volumen persistente, compartido por los reinicios de esa instancia. Cada servidor debe tener su propio directorio. Los reintentos contra la misma base de datos no duplican premios.

Ejemplo de entorno (valores públicos de ejemplo; la credencial privada se configura mediante una ruta):

```bash
export FIREBASE_PROJECT_ID=mi-proyecto
export FIREBASE_API_KEY=clave-publica-de-la-aplicacion-web
export FIREBASE_AUTH_DOMAIN=mi-proyecto.firebaseapp.com
export ARCANA_PUBLIC_URL=https://arena.example.com
export GOOGLE_APPLICATION_CREDENTIALS=/srv/arcana/secrets/firebase.json
export ARCANA_REWARD_OUTBOX=/srv/arcana/data/rewards
```

Si no se configura Firebase, el servidor conserva las partidas habituales y comunica que las cuentas están desactivadas. Un servidor local incluido en el paquete Windows no hereda credenciales Firebase del entorno. Las recompensas permanentes pertenecen al servidor central configurado.

## Objetos procedurales

Cada objeto es una instancia con ID propio. Botas: velocidad; gorro: alcance; pecho: defensa; arma: ataque, curación, absorción o potencia del elemento según su familia. El atributo principal es positivo. Plata, Oro y Platino admiten atributos secundarios variados y penalizaciones. Diamante y Mítico tienen una familia —Agua, Vida, Escudo, Frío, Rayo, Arcano, Tierra o Fuego— y secundarios positivos de esa familia. Los ocho elementos tienen la misma probabilidad al elegir una familia.

| Calidad | Probabilidad | Atributos secundarios | Oro al eliminar |
| --- | ---: | ---: | ---: |
| Plata | 48 % | 1 | 10 |
| Oro | 27 % | 2 | 25 |
| Platino | 15 % | 2 | 60 |
| Diamante | 8 % | 2 | 150 |
| Mítico | 2 % | 3 | 400 |

El catálogo define mínimos, máximos, posiciones y familias permitidas. Cada instancia guarda `minimum`, `maximum` y `current` para cada atributo, además de `generationVersion`. El sorteo inicial tiene un presupuesto por calidad; los intervalos originales se conservan para permitir futuras mejoras con oro. La mejora todavía no está habilitada en esta versión.

| Familia | Principal del arma | Secundarios posibles |
| --- | --- | --- |
| Agua | Potencia de agua | Potencia de agua, duración de mojado, alcance y regeneración de energía |
| Vida | Curación | Curación, regeneración de energía, ahorro al reanimar y duración del escudo |
| Escudo | Absorción del escudo | Absorción, duración, defensa y regeneración de energía |
| Frío | Daño de frío | Daño de frío, potencia de enfriamiento, duración de congelación y regeneración de energía |
| Rayo | Daño de rayo | Daño de rayo, bonificación contra mojados, alcance y regeneración de energía |
| Arcano | Daño arcano | Daño arcano, rapidez de carga del haz, alcance y regeneración de energía |
| Tierra | Daño de tierra | Daño de tierra, rapidez de carga de rocas, defensa y absorción del escudo |
| Fuego | Daño de fuego | Daño de fuego, duración de quemaduras, ataque y regeneración de energía |

El principal de botas, gorro y pecho sigue siendo velocidad, alcance y defensa, respectivamente. No se repite un atributo dentro de una pieza. Plata, Oro y Platino permiten cualquier secundario del catálogo, incluidas las nuevas estadísticas elementales.

| Calidad | Principal | Cada secundario | Número de secundarios | Presupuesto positivo inicial |
| --- | --- | --- | ---: | ---: |
| Plata | +1 a +5 % | −4 a +6 % | 1 | 10 % |
| Oro | +2 a +7 % | −3 a +8 % | 2 | 19 % |
| Platino | +3 a +9 % | −2 a +10 % | 2 | 25 % |
| Diamante | +6 a +11 % | +4 a +10 % | 2 | 30 % |
| Mítico | +8 a +13 % | +7 a +14 % | 3 | 46 % |

La potencia de agua aumenta su empuje y presión sobre protecciones, además de su componente de daño en vapor e hielo. El agua pura mantiene su efecto de estado. La bonificación de Rayo contra mojados multiplica el daño eléctrico solo cuando el objetivo está mojado, además del multiplicador base de mojado. Las cargas rápidas del haz Arcano y las rocas de Tierra aceleran la llegada al máximo; no elevan el techo de potencia ni la curación del haz.

Las cuatro posiciones suman atributos y el núcleo calcula una sola vez los modificadores efectivos. Límites del equipo: +30 % velocidad, +35 % alcance, 35 % reducción de daño, +40 % ataque, +50 % potencia o daño de Agua, Frío, Rayo, Arcano, Tierra y Fuego; +60 % curación, absorción, duración de escudos, energía, quemaduras, duración de mojado, enfriamiento, congelación y rapidez de cargas. La bonificación adicional de Rayo contra mojados alcanza el 35 %. El ahorro al reanimar alcanza el 35 %. Las penalizaciones agregadas se limitan al 25 %. Las reliquias temporales y las afinidades de clase mantienen sus reglas y se combinan con el equipo.

## Conjuntos Míticos

| Familia | 2 o 3 piezas | 4 piezas |
| --- | --- | --- |
| Agua | +5 % potencia de agua | +15 % potencia de agua y +20 % duración de mojado |
| Vida | +5 % curación | +15 % curación y +10 % ahorro al reanimar |
| Escudo | +10 % duración | +20 % duración y +20 % absorción |
| Frío | +5 % daño de frío | +15 % daño de frío y +20 % duración de congelación |
| Rayo | +5 % daño de rayo | +15 % daño de rayo y +20 % bonificación contra mojados |
| Arcano | +5 % daño arcano | +15 % daño arcano y +20 % rapidez de carga del haz |
| Tierra | +5 % daño de tierra | +15 % daño de tierra y +20 % rapidez de carga de rocas |
| Fuego | +5 % daño de fuego | +15 % daño de fuego y +20 % duración de quemadura |

El bonus de cuatro sustituye al de dos. Se pueden activar dos conjuntos de dos piezas. Vida reduce el coste de Reanimar: los hechizos normales de curación ya son gratuitos. Los botones de PC y móvil muestran el coste efectivo.

## Uso y recompensas

El equipamiento se selecciona desde **Inventario y mago**, en el launcher, o **Personalizar mago e inventario**, en la sala. Las flechas de cada posición recorren sus objetos y la opción **Sin equipo**; pasar del final al inicio funciona en ambos sentidos. Pulsar el nombre de una posición permite ver sus atributos individuales: valor actual, mínimo y máximo. La tabla **Suma de todo el equipo** separa las piezas, el conjunto, la suma y el valor aplicado después de los límites. Cada cambio se guarda automáticamente. Se puede usar en PvP del servidor central, partidas contra bots de ese servidor y práctica local. La última cuenta cargada conserva una caché de objetos para jugar contra IA sin conexión y permite seleccionar piezas para esa práctica. El servidor central siempre recupera su propio equipamiento guardado antes de admitir al jugador: no acepta estadísticas enviadas por el cliente.

Una victoria PvP válida entrega un objeto. Debe haber cuentas distintas de humanos rivales con al menos 15 segundos de participación activa; lanzar continuamente y disputar puntos también cuentan. Una partida contra bots, práctica, cuentas repetidas o participantes inactivos no concede progresión. Los perdedores elegibles registran su derrota. El diario de resultados se entrega fuera del bucle de simulación y se reintenta tras fallos o reinicios. La partida no espera a Firestore para avanzar. Si el inventario alcanza 500 objetos, el premio se convierte automáticamente en su valor de oro.

El equipo de una partida permanece fijado hasta la siguiente ronda. Morir o reaparecer conserva las piezas. El cliente recibe una notificación al guardar el resultado y actualiza su inventario. Los sprites y la apariencia del mago no cambian al equipar objetos. La calidad y la familia se indican con texto en el selector; los cambios visuales de indumentaria quedan para otra versión. La selección en la cuenta permanece al cambiar de partida, de sesión y de versión; la selección sin conexión permanece en la caché del dispositivo para las partidas locales contra IA.

Eliminar una pieza solicita confirmación y realiza una transacción: retira el objeto, lo desequipa si corresponde, aumenta el oro y registra un recibo de venta. Un reintento no paga dos veces. El oro se conserva para versiones futuras; no hay compra, mejora ni fabricación en esta versión.

## Documentos y compatibilidad

| Ruta | Datos |
| --- | --- |
| `users/{uid}` | Perfil, oro entero de 64 bits, victorias, derrotas, número de objetos y `schemaVersion` |
| `users/{uid}/items/{itemId}` | Posición, calidad, familia, tiradas con mínimo/máximo/actual, valor de venta, origen y versión de generación |
| `users/{uid}/loadouts/active` | IDs de las cuatro piezas seleccionadas |
| `users/{uid}/rewards/{matchId}` | Resultado y premio ya entregado |
| `users/{uid}/sales/{itemId}` | Oro entregado por una eliminación |
| `matches/{matchId}` | Resultado del servidor y participantes elegibles |
| `bonusDefinitions/{stat}` | Catálogo publicado por el servidor |
| `catalogues/{generationVersion}/bonusDefinitions/{stat}` | Definiciones archivadas por versión de generación |

La UID, el esquema y las versiones de generación no dependen de `VERSION`. Actualizar el juego no crea otra cuenta ni vuelve a sortear objetos. Las futuras migraciones deben añadir o transformar campos conservando las instancias y sus intervalos. La ampliación a ocho familias utiliza `EquipmentCatalogue.GenerationVersion = 2`; los objetos de generación 1 siguen siendo válidos. Las definiciones nuevas se archivan bajo `catalogues/2` y no sobrescriben `catalogues/1`. Un cambio futuro del catálogo debe aumentar esa versión. El protocolo de partida sigue exigiendo cliente y servidor compatibles: esta versión utiliza el protocolo 13.

## Validación

`bash scripts/test-accounts.sh` descarga el emulador oficial Firestore 1.19.8, verifica su SHA256 y ejecuta pruebas sobre el proyecto local `demo-arcana-accounts`. No utiliza la base real. El verificador Google se sustituye solo dentro del proyecto de pruebas; el servidor distribuido utiliza Firebase Admin.

`bash scripts/test-client-smoke.sh equipment-smoke RUTA_A_GODOT` comprueba las flechas, atributos y dos partidas IA, y abre un segundo proceso para verificar la recuperación del equipo desde disco. El perfil de prueba utiliza un directorio temporal aislado.

Las comprobaciones cubren premios concurrentes, venta repetida, precisión de oro de 64 bits, propiedad, venta frente a equipamiento simultáneo, persistencia de tiradas y de las cuatro posiciones tras una nueva instancia y sesión, sesiones falsas, fin de partida y recuperación del diario. El acceso Google real requiere el proyecto y dominio configurados; las pruebas del emulador no sustituyen una comprobación de OAuth con las cuentas del proyecto de producción.
