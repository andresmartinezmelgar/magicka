package com.arcana.firebase;

import android.content.Context;
import androidx.annotation.NonNull;
import org.godotengine.godot.Godot;
import org.godotengine.godot.plugin.GodotPlugin;
import org.godotengine.godot.plugin.SignalInfo;
import org.godotengine.godot.plugin.UsedByGodot;
import java.lang.ref.WeakReference;
import java.util.Collections;
import java.util.Set;

/** Conserva el proceso de una partida; la pausa y la simulación pertenecen a Godot. */
public final class ArcanaSessionPlugin extends GodotPlugin {
    private static WeakReference<ArcanaSessionPlugin> current = new WeakReference<>(null);
    private final SessionRetentionPolicy policy = new SessionRetentionPolicy();
    private boolean online;

    public ArcanaSessionPlugin(Godot godot) { super(godot); current = new WeakReference<>(this); }
    @NonNull @Override public String getPluginName() { return "ArcanaSession"; }
    @NonNull @Override public Set<SignalInfo> getPluginSignals() {
        return Collections.singleton(new SignalInfo("session_status", String.class, String.class));
    }

    @UsedByGodot public void setSessionActive(boolean active, boolean online) {
        runOnHostThread(() -> {
            this.online = online;
            apply(policy.setActive(active));
        });
    }

    @Override public void onMainResume() { apply(policy.resume()); }
    @Override public void onMainPause() {
        policy.pause();
        ArcanaSessionService.updateState(online, true);
    }
    @Override public void onMainDestroy() { apply(policy.setActive(false)); }

    private void apply(SessionRetentionPolicy.Action action) {
        Context context = getContext().getApplicationContext();
        try {
            if (!policy.isActive()) {
                ArcanaSessionService.release(context);
                return;
            }
            ArcanaSessionService.updateState(online, !policy.isResumed());
            if (action == SessionRetentionPolicy.Action.START) {
                // La partida solicita la retención durante la carga, con la actividad visible.
                ArcanaSessionService.retain(context);
                emitSignal("session_status", "retained", "Sesión Android conservada al cambiar de aplicación.");
            }
        } catch (RuntimeException error) {
            policy.startFailed();
            try { ArcanaSessionService.release(context); }
            catch (RuntimeException ignored) { /* La pausa y el autoguardado siguen disponibles. */ }
            emitSignal("session_status", "failed", error.toString());
        }
    }

    static void serviceFailed(String message) {
        ArcanaSessionPlugin plugin = current.get();
        if (plugin == null) return;
        plugin.runOnHostThread(() -> {
            plugin.policy.startFailed();
            plugin.emitSignal("session_status", "failed", message);
        });
    }
}
