# Tienda y economía de equipo (0.24.1)

La tienda permanente se abre desde **Tienda en la navegación principal**, junto a Inventario. Requiere una cuenta Google y un servidor conectado a Firebase para consultar ofertas y comprar. El cliente descubre la dirección oficial publicada en Firestore y valida el anuncio antes de entregar la credencial. La dirección se administra desde el despliegue del servidor: no hay botón ni campo editable y se ignora la antigua configuración local.

Sin conexión, la pantalla conserva el título, saldo sin actualizar, controles y seis espacios vacíos; muestra un aviso y no ofrece objetos ni precios. «Consultar» reintenta la conexión. Al fallar una consulta o compra se retiran las ofertas anteriores; al cambiar o cerrar sesión también se limpian. El inventario y la equipación pueden seguir consultándose directamente en Firebase aunque el servidor de compras esté apagado.

Las seis ofertas son personales y se conservan al cerrar la ventana, reiniciar la aplicación o reiniciar el servidor. Las cuatro posiciones aparecen en cada catálogo. Se muestran los valores actuales antes de comprar; los límites de los atributos están en sus descripciones. Comprar añade la instancia exacta al inventario y conserva la equipación elegida. Una oferta comprada queda agotada hasta renovar.

| Calidad | Precio de compra | Primera mejora en Anillos | Décima mejora | Total de diez mejoras |
|---|---:|---:|---:|---:|
| Plata | 100 | 80 | 4.400 | 17.600 |
| Oro | 250 | 120 | 6.600 | 26.400 |
| Platino | 650 | 180 | 9.900 | 39.600 |
| Diamante | 1.800 | 260 | 14.300 | 57.200 |
| Mítico | 6.000 | 400 | 22.000 | 88.000 |

La tienda gasta **oro permanente de la cuenta**. Las mejoras siguen perteneciendo al **equipo y oro temporales de Anillos**. Las monedas no se transfieren entre ambos. Antes, las diez mejoras costaban 1.925 de oro para cualquier rareza. Ahora la mejora número `n`, de 1 a 10, cuesta `basePorRareza × n × (n + 1) / 2`. Cada mejora sigue sumando un punto porcentual a cada atributo; −6 % termina en +4 %, y +10 % en +20 %.

## Renovación por anuncios desactivada

«Renovar · próximamente» permanece deshabilitado; en móvil se acorta a «Renovar» y la descripción indica que todavía no está disponible. No se ejecuta la simulación ni se envían solicitudes de anuncios desde la interfaz. Los endpoints de inicio y finalización devuelven 503 sin renovar ni modificar el saldo; un cliente anterior tampoco puede renovar en un servidor actualizado. Consultar, cerrar y volver a abrir no renueva el catálogo.

El código de transacciones de la simulación permanece reservado para desarrollo, sin acceso por la API. Una integración futura deberá verificar la recompensa con el proveedor antes de habilitar la renovación. No hay SDK de AdMob, cobros ni ingresos publicitarios.

## Persistencia y validación

El servidor guarda `users/{uid}/shop/current`, `users/{uid}/shop/simulatedAd` y `users/{uid}/shopPurchases/{itemId}`. El SDK cliente no puede leerlos ni escribirlos directamente. La API obtiene la UID de la sesión verificada; el cliente envía únicamente identificadores de catálogo, oferta y anuncio. No envía precios, rarezas, valores de atributos ni saldos.

Cada compra actualiza en una transacción el saldo, el contador de objetos, el inventario, la oferta agotada y el recibo. Las compras simultáneas comparten el mismo saldo autoritativo. Revender la pieza no elimina su recibo de compra. Renovar o abrir la tienda no restaura ofertas anteriores.

La tienda requiere servidor 0.24.1 para aplicar también el bloqueo de anuncios. El protocolo de combate sigue siendo 27. Se deben actualizar cliente y servidor juntos; el servidor conserva la decisión final sobre el gasto de oro.

## Regresiones corregidas

- Cada operación de cuenta captura su identidad y credencial. Antes de publicar una respuesta comprueba que la sesión sigue vigente. Un resultado o error 401 de la cuenta anterior no altera la nueva; terminar un cierre de sesión antiguo tampoco la borra.
- En Anillos se conserva el estado de combate al salir voluntariamente y volver con la misma cuenta: recursos, efectos, protección restante, escudos, posición, recargas y reaparición. El nuevo saludo de red no vuelve a conceder invulnerabilidad. La reconexión de transporte mantiene su reserva habitual de 30 segundos.

Las pruebas automáticas cubren compras y anuncios contra el emulador Firestore y HTTP real, aislamiento entre cuentas y reproducciones de los dos bugs. La interfaz se verifica con Godot en escritorio y móvil simulado a 720 × 540; la validación en un Android físico sigue pendiente.
