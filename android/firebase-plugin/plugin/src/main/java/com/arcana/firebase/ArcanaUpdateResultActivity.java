package com.arcana.firebase;

import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageInstaller;
import android.os.Bundle;

/** Callback explícito del instalador; sigue existiendo después de actualizar nuestra APK. */
public final class ArcanaUpdateResultActivity extends Activity {
    @Override public void onCreate(Bundle state) { super.onCreate(state); handle(getIntent()); }
    @Override public void onNewIntent(Intent intent) { super.onNewIntent(intent); setIntent(intent); handle(intent); }

    private void handle(Intent intent) {
        int status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE);
        if (status == PackageInstaller.STATUS_PENDING_USER_ACTION) {
            Intent confirmation = intent.getParcelableExtra(Intent.EXTRA_INTENT);
            if (confirmation == null) { fail("Android no ha proporcionado su confirmacion de instalacion."); return; }
            ArcanaUpdatesPlugin.notifyStatus("confirmation", "Confirma la actualizacion en la ventana de Android.");
            try { startActivity(confirmation); }
            catch (Exception error) { fail(error.getMessage()); }
            finish();
            return;
        }
        if (status != PackageInstaller.STATUS_SUCCESS) {
            fail(intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE));
            return;
        }
        getSharedPreferences(ArcanaUpdatesPlugin.PREFS, 0).edit().clear().apply();
        Intent game = getPackageManager().getLaunchIntentForPackage(getPackageName());
        if (game != null) {
            game.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
            startActivity(game);
        }
        finish();
    }

    private void fail(String message) {
        getSharedPreferences(ArcanaUpdatesPlugin.PREFS, 0).edit().putBoolean("pending", false).apply();
        ArcanaUpdatesPlugin.notifyStatus("failed", message == null ? "Android no ha instalado la actualizacion." : message);
        finish();
    }
}
