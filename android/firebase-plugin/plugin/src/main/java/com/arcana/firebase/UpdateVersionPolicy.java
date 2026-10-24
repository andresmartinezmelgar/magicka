package com.arcana.firebase;

/** El canal de pruebas distingue compilaciones mediante su identidad verificada en C#. */
final class UpdateVersionPolicy {
    private UpdateVersionPolicy() { }

    static boolean accepts(long candidate, long expected, long installed, boolean preview) {
        // Nunca se instala una versión inferior ni una APK distinta de la descargada.
        // Android admite reemplazos firmados con igual versionCode; solo las pruebas los necesitan.
        return expected > 0 && candidate == expected && candidate >= installed &&
            (candidate > installed || preview);
    }
}
