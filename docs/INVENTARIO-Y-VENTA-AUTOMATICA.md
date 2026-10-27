# Favoritos y venta automática

Marcar un objeto como favorito bloquea su venta individual, el atajo de doble toque y la venta múltiple, aunque no esté equipado. Las piezas equipadas también están protegidas. Para vender un favorito hay que desmarcarlo primero.

Los botones muestran la acción disponible: **Equipar / Desequipar** y **Marcar como favorito / Desmarcar como favorito**. No usan bordes rojos o verdes para indicar el estado.

El botón **Vender ítems de menor calidad** activa la venta de los futuros objetos de la misma **ranura y familia**, con una calidad **estrictamente inferior a la pieza que activa la regla**. Una regla activada desde Oro conserva Oro, Platino, Diamante y Mítico; vende Plata de la misma ranura y familia. Las tiradas de estadísticas, los afijos y el nivel de mejora no definen otro estilo. Los objetos que ya tienes al activar el filtro se conservan.

Puedes desactivarlo en la tarjeta con **Mantener ítems de menor calidad**, o en **Opciones → Venta automática → Conservar**. Al reactivarlo o cambiar el límite se vuelven a proteger todos los objetos que tengas en ese momento. Solo hay un límite por ranura y familia; activar otro lo sustituye. Plata es la calidad mínima: desde ella solo se puede desactivar una regla existente.

Las tarjetas muestran el límite vigente de su ranura y familia. Una pieza de calidad inferior también permite desactivarlo, incluida Plata si hay una regla activa. La confirmación distingue activar de desactivar y recuerda que solo se filtra botín nuevo; no vende objetos anteriores ni otras familias.

Las reglas anteriores, que vendían la calidad exacta, se desactivan al actualizar tanto las preferencias de cuenta como un guardado de Supervivencia. Se conservan los objetos y los favoritos. El protocolo 35 exige clientes y servidores con el mismo criterio; el servidor comprueba la pieza propia que establece el límite.

En el inventario permanente, los favoritos y filtros se guardan por cuenta en este dispositivo. Las ventas usan el servicio de cuenta y se realizan cuando cierras el inventario y la tienda. Si se pierde una respuesta, se consulta de nuevo el inventario antes de continuar, para no duplicar pagos. Cambiar de cuenta detiene el proceso.

En Supervivencia, el servidor protege los favoritos y vende el botín nuevo filtrado antes de guardarlo o equiparlo. Estas preferencias pertenecen a la partida y se conservan al guardar o reconectar. Los guardados antiguos empiezan sin filtros de venta.

Con 24/24 objetos, el botín nuevo se convierte en su valor de oro aunque la venta automática esté desactivada. Desactivar una regla no amplía la capacidad. La ayuda, la confirmación del ajuste y el aviso de recogida distinguen esta conversión de una venta por regla y muestran el oro recibido.

El protocolo de esta rama es **39**: deben actualizarse cliente y servidor juntos para reconocer los afijos de combate, la apariencia de ojos míticos y el desglose de daño por origen.

## Efecto del equipo

«Ver estadísticas» incluye una tabla numerada con las mejoras de la partida: nivel, reliquias, auras, pasivas y regeneración de un escudo de vida activo. Las reliquias muestran su efecto acumulado; Regeneración explica que cura sin escudo, sin recibir ataques y fuera de las auras. Los porcentajes del equipo mantienen su tabla y sus aportaciones por pieza, también enumeradas.

Todos los objetos del inventario y de la tienda usan la misma tabla de dos columnas, con el valor actual seguido del máximo entre paréntesis, por ejemplo «+11.33% (Max 21.33)». El mínimo no se muestra. El equipo público mantiene la columna del valor actual, ya que el servidor no publica sus máximos. Los efectos especiales y las habilidades de los anillos se presentan como una lista numerada.

Con varios humanos, consultar el inventario mantiene el combate y activa la asistencia de supervivencia máxima, también cuando no hay bots. Mantiene o repone un escudo, busca una retirada segura, evita áreas enemigas y se cura o se apaga el fuego cuando lo necesita. Solo ataca brevemente desde una posición segura; interrumpe el ataque para defenderse. No compra equipo, no cambia la selección del jugador, no concede inmunidad ni genera tiempo activo de participación. Al cerrar la pantalla devuelve el control. Con un único humano se conserva la pausa de la partida.

Las piezas equipadas aportan sus colores a una única aura alrededor del mago. El brillo se mezcla entre zonas y respira suavemente con un movimiento común. La calidad y las mejoras aumentan su intensidad. El aura aparece tanto en el inventario como en combate; desde una sola pieza mítica añade filamentos quebrados del color del equipo. Quitar la última pieza mítica elimina esos filamentos y morir oculta el aura. Los ojos de poder siguen requiriendo las seis piezas míticas.

La cantidad de partículas depende de la suma de calidad de las seis piezas equipadas, incluidos los anillos: Plata vale 1 punto, Oro 2, Platino 3, Diamante 4 y Mítico 5. Cada punto aporta cuatro partículas; una ranura vacía aporta cero. Con seis piezas de igual calidad, el presupuesto es 24, 48, 72, 96 o 120 partículas respectivamente. Tres Plata, dos Oro y una Diamante suman 11 puntos y dan 44 partículas.

Las partículas aparecen y se desvanecen gradualmente, por lo que no todas están en su máximo de brillo al mismo tiempo. Comparten el campo del mago y cada pieza aporta su color según sus puntos. Las mejoras mantienen el refuerzo del brillo del halo dentro de su calidad.

El límite es 120 partículas por avatar. Se reutilizan las texturas, se redibujan como máximo a 24 Hz y se suspende su actualización cuando el avatar está oculto o no hay equipo.

## Compilación y publicación

Subir cambios no ejecuta compilaciones ni crea tags. El flujo de GitHub Actions se lanza manualmente cuando lo indique el usuario. La opción `publish_preview` empieza desactivada; solo se activa si también se ha solicitado crear el tag y publicar la versión de pruebas.
