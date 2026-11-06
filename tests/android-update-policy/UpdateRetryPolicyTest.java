package com.arcana.firebase;

public final class UpdateRetryPolicyTest {
    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }

    public static void main(String[] args) {
        String denied = "INSTALL_FAILED_ABORTED: Permission denied";
        check(UpdateRetryPolicy.shouldRetry(false, false, false, denied),
            "Un aborto por permiso antes de la confirmación necesita pulsar Reintentar.");
        check(UpdateRetryPolicy.shouldRetry(false, false, false, " Permission denied "),
            "Se ignoró el motivo de permiso sin prefijo.");
        check(UpdateRetryPolicy.shouldRetry(false, false, true, null),
            "Un fallo genérico inicial perdió su reintento.");
        check(!UpdateRetryPolicy.shouldRetry(true, false, false, denied),
            "Se reintentó más de una vez la instalación.");
        check(!UpdateRetryPolicy.shouldRetry(false, true, false, denied),
            "Se abrió otro intento después de pedir la confirmación al usuario.");
        check(!UpdateRetryPolicy.shouldRetry(false, true, true, null),
            "Se repitió un fallo genérico tras la confirmación.");
        check(!UpdateRetryPolicy.shouldRetry(false, false, false, "INSTALL_FAILED_ABORTED: User rejected permissions"),
            "Se ignoró el rechazo explícito del usuario.");
        check(!UpdateRetryPolicy.shouldRetry(false, false, false, "INSTALL_FAILED_ABORTED: Session was abandoned"),
            "Se repitió una sesión abandonada.");
        check(!UpdateRetryPolicy.shouldRetry(false, false, false, null),
            "Se reintentó un fallo no recuperable.");
        System.out.println("ANDROID_UPDATE_RETRY_OK");
    }
}
