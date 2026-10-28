package com.arcana.firebase;

public final class SessionRetentionPolicyTest {
    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }

    public static void main(String[] args) {
        var policy = new SessionRetentionPolicy();
        check(policy.setActive(true) == SessionRetentionPolicy.Action.NONE,
            "Se inicia el servicio antes de que la actividad esté reanudada.");
        check(policy.resume() == SessionRetentionPolicy.Action.START,
            "La partida no se protege al entrar en primer plano.");
        check(policy.resume() == SessionRetentionPolicy.Action.NONE &&
            policy.setActive(true) == SessionRetentionPolicy.Action.NONE, "Se duplica el servicio de una misma partida.");

        policy.pause();
        check(policy.isActive() && !policy.isResumed() && policy.setActive(true) == SessionRetentionPolicy.Action.NONE,
            "Cambiar de app detiene la retención o intenta iniciar otro servicio en segundo plano.");
        check(policy.resume() == SessionRetentionPolicy.Action.NONE, "Volver a la partida conservada reinicia su servicio.");
        check(policy.setActive(false) == SessionRetentionPolicy.Action.STOP &&
            policy.setActive(false) == SessionRetentionPolicy.Action.NONE, "Volver al menú no libera la sesión una sola vez.");

        policy.pause();
        check(policy.setActive(true) == SessionRetentionPolicy.Action.NONE, "Una solicitud tardía inicia el servicio desde el fondo.");
        check(policy.setActive(false) == SessionRetentionPolicy.Action.NONE &&
            policy.resume() == SessionRetentionPolicy.Action.NONE, "Se reabre una sesión descartada antes de reanudar.");

        check(policy.setActive(true) == SessionRetentionPolicy.Action.START, "No se retiene una nueva partida.");
        policy.pause(); policy.startFailed();
        check(policy.setActive(true) == SessionRetentionPolicy.Action.NONE, "Un fallo provoca reintentos ilegales en segundo plano.");
        check(policy.resume() == SessionRetentionPolicy.Action.START && policy.resume() == SessionRetentionPolicy.Action.NONE,
            "Volver tras un fallo no permite un único intento nuevo.");
        policy.pause();
        check(policy.setActive(false) == SessionRetentionPolicy.Action.STOP,
            "Cerrar la actividad no detiene el servicio mientras está en segundo plano.");
        check(policy.resume() == SessionRetentionPolicy.Action.NONE, "Una actividad nueva resucita una partida cerrada.");

        System.out.println("ANDROID_SESSION_POLICY_OK: retención al cargar, pausa sin cierre, retorno sin reinicio, salida y errores sin arranques desde el fondo.");
    }
}
