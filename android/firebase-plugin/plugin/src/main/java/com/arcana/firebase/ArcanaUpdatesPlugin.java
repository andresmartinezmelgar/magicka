package com.arcana.firebase;

import android.app.Activity;
import android.app.ActivityOptions;
import android.app.PendingIntent;
import android.content.Intent;
import android.content.pm.PackageInfo;
import android.content.pm.PackageInstaller;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;
import androidx.annotation.NonNull;
import org.godotengine.godot.Godot;
import org.godotengine.godot.plugin.GodotPlugin;
import org.godotengine.godot.plugin.SignalInfo;
import org.godotengine.godot.plugin.UsedByGodot;
import java.io.File;
import java.io.FileInputStream;
import java.io.OutputStream;
import java.lang.ref.WeakReference;
import java.security.MessageDigest;
import java.util.Collections;
import java.util.Set;
import java.util.concurrent.Executors;

/** Instalador de la propia app: sin file://, explorador de archivos ni proveedor publico. */
public final class ArcanaUpdatesPlugin extends GodotPlugin {
    static final String PREFS = "arcana-updates";
    static final String SESSION_EXTRA = "com.arcana.arena.UPDATE_SESSION";
    private static final int PERMISSION_REQUEST = 43127;
    private static WeakReference<ArcanaUpdatesPlugin> current = new WeakReference<>(null);
    private final UpdateForegroundGate foreground = new UpdateForegroundGate();
    // Estos estados y la confirmación de la sesión se gestionan en el hilo de la actividad.
    private boolean busy;
    private boolean awaitingPermission;

    public ArcanaUpdatesPlugin(Godot godot) { super(godot); current = new WeakReference<>(this); }
    @NonNull @Override public String getPluginName() { return "ArcanaUpdates"; }
    @NonNull @Override public Set<SignalInfo> getPluginSignals() {
        return Collections.singleton(new SignalInfo("update_status", String.class, String.class));
    }
    static void notifyStatus(String status, String message) {
        ArcanaUpdatesPlugin plugin = current.get();
        if (plugin != null) {
            plugin.runOnHostThread(() -> {
                if (status.equals("failed")) {
                    plugin.busy = false;
                    plugin.awaitingPermission = false;
                    plugin.foreground.clear();
                }
                plugin.emitSignal("update_status", status, message == null ? "No se ha completado la actualizacion." : message);
            });
        }
    }
    private Activity activity() {
        Activity activity = getActivity();
        if (activity == null) throw new IllegalStateException("Android no esta preparado para actualizar.");
        return activity;
    }

    @UsedByGodot public String installedApkPath() {
        return activity().getApplicationInfo().sourceDir;
    }

    @UsedByGodot public void installUpdate(String path, String versionCode, String sha256) {
        startUpdate(path, versionCode, sha256, false);
    }

    // Solo lo invoca el cliente tras seleccionar una compilación posterior del
    // canal de pruebas autenticado. Se conserva la entrada estable de tres argumentos.
    @UsedByGodot public void installPreviewUpdate(String path, String versionCode, String sha256) {
        startUpdate(path, versionCode, sha256, true);
    }

    private void startUpdate(String path, String versionCode, String sha256, boolean preview) {
        runOnHostThread(() -> prepareUpdate(path, versionCode, sha256, preview));
    }

    private void prepareUpdate(String path, String versionCode, String sha256, boolean preview) {
        if (busy) return;
        try {
            Activity activity = activity();
            File apk = new File(path).getCanonicalFile();
            File privateRoot = activity.getFilesDir().getCanonicalFile();
            if (!apk.toPath().startsWith(privateRoot.toPath()) || !apk.isFile() ||
                !apk.getName().endsWith(".apk") || !sha256.matches("[a-fA-F0-9]{64}"))
                throw new IllegalArgumentException("APK de actualizacion no valido.");
            long code = Long.parseLong(versionCode);
            activity.getSharedPreferences(PREFS, 0).edit().putString("path", apk.getPath())
                .putString("sha256", sha256).putLong("version", code).putBoolean("preview", preview)
                .remove("session").putBoolean("pending", true).apply();
            busy = true;
            foreground.runWhenResumed(() -> continueUpdate(true));
        } catch (Exception error) { notifyStatus("failed", error.getMessage()); }
    }

