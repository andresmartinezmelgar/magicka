# Favoritos y venta automática

Marcar un objeto como favorito bloquea su venta individual, el atajo de doble toque y la venta múltiple, aunque no esté equipado. Las piezas equipadas también están protegidas. Para vender un favorito hay que desmarcarlo primero.

El botón **No más de este estilo de ítem** activa la venta de los futuros objetos de la misma **ranura, calidad y familia**. Las tiradas de estadísticas, los afijos y el nivel de mejora no definen otro estilo. Los objetos que ya tienes al activar el filtro se conservan.

Puedes desactivarlo en la tarjeta con **Conservar nuevos de este estilo**, o en **Opciones → Venta automática → Conservar**. Al reactivarlo se vuelven a proteger todos los objetos que tengas en ese momento.

En el inventario permanente, los favoritos y filtros se guardan por cuenta en este dispositivo. Las ventas usan el servicio de cuenta y se realizan cuando cierras el inventario y la tienda. Si se pierde una respuesta, se consulta de nuevo el inventario antes de continuar, para no duplicar pagos. Cambiar de cuenta detiene el proceso.

En Supervivencia, el servidor protege los favoritos y vende el botín nuevo filtrado antes de guardarlo o equiparlo. Estas preferencias pertenecen a la partida y se conservan al guardar o reconectar. Los guardados antiguos empiezan sin filtros de venta.

El protocolo pasa a **34**: en la próxima compilación deben actualizarse cliente y servidor juntos.

## Efecto del equipo

Las piezas equipadas aportan sus colores a una única aura alrededor del mago. El brillo se mezcla entre zonas y respira suavemente con un movimiento común. La calidad y las mejoras aumentan su intensidad. No contiene rayos ni círculos animados independientes.

## Compilación y publicación

Subir cambios no ejecuta compilaciones ni crea tags. El flujo de GitHub Actions se lanza manualmente cuando lo indique el usuario. La opción `publish_preview` empieza desactivada; solo se activa si también se ha solicitado crear el tag y publicar la versión de pruebas.
