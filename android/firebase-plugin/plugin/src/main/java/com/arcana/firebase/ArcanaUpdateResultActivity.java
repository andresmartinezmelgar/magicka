package com.arcana.firebase;

import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageInstaller;
import android.os.Bundle;
import java.io.File;

/** Callback explícito del instalador; sigue existiendo después de actualizar nuestra APK. */
public final class ArcanaUpdateResultActivity extends Activity {
    private final UpdateForegroundGate foreground = new UpdateForegroundGate();

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        Intent intent = getIntent();
        foreground.runWhenReady(() -> handle(intent));
    }
    @Override public void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        foreground.runWhenReady(() -> handle(intent));
    }
    @Override public void onPostResume() {
        super.onPostResume();
        foreground.setWindowFocused(hasWindowFocus());
        foreground.resume();
    }
    @Override public void onPause() {
        foreground.pause();
        super.onPause();
    }
    @Override public void onWindowFocusChanged(boolean focused) {
        super.onWindowFocusChanged(focused);
        foreground.setWindowFocused(focused);
    }

    private void handle(Intent intent) {
        if (intent == null || !intent.hasExtra(PackageInstaller.EXTRA_STATUS)) { finish(); return; }
        var preferences = getSharedPreferences(ArcanaUpdatesPlugin.PREFS, 0);
        // Un resultado tardío de un intento anterior no puede cancelar el actual
        // ni borrar su APK. Los callbacks de versiones antiguas usan el extra del sistema.
        int sessionId = intent.getIntExtra(ArcanaUpdatesPlugin.SESSION_EXTRA,
            intent.getIntExtra(PackageInstaller.EXTRA_SESSION_ID, -1));
        if (sessionId != -1 && sessionId != preferences.getInt("session", -1)) {
            ArcanaUpdatesPlugin.notifyStatus("diagnostic", "Ignorado resultado de la sesion Android anterior " + sessionId + ".");
            finish(); return;
        }
        int status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE);
        ArcanaUpdatesPlugin.notifyStatus("diagnostic", "Resultado de sesion Android " + sessionId + ": estado " + status +
            "; " + intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE));
        if (status == PackageInstaller.STATUS_PENDING_USER_ACTION) {
            Intent confirmation = intent.getParcelableExtra(Intent.EXTRA_INTENT);
            if (confirmation == null) { fail(sessionId, "Android no ha proporcionado su confirmacion de instalacion."); return; }
            ArcanaUpdatesPlugin.notifyStatus("confirmation", "Confirma la actualizacion en la ventana de Android.");
            if (ArcanaUpdatesPlugin.confirmInGame(sessionId, confirmation)) { finish(); return; }
            try { startActivity(confirmation); }
            catch (Exception error) { fail(sessionId, error.getMessage()); return; }
            finish();
            return;
        }
        if (status != PackageInstaller.STATUS_SUCCESS) {
            if (status == PackageInstaller.STATUS_FAILURE && ArcanaUpdatesPlugin.retrySession(sessionId)) {
                finish();
                return;
            }
            fail(sessionId, intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE));
            return;
        }
        // Solo se retira la descarga después de que Android confirme la instalación.
        try {
            File apk = new File(preferences.getString("path", "")).getCanonicalFile();
            File root = getFilesDir().getCanonicalFile();
            if (apk.toPath().startsWith(root.toPath()) && apk.getName().endsWith(".apk")) apk.delete();
        } catch (Exception ignored) { /* El arranque nuevo limpia también los paquetes anteriores. */ }
        preferences.edit().clear().apply();
        Intent game = getPackageManager().getLaunchIntentForPackage(getPackageName());
        if (game != null) {
            game.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
            startActivity(game);
        }
        finish();
    }

    private void fail(int sessionId, String message) {
        getSharedPreferences(ArcanaUpdatesPlugin.PREFS, 0).edit().remove("session").putBoolean("pending", false).apply();
        if (sessionId >= 0)
            ArcanaUpdatesPlugin.abandonSession(getPackageManager().getPackageInstaller(), sessionId);
        ArcanaUpdatesPlugin.notifyStatus("failed", message == null ? "Android no ha instalado la actualizacion." : message);
        finish();
    }
}
