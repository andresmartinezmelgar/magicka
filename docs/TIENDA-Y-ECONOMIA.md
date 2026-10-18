# Tienda y economía de equipo (0.24.0)

La tienda permanente se abre en **Inventario → Tienda**. Requiere una cuenta Google y un servidor conectado a Firebase. Si no se ha configurado un servidor de operaciones, el cliente descubre la dirección oficial publicada en Firestore; valida el anuncio antes de entregar la credencial. También se puede configurar una URL HTTPS autorizada en Inventario → Servidor.

Las seis ofertas son personales y se conservan al cerrar la ventana, reiniciar la aplicación o reiniciar el servidor. Las cuatro posiciones aparecen en cada catálogo. Se muestran los valores actuales antes de comprar; los límites de los atributos están en sus descripciones. Comprar añade la instancia exacta al inventario y conserva la equipación elegida. Una oferta comprada queda agotada hasta renovar.

| Calidad | Precio de compra | Primera mejora en Anillos | Décima mejora | Total de diez mejoras |
|---|---:|---:|---:|---:|
| Plata | 100 | 80 | 4.400 | 17.600 |
| Oro | 250 | 120 | 6.600 | 26.400 |
| Platino | 650 | 180 | 9.900 | 39.600 |
| Diamante | 1.800 | 260 | 14.300 | 57.200 |
| Mítico | 6.000 | 400 | 22.000 | 88.000 |

La tienda gasta **oro permanente de la cuenta**. Las mejoras siguen perteneciendo al **equipo y oro temporales de Anillos**. Las monedas no se transfieren entre ambos. Antes, las diez mejoras costaban 1.925 de oro para cualquier rareza. Ahora la mejora número `n`, de 1 a 10, cuesta `basePorRareza × n × (n + 1) / 2`. Cada mejora sigue sumando un punto porcentual a cada atributo; −6 % termina en +4 %, y +10 % en +20 %.

## Renovación por anuncio simulado

«Renovar (demo)» en móvil o «Renovar · anuncio simulado» en escritorio inicia una simulación de tres segundos. Se puede cancelar antes de completar. Completada la simulación, las seis ofertas cambian sin consumir oro. No hay anuncios comerciales, SDK de AdMob, cobros ni ingresos publicitarios.

El servidor crea un identificador de anuncio vinculado a la cuenta y al catálogo. Comprueba la hora mínima, la caducidad y que no se haya consumido; una respuesta perdida puede reintentarse sin obtener una segunda renovación. Esto reproduce el flujo de la interfaz y de las transacciones. Una integración futura con anuncios reales deberá sustituir la simulación por la verificación del proveedor: la espera por sí sola no demuestra la visualización de un anuncio.

## Persistencia y validación

El servidor guarda `users/{uid}/shop/current`, `users/{uid}/shop/simulatedAd` y `users/{uid}/shopPurchases/{itemId}`. El SDK cliente no puede leerlos ni escribirlos directamente. La API obtiene la UID de la sesión verificada; el cliente envía únicamente identificadores de catálogo, oferta y anuncio. No envía precios, rarezas, valores de atributos ni saldos.

Cada compra actualiza en una transacción el saldo, el contador de objetos, el inventario, la oferta agotada y el recibo. Las compras simultáneas comparten el mismo saldo autoritativo. Revender la pieza no elimina su recibo de compra. Renovar o abrir la tienda no restaura ofertas anteriores.

La tienda requiere servidor 0.24.0. El protocolo de combate sigue siendo 27. Para utilizar los nuevos costes de mejora se deben actualizar cliente y servidor juntos; el servidor conserva la decisión final sobre el gasto de oro.

## Regresiones corregidas

- Cada operación de cuenta captura su identidad y credencial. Antes de publicar una respuesta comprueba que la sesión sigue vigente. Un resultado o error 401 de la cuenta anterior no altera la nueva; terminar un cierre de sesión antiguo tampoco la borra.
- En Anillos se conserva el estado de combate al salir voluntariamente y volver con la misma cuenta: recursos, efectos, protección restante, escudos, posición, recargas y reaparición. El nuevo saludo de red no vuelve a conceder invulnerabilidad. La reconexión de transporte mantiene su reserva habitual de 30 segundos.

Las pruebas automáticas cubren compras y anuncios contra el emulador Firestore y HTTP real, aislamiento entre cuentas y reproducciones de los dos bugs. La interfaz se verifica con Godot en escritorio y móvil simulado a 720 × 540; la validación en un Android físico sigue pendiente.
