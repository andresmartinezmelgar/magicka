# v0.13.0 — Cuentas, equipo procedural y oro

El mago incorpora inventario permanente con botas, gorro, pecho y arma. Hay cinco calidades —Plata, Oro, Platino, Diamante y Mítico— y tiradas por atributo. Los tres niveles inferiores pueden combinar ventajas y penalizaciones; los dos superiores se centran en Vida, Escudo o Fuego. Cada tirada conserva su mínimo, máximo y valor actual para mejoras futuras.

El equipo modifica el combate real: velocidad, alcance de rayos y proyectiles, defensa, ataque, curación, escudos, regeneración de energía y quemaduras. Los conjuntos Míticos activan bonus con dos o cuatro piezas de una familia. El equipamiento se utiliza en PvP y contra IA y permanece al morir. La sala y el launcher incluyen inventario, selección, vista previa, límites y totales efectivos.

Las victorias PvP entre cuentas humanas rivales activas conceden una pieza. La práctica y las victorias contra bots no conceden progresión permanente. El servidor calcula los premios y los entrega mediante un diario recuperable y recibos idempotentes de Firestore. El cliente muestra una notificación y recupera su inventario actualizado.

Eliminar un objeto entrega oro y lo desequipa mediante una transacción. Los reintentos no pagan dos veces. El oro y los rangos quedan preparados para mejorar objetos en una versión posterior; la mejora no está habilitada todavía.

Firebase Authentication verifica el acceso Google y su UID. Cloud Firestore mantiene perfiles, objetos, equipamiento, oro, ventas, resultados y catálogos separados de la versión del juego. El servidor requiere la configuración descrita en [PROGRESSION.md](PROGRESSION.md), dominio HTTPS y credenciales privadas. No se han creado ni configurado recursos en una cuenta Firebase de producción desde esta sesión.

Cliente y servidor deben actualizarse juntos: versión 0.13.0, protocolo 12 y código Android 13000. El acceso web funciona mediante el navegador del sistema y no incorpora el SDK administrativo ni credenciales privadas a la APK.

La validación incluye reglas del equipo y combate, servidor real, transacciones con el emulador Firestore, cierre y recuperación del diario y comprobaciones Godot de escritorio y móvil. La revisión de interfaz utiliza OpenGL Mesa/llvmpipe; no constituye una medición en teléfonos físicos. La autenticación Google de producción y los paquetes Android/Windows quedan sujetos a configurar Firebase y completar sus compilaciones, respectivamente.
