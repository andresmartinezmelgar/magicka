package com.arcana.firebase;

/** Solo se usa en el hilo de la actividad. Reanudación y foco pueden llegar en distinto orden. */
final class UpdateForegroundGate {
    private boolean resumed;
    private boolean windowFocused;
    private Runnable pending;

    void pause() {
        resumed = false;
        windowFocused = false;
    }

    void resume() {
        resumed = true;
        runPending();
    }

    void setWindowFocused(boolean focused) {
        windowFocused = focused;
        runPending();
    }

    private void runPending() {
        if (!resumed || !windowFocused) return;
        Runnable action = pending;
        pending = null;
        if (action != null) action.run();
    }

    void runWhenReady(Runnable action) {
        if (resumed && windowFocused) action.run();
        else pending = action;
    }

    void clear() { pending = null; }
}
