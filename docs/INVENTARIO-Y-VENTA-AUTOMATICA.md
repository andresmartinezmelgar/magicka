# Favoritos y venta automática

Marcar un objeto como favorito bloquea su venta individual, el atajo de doble toque y la venta múltiple, aunque no esté equipado. Las piezas equipadas también están protegidas. Para vender un favorito hay que desmarcarlo primero.

El botón **No más de este estilo de calidad inferior** activa la venta de los futuros objetos de la misma **ranura y familia**, con una calidad **estrictamente inferior a la pieza que activa la regla**. Una regla activada desde Oro conserva Oro, Platino, Diamante y Mítico; vende Plata de la misma ranura y familia. Las tiradas de estadísticas, los afijos y el nivel de mejora no definen otro estilo. Los objetos que ya tienes al activar el filtro se conservan.

Puedes desactivarlo en la tarjeta con **Conservar también los de calidad inferior**, o en **Opciones → Venta automática → Conservar**. Al reactivarlo o cambiar el límite se vuelven a proteger todos los objetos que tengas en ese momento. Solo hay un límite por ranura y familia; activar otro lo sustituye. En Plata el botón está desactivado porque no existe una calidad inferior.

Las reglas anteriores, que vendían la calidad exacta, se desactivan al actualizar tanto las preferencias de cuenta como un guardado de Supervivencia. Se conservan los objetos y los favoritos. El protocolo 35 exige clientes y servidores con el mismo criterio; el servidor comprueba la pieza propia que establece el límite.

En el inventario permanente, los favoritos y filtros se guardan por cuenta en este dispositivo. Las ventas usan el servicio de cuenta y se realizan cuando cierras el inventario y la tienda. Si se pierde una respuesta, se consulta de nuevo el inventario antes de continuar, para no duplicar pagos. Cambiar de cuenta detiene el proceso.

En Supervivencia, el servidor protege los favoritos y vende el botín nuevo filtrado antes de guardarlo o equiparlo. Estas preferencias pertenecen a la partida y se conservan al guardar o reconectar. Los guardados antiguos empiezan sin filtros de venta.

El protocolo actual es **37**: deben actualizarse cliente y servidor juntos para reconocer los nuevos afijos de combate y la apariencia de ojos míticos.

## Efecto del equipo

Las piezas equipadas aportan sus colores a una única aura alrededor del mago. El brillo se mezcla entre zonas y respira suavemente con un movimiento común. La calidad y las mejoras aumentan su intensidad. No contiene rayos ni círculos animados independientes.

La cantidad de partículas depende de la suma de calidad de las seis piezas equipadas, incluidos los anillos: Plata vale 1 punto, Oro 2, Platino 3, Diamante 4 y Mítico 5. Cada punto aporta cuatro partículas; una ranura vacía aporta cero. Con seis piezas de igual calidad, el presupuesto es 24, 48, 72, 96 o 120 partículas respectivamente. Tres Plata, dos Oro y una Diamante suman 11 puntos y dan 44 partículas.

Las partículas aparecen y se desvanecen gradualmente, por lo que no todas están en su máximo de brillo al mismo tiempo. Comparten el campo del mago y cada pieza aporta su color según sus puntos. Las mejoras mantienen el refuerzo del brillo del halo dentro de su calidad.

El límite es 120 partículas por avatar. Se reutilizan las texturas, se redibujan como máximo a 24 Hz y se suspende su actualización cuando el avatar está oculto o no hay equipo.

## Compilación y publicación

Subir cambios no ejecuta compilaciones ni crea tags. El flujo de GitHub Actions se lanza manualmente cuando lo indique el usuario. La opción `publish_preview` empieza desactivada; solo se activa si también se ha solicitado crear el tag y publicar la versión de pruebas.
