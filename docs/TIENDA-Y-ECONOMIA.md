# Tienda y economía de equipo

La tienda permanente se abre desde **Tienda en la navegación principal**, junto a Inventario. Requiere una cuenta Google y un servidor conectado a Firebase para consultar ofertas y comprar. El cliente descubre la dirección oficial publicada en Firestore y valida el anuncio antes de entregar la credencial. La dirección se administra desde el despliegue del servidor: no hay botón ni campo editable y se ignora la antigua configuración local.

Sin conexión, la pantalla conserva el título, saldo sin actualizar, controles y seis espacios vacíos; muestra un aviso y no ofrece objetos ni precios. «Consultar» reintenta la conexión. Al fallar una consulta o compra se retiran las ofertas anteriores; al cambiar o cerrar sesión también se limpian. El inventario y la equipación pueden seguir consultándose directamente en Firebase aunque el servidor de compras esté apagado.

Las seis ofertas son personales y se conservan durante el día al cerrar la ventana, reiniciar la aplicación o reiniciar el servidor. Las seis posiciones aparecen en cada catálogo. Se muestran los valores actuales antes de comprar; los límites de los atributos están en sus descripciones. Comprar añade la instancia exacta al inventario y conserva la equipación elegida. Una oferta comprada queda agotada hasta la siguiente renovación.

| Calidad | Precio de compra | Primera mejora en Anillos | Décima mejora | Total de diez mejoras |
|---|---:|---:|---:|---:|
| Plata | 100 | 80 | 4.400 | 17.600 |
| Oro | 250 | 120 | 6.600 | 26.400 |
| Platino | 650 | 180 | 9.900 | 39.600 |
| Diamante | 1.800 | 260 | 14.300 | 57.200 |
| Mítico | 6.000 | 400 | 22.000 | 88.000 |

La tienda gasta **oro permanente de la cuenta**. Las mejoras siguen perteneciendo al **equipo y oro temporales de Anillos**. Las monedas no se transfieren entre ambos. Antes, las diez mejoras costaban 1.925 de oro para cualquier rareza. Ahora la mejora número `n`, de 1 a 10, cuesta `basePorRareza × n × (n + 1) / 2`. Cada mejora sigue sumando un punto porcentual a cada atributo; −6 % termina en +4 %, y +10 % en +20 %.

## Renovación diaria

Las seis ofertas cambian gratis a las **00:00, hora peninsular (Europe/Madrid)**. Se respetan los cambios entre horario de invierno y verano: el día puede durar 23 o 25 horas. El servidor guarda el vencimiento y genera el catálogo nuevo en la primera consulta posterior, sin tareas programadas ni escrituras para cuentas que no entran. Si la tienda está abierta, el cliente consulta las nuevas ofertas al llegar ese vencimiento, usando la hora recibida del servidor y el tiempo transcurrido.

Varias consultas simultáneas, incluso desde dispositivos distintos, comparten una sola renovación guardada en una transacción. Abrir de nuevo dentro del mismo día conserva las ofertas y las compras agotadas. Tras varios días sin entrar se genera únicamente el catálogo actual. El oro y los objetos comprados permanecen en la cuenta. Los catálogos anteriores sin vencimiento se adaptan una sola vez al primer acceso.

La interfaz indica «Renovación diaria · 00:00». El indicador no permite renovar manualmente. No se ejecuta la simulación ni se envían solicitudes de anuncios desde la interfaz. Los endpoints de inicio y finalización de anuncios devuelven 503 sin renovar ni modificar el saldo.

El código de transacciones de la simulación permanece reservado para desarrollo, sin acceso por la API. Una integración futura deberá verificar la recompensa con el proveedor antes de habilitar la renovación. No hay SDK de AdMob, cobros ni ingresos publicitarios.

## Persistencia y validación

El servidor guarda `users/{uid}/shop/current`, `users/{uid}/shop/simulatedAd` y `users/{uid}/shopPurchases/{itemId}`. El SDK cliente no puede leerlos ni escribirlos directamente. La API obtiene la UID de la sesión verificada; el cliente envía únicamente identificadores de catálogo, oferta y anuncio. No envía precios, rarezas, valores de atributos ni saldos.

Cada compra actualiza en una transacción el saldo, el contador de objetos, el inventario, la oferta agotada y el recibo. Las compras simultáneas comparten el mismo saldo autoritativo. Una oferta caducada se rechaza aunque todavía no se haya consultado el catálogo nuevo. Revender la pieza no elimina su recibo de compra; reintentar una compra ya registrada de otro día no cobra ni entrega el objeto otra vez.

La renovación requiere desplegar el servidor actualizado. Los clientes anteriores pueden consultar el catálogo diario al abrir la tienda; la actualización de una tienda que sigue abierta requiere también el cliente nuevo. Los campos opcionales `refreshAt` y `serverTime` mantienen la compatibilidad de las respuestas. El protocolo de combate no cambia y el servidor conserva la decisión final sobre el gasto de oro.

## Regresiones corregidas

- Cada operación de cuenta captura su identidad y credencial. Antes de publicar una respuesta comprueba que la sesión sigue vigente. Un resultado o error 401 de la cuenta anterior no altera la nueva; terminar un cierre de sesión antiguo tampoco la borra.
- En Anillos se conserva el estado de combate al salir voluntariamente y volver con la misma cuenta: recursos, efectos, protección restante, escudos, posición, recargas y reaparición. El nuevo saludo de red no vuelve a conceder invulnerabilidad. La reconexión de transporte mantiene su reserva habitual de 30 segundos.

Las pruebas automáticas cubren compras y anuncios contra el emulador Firestore y HTTP real, aislamiento entre cuentas y reproducciones de los dos bugs. La interfaz se verifica con Godot en escritorio y móvil simulado a 720 × 540; la validación en un Android físico sigue pendiente.
