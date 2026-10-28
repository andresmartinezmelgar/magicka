package com.arcana.firebase;

/** Se usa solo en el hilo de la actividad. Un resultado llega antes de onResume. */
final class UpdateForegroundGate {
    private boolean resumed;
    private Runnable pending;

    void pause() { resumed = false; }

    void resume() {
        resumed = true;
        Runnable action = pending;
        pending = null;
        if (action != null) action.run();
    }

    void runWhenResumed(Runnable action) {
        if (resumed) action.run();
        else pending = action;
    }

    void clear() { pending = null; }
}
