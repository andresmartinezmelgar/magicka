package com.arcana.firebase;

/** Decide si se puede volver a pedir una sesión antes de abrir la confirmación del sistema. */
final class UpdateRetryPolicy {
    private UpdateRetryPolicy() { }

    static boolean shouldRetry(boolean alreadyRetried, boolean confirmationStarted,
            boolean genericFailure, String abortMessage) {
        if (alreadyRetried || confirmationStarted) return false;
        if (genericFailure) return true;
        if (abortMessage == null) return false;
        String reason = abortMessage.trim();
        return "Permission denied".equalsIgnoreCase(reason) ||
            "INSTALL_FAILED_ABORTED: Permission denied".equalsIgnoreCase(reason);
    }
}
