package com.arcana.firebase;

public final class UpdateForegroundGateTest {
    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }

    public static void main(String[] args) {
        var gate = new UpdateForegroundGate();
        int[] preparations = { 0 }, commits = { 0 };
        gate.runWhenReady(() -> preparations[0]++);
        check(preparations[0] == 0, "Se instala antes de reanudar el juego por primera vez.");
        gate.resume();
        check(preparations[0] == 0, "El primer intento se adelanta al foco de la ventana.");
        gate.setWindowFocused(true);
        gate.resume();
        gate.setWindowFocused(true);
        check(preparations[0] == 1, "El primer intento no continúa una sola vez al reanudar.");

        // Ajustes entrega su resultado mientras la actividad principal sigue pausada.
        gate.pause();
        gate.runWhenReady(() -> preparations[0]++);
        gate.setWindowFocused(true);
        check(preparations[0] == 1, "El resultado de permisos inicia la instalación en segundo plano.");
        gate.resume();
        gate.resume();
        check(preparations[0] == 2, "Volver de permisos necesita Reintentar o duplica la preparación.");

        // La copia de la APK termina después de pulsar Inicio o bloquear la pantalla.
        gate.pause();
        gate.runWhenReady(() -> commits[0]++);
        check(commits[0] == 0, "Se confirma la sesión con la aplicación en segundo plano.");
        gate.resume();
        check(commits[0] == 0, "Se confirma la sesión antes de recuperar el foco.");
        gate.setWindowFocused(true);
        gate.resume();
        check(commits[0] == 1, "Volver a la APK preparada no confirma exactamente una vez.");

        gate.runWhenReady(() -> commits[0]++);
        check(commits[0] == 2, "Una instalación con permiso y primer plano no avanza inmediatamente.");

        int[] confirmations = { 0 };
        // Android puede quitar el foco al mostrar otra ventana sin pausar el juego.
        gate.setWindowFocused(false);
        gate.runWhenReady(() -> confirmations[0]++);
        check(confirmations[0] == 0, "La confirmación del sistema se abre sin que el juego tenga el foco.");
        gate.setWindowFocused(true);
        gate.setWindowFocused(true);
        check(confirmations[0] == 1, "La confirmación del sistema no se abre una sola vez al volver al juego.");

        gate.pause();
        gate.runWhenReady(() -> commits[0]++);
        gate.clear();
        gate.resume();
        gate.setWindowFocused(true);
        check(commits[0] == 2, "Un intento cancelado se confirma al volver al juego.");
        gate.runWhenReady(() -> commits[0]++);
        check(commits[0] == 3, "Un error impide iniciar un intento nuevo.");

        // Reanudar puede abrir Ajustes y dejar pendiente su continuación.
        gate.pause();
        gate.runWhenReady(() -> {
            gate.pause();
            gate.runWhenReady(() -> preparations[0]++);
        });
        gate.resume();
        gate.setWindowFocused(true);
        check(preparations[0] == 2, "Abrir Ajustes consume su continuación sin esperar la vuelta.");
        gate.resume();
        gate.setWindowFocused(true);
        check(preparations[0] == 3, "Se perdió la continuación registrada durante onResume.");

        System.out.println("ANDROID_UPDATE_FOREGROUND_OK: primer intento, foco, permisos, confirmación única y cancelación.");
    }
}
