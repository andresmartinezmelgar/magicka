# Arcana Arena v0.6.1 — correcciones de estabilidad

Descargar `ArcanaArena-v0.6.1-Windows-x64.zip`, extraer todo y abrir **ArcanaArena.exe**. Incluye el cliente, el servidor y sus runtimes.

- El bastón respeta fuego amigo: no daña aliados cuando la sala lo desactiva y sí mantiene el daño a rivales.
- El límite de 32 saludos simultáneos protege solo la negociación; las sesiones establecidas no consumen plazas de saludo.
- El cliente termina los smoke tests con error si falla el motor, aunque haya llegado a imprimir un marcador de éxito.
- La CI separa los logs temporales del runner de los logs del repositorio para conservar los diagnósticos de Windows.

Publicación condicionada a la compilación, 55 pruebas de reglas/diagnóstico/red, seis modos smoke del cliente, comprobación negativa de recursos ausentes, Docker y comprobaciones del ejecutable Windows.

El protocolo 4 no cambia; los clientes y servidores deben actualizarse juntos.