    private void continueUpdate(boolean requestPermission) {
        try {
            Activity activity = activity();
            if (!activity.getPackageManager().canRequestPackageInstalls()) {
                if (!requestPermission) {
                    notifyStatus("failed", "Android no ha autorizado las actualizaciones de esta aplicacion.");
                    return;
                }
                awaitingPermission = true;
                notifyStatus("permission", "Autoriza a Arcana Arena a instalar sus actualizaciones. Al volver, continuara automaticamente.");
                activity.startActivityForResult(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                    Uri.parse("package:" + activity.getPackageName())), PERMISSION_REQUEST);
                return;
            }
            notifyStatus("installing", "Android esta preparando la actualizacion.");
            var executor = Executors.newSingleThreadExecutor();
            executor.execute(() -> {
                try { createSession(activity); }
                catch (Exception error) { notifyStatus("failed", error.getMessage()); }
                finally { executor.shutdown(); }
            });
        } catch (Exception error) { notifyStatus("failed", error.getMessage()); }
    }

    @Override public void onMainActivityResult(int requestCode, int resultCode, Intent data) {
        if (requestCode == PERMISSION_REQUEST && awaitingPermission) {
            awaitingPermission = false;
            // Android entrega el resultado antes de reanudar la actividad.
            foreground.runWhenResumed(() -> continueUpdate(false));
        }
    }

    @Override public void onMainPause() { foreground.pause(); }

    @Override public void onMainResume() {
        boolean returningFromPermission = awaitingPermission;
        foreground.resume();
        // Algunos ajustes de fabricantes solo notifican la vuelta a primer plano.
        if (returningFromPermission && awaitingPermission) {
            awaitingPermission = false;
            foreground.runWhenResumed(() -> continueUpdate(false));
        }
    }

    private void createSession(Activity activity) throws Exception {
        var preferences = activity.getSharedPreferences(PREFS, 0);
        File apk = new File(preferences.getString("path", "")).getCanonicalFile();
        if (!apk.toPath().startsWith(activity.getFilesDir().getCanonicalFile().toPath()))
            throw new IllegalArgumentException("La APK no pertenece al almacenamiento privado.");
        MessageDigest hash = MessageDigest.getInstance("SHA-256");
        byte[] buffer = new byte[65536];
        try (var input = new FileInputStream(apk)) {
            int read;
            while ((read = input.read(buffer)) != -1) hash.update(buffer, 0, read);
        }
        StringBuilder digest = new StringBuilder();
        for (byte value : hash.digest()) digest.append(String.format("%02x", value & 255));
        if (!digest.toString().equalsIgnoreCase(preferences.getString("sha256", "")))
            throw new IllegalArgumentException("La APK ha cambiado despues de descargarse.");
        PackageInfo candidate = activity.getPackageManager().getPackageArchiveInfo(apk.getPath(), 0);
        PackageInfo installed = activity.getPackageManager().getPackageInfo(activity.getPackageName(), 0);
        long wanted = preferences.getLong("version", 0);
        long candidateCode = candidate == null ? 0 : Build.VERSION.SDK_INT >= 28 ? candidate.getLongVersionCode() : candidate.versionCode;
        long installedCode = Build.VERSION.SDK_INT >= 28 ? installed.getLongVersionCode() : installed.versionCode;
        if (candidate == null || !activity.getPackageName().equals(candidate.packageName) ||
            !UpdateVersionPolicy.accepts(candidateCode, wanted, installedCode, preferences.getBoolean("preview", false)))
            throw new IllegalArgumentException("La APK no es una version nueva de Arcana Arena.");
        // PackageInstaller verifica además la firma contra la app ya instalada.
        PackageInstaller installer = activity.getPackageManager().getPackageInstaller();
        PackageInstaller.SessionParams params = new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);
        params.setAppPackageName(activity.getPackageName());
        params.setSize(apk.length());
        if (Build.VERSION.SDK_INT >= 31)
            params.setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED);
        int sessionId = installer.createSession(params);
        try {
            if (!preferences.edit().putInt("session", sessionId).commit())
                throw new IllegalStateException("No se ha podido guardar la sesion de actualizacion.");
            notifyStatus("diagnostic", "APK verificada; preparando sesion Android " + sessionId + ".");
            try (PackageInstaller.Session session = installer.openSession(sessionId)) {
                try (var input = new FileInputStream(apk); OutputStream output = session.openWrite("base.apk", 0, apk.length())) {
                    int read;
                    while ((read = input.read(buffer)) != -1) output.write(buffer, 0, read);
                    session.fsync(output);
                }
            }
            // Verificar/copiar una APK puede durar varios segundos. Si el juego perdió
            // el foco, se conserva la sesión y se confirma al volver, sin fallar ni descargar otra vez.
            runOnHostThread(() -> foreground.runWhenResumed(() -> commitSession(sessionId)));
        } catch (Exception error) {
            abandonSession(installer, sessionId);
            throw error;
        }
    }

    private void commitSession(int sessionId) {
        PackageInstaller installer = null;
        try {
            Activity activity = activity();
            installer = activity.getPackageManager().getPackageInstaller();
            try (PackageInstaller.Session session = installer.openSession(sessionId)) {
                notifyStatus("diagnostic", "Confirmando sesion Android " + sessionId + " con la actividad reanudada.");
                Intent result = new Intent(activity, ArcanaUpdateResultActivity.class)
                    .setAction("com.arcana.arena.UPDATE_RESULT")
                    .putExtra(SESSION_EXTRA, sessionId)
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
                int flags = PendingIntent.FLAG_UPDATE_CURRENT;
                if (Build.VERSION.SDK_INT >= 31) flags |= PendingIntent.FLAG_MUTABLE;
                ActivityOptions options = ActivityOptions.makeBasic();
                if (Build.VERSION.SDK_INT >= 35)
                    options.setPendingIntentCreatorBackgroundActivityStartMode(ActivityOptions.MODE_BACKGROUND_ACTIVITY_START_ALLOWED);
                PendingIntent callback = PendingIntent.getActivity(activity, sessionId, result, flags, options.toBundle());
                session.commit(callback.getIntentSender());
            }
        } catch (Exception error) {
            if (installer != null) abandonSession(installer, sessionId);
            notifyStatus("failed", error.getMessage());
        }
    }

    private static void abandonSession(PackageInstaller installer, int sessionId) {
        try { installer.abandonSession(sessionId); }
        catch (Exception ignored) { /* Conservar el error original, aunque Android ya haya cerrado la sesión. */ }
    }
}
