package com.arcana.firebase;

/** Decisiones en el hilo de la actividad; nunca se inicia un servicio desde segundo plano. */
final class SessionRetentionPolicy {
    enum Action { NONE, START, STOP }
    private boolean active, resumed, started;

    Action setActive(boolean value) { active = value; return reconcile(); }
    Action resume() { resumed = true; return reconcile(); }
    void pause() { resumed = false; }
    boolean isActive() { return active; }
    boolean isResumed() { return resumed; }
    void startFailed() { started = false; }

    private Action reconcile() {
        if (!active && started) { started = false; return Action.STOP; }
        if (active && resumed && !started) { started = true; return Action.START; }
        return Action.NONE;
    }
}
