package com.arcana.firebase;

import android.app.Activity;
import android.app.ActivityOptions;
import android.app.PendingIntent;
import android.content.Intent;
import android.content.pm.PackageInfo;
import android.content.pm.PackageInstaller;
import android.net.Uri;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.view.View;
import android.view.ViewTreeObserver;
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
    private static final int PERMISSION_RECHECKS = 8;
    private static final long PERMISSION_RECHECK_MS = 250;
    private static WeakReference<ArcanaUpdatesPlugin> current = new WeakReference<>(null);
    private final UpdateForegroundGate foreground = new UpdateForegroundGate();
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final ViewTreeObserver.OnWindowFocusChangeListener windowFocusListener = foreground::setWindowFocused;
    private final Runnable recheckPermission = () -> foreground.runWhenReady(() -> continueUpdate(false));
    // Estos estados y la confirmación de la sesión se gestionan en el hilo de la actividad.
    private View hostWindow;
    private boolean busy;
    private boolean awaitingPermission;
    private boolean retried;
    private int permissionChecksRemaining;
    private int confirmationSession = -1;

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
                    plugin.mainHandler.removeCallbacks(plugin.recheckPermission);
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
            retried = false;
            confirmationSession = -1;
            permissionChecksRemaining = 0;
            foreground.runWhenReady(() -> continueUpdate(true));
        } catch (Exception error) { notifyStatus("failed", error.getMessage()); }
    }

    private void continueUpdate(boolean requestPermission) {
        try {
            Activity activity = activity();
            if (!activity.getPackageManager().canRequestPackageInstalls()) {
                if (!requestPermission) {
                    // La vuelta de Ajustes puede preceder a la propagación del permiso.
                    if (permissionChecksRemaining > 0) {
                        permissionChecksRemaining--;
                        mainHandler.postDelayed(recheckPermission, PERMISSION_RECHECK_MS);
                    } else notifyStatus("failed", "Android no ha autorizado las actualizaciones de esta aplicacion.");
                    return;
                }
                awaitingPermission = true;
                notifyStatus("permission", "Autoriza a Arcana Arena a instalar sus actualizaciones. Al volver, continuara automaticamente.");
                activity.startActivityForResult(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                    Uri.parse("package:" + activity.getPackageName())), PERMISSION_REQUEST);
                return;
            }
            permissionChecksRemaining = 0;
            boolean requireConfirmation = retried;
            notifyStatus("installing", requireConfirmation ? "Preparando la confirmación de Android."
                : "Android está preparando la actualización.");
            var executor = Executors.newSingleThreadExecutor();
            executor.execute(() -> {
                try { createSession(activity, requireConfirmation); }
                catch (Exception error) { notifyStatus("failed", error.getMessage()); }
                finally { executor.shutdown(); }
            });
        } catch (Exception error) { notifyStatus("failed", error.getMessage()); }
    }

    @Override public void onMainActivityResult(int requestCode, int resultCode, Intent data) {
        if (requestCode == PERMISSION_REQUEST && awaitingPermission) resumeAfterPermission();
    }

    private void resumeAfterPermission() {
        awaitingPermission = false;
        permissionChecksRemaining = PERMISSION_RECHECKS;
        recheckPermission.run();
    }

    @Override public void onMainResume() {
        Activity activity = getActivity();
        if (activity == null) return;
        if (hostWindow == null) {
            hostWindow = activity.getWindow().getDecorView();
            hostWindow.getViewTreeObserver().addOnWindowFocusChangeListener(windowFocusListener);
        }
        // Algunos ajustes de fabricantes solo notifican la vuelta a primer plano.
        if (awaitingPermission) resumeAfterPermission();
        foreground.setWindowFocused(hostWindow.hasWindowFocus());
        foreground.resume();
    }

    @Override public void onMainPause() {
        foreground.pause();
        if (hostWindow != null) {
            var observer = hostWindow.getViewTreeObserver();
            if (observer.isAlive()) observer.removeOnWindowFocusChangeListener(windowFocusListener);
            hostWindow = null;
        }
    }

    @Override public void onMainDestroy() {
        onMainPause();
        mainHandler.removeCallbacks(recheckPermission);
        foreground.clear();
        if (current.get() == this) current.clear();
    }

    // La actividad de resultado vuelve al juego antes de mostrar la confirmación del sistema.
    static boolean confirmInGame(int sessionId, Intent confirmation) {
        ArcanaUpdatesPlugin plugin = current.get();
        Activity host = plugin == null ? null : plugin.getActivity();
        if (host == null || host.isFinishing() || !plugin.busy || sessionId < 0) return false;
        if (plugin.confirmationSession == sessionId) return true;
        var preferences = host.getSharedPreferences(PREFS, 0);
        plugin.foreground.runWhenReady(() -> {
            if (!plugin.busy || preferences.getInt("session", -1) != sessionId ||
                plugin.confirmationSession == sessionId) return;
            plugin.confirmationSession = sessionId;
            try { host.startActivity(confirmation); }
            catch (Exception error) {
                preferences.edit().remove("session").putBoolean("pending", false).apply();
                abandonSession(host.getPackageManager().getPackageInstaller(), sessionId);
                notifyStatus("failed", error.getMessage());
            }
        });
        return true;
    }

    static boolean retrySession(int sessionId, int status, String message) {
        ArcanaUpdatesPlugin plugin = current.get();
        Activity host = plugin == null ? null : plugin.getActivity();
        if (host == null || host.isFinishing() || !plugin.busy || sessionId < 0) return false;
        var preferences = host.getSharedPreferences(PREFS, 0);
        if (preferences.getInt("session", -1) != sessionId) return false;
        boolean genericFailure = status == PackageInstaller.STATUS_FAILURE;
        String abortMessage = status == PackageInstaller.STATUS_FAILURE_ABORTED ? message : null;
        if (!UpdateRetryPolicy.shouldRetry(plugin.retried, plugin.confirmationSession == sessionId,
            genericFailure, abortMessage)) return false;
        plugin.retried = true;
        preferences.edit().remove("session").apply();
        abandonSession(host.getPackageManager().getPackageInstaller(), sessionId);
        notifyStatus("diagnostic", "Se repetirá la sesión Android con confirmación y la misma APK verificada.");
        plugin.foreground.runWhenReady(() -> plugin.continueUpdate(false));
        return true;
    }

    private void createSession(Activity activity, boolean requireConfirmation) throws Exception {
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
            params.setRequireUserAction(requireConfirmation ? PackageInstaller.SessionParams.USER_ACTION_REQUIRED
                : PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED);
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
            runOnHostThread(() -> foreground.runWhenReady(() -> commitSession(sessionId)));
        } catch (Exception error) {
            preferences.edit().remove("session").putBoolean("pending", false).apply();
            abandonSession(installer, sessionId);
            throw error;
        }
    }

    private void commitSession(int sessionId) {
        Activity activity = null;
        PackageInstaller installer = null;
        try {
            activity = activity();
            installer = activity.getPackageManager().getPackageInstaller();
            try (PackageInstaller.Session session = installer.openSession(sessionId)) {
                notifyStatus("diagnostic", "Confirmando sesion Android " + sessionId + " con el juego en primer plano.");
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
            if (installer != null) {
                activity.getSharedPreferences(PREFS, 0).edit().remove("session").putBoolean("pending", false).apply();
                abandonSession(installer, sessionId);
            }
            notifyStatus("failed", error.getMessage());
        }
    }

    static void abandonSession(PackageInstaller installer, int sessionId) {
        try { installer.abandonSession(sessionId); }
        catch (Exception ignored) { /* Conservar el error original, aunque Android ya haya cerrado la sesión. */ }
    }
}
