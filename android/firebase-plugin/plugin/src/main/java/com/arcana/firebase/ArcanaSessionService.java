package com.arcana.firebase;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.os.Build;
import android.os.IBinder;
import android.util.Log;
import java.lang.ref.WeakReference;

/** Mismo proceso que Godot, sin temporizadores, red, WakeLock ni simulación propia. */
public final class ArcanaSessionService extends Service {
    private static final String CHANNEL = "arcana-game-session";
    private static final int NOTIFICATION = 29034;
    // Solo se accede desde el hilo principal de Android, incluido el plugin.
    private static WeakReference<ArcanaSessionService> current = new WeakReference<>(null);
    private static boolean wanted, online, paused;
    private boolean foreground;

    static void retain(Context context) {
        wanted = true;
        context.startForegroundService(new Intent(context, ArcanaSessionService.class));
    }

    static void release(Context context) {
        wanted = false;
        context.stopService(new Intent(context, ArcanaSessionService.class));
    }

    static void updateState(boolean connected, boolean suspended) {
        if (online == connected && paused == suspended) return;
        online = connected; paused = suspended;
        ArcanaSessionService service = current.get();
        if (service == null || !service.foreground || !wanted) return;
        try {
            NotificationManager manager = service.getSystemService(NotificationManager.class);
            if (manager.areNotificationsEnabled()) manager.notify(NOTIFICATION, service.notification());
        } catch (RuntimeException error) {
            Log.w("ArcanaSession", "No se pudo actualizar el aviso de la partida.", error);
        }
    }

    @Override public void onCreate() { super.onCreate(); current = new WeakReference<>(this); }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        try {
            NotificationManager manager = getSystemService(NotificationManager.class);
            var channel = new NotificationChannel(CHANNEL, "Partida abierta", NotificationManager.IMPORTANCE_LOW);
            channel.setDescription("Conserva la sesión de juego al cambiar de aplicación.");
            channel.setShowBadge(false); channel.setSound(null, null); channel.enableVibration(false);
            manager.createNotificationChannel(channel);
            if (Build.VERSION.SDK_INT >= 34)
                startForeground(NOTIFICATION, notification(), ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
            else startForeground(NOTIFICATION, notification());
            foreground = true;
            // Una salida durante el arranque del servicio debe ganar a una petición ya encolada.
            if (!wanted) { stopForeground(STOP_FOREGROUND_REMOVE); foreground = false; stopSelf(); }
        } catch (RuntimeException error) {
            wanted = false;
            ArcanaSessionPlugin.serviceFailed(error.toString());
            stopForeground(STOP_FOREGROUND_REMOVE); foreground = false; stopSelf();
        }
        // Cerrar la app o detener el servicio no resucita un proceso ni una partida.
        return START_NOT_STICKY;
    }

    private Notification notification() {
        String title = online ? "Sesión online abierta" : paused ? "Partida pausada" : "Partida abierta";
        String text = online ? "Toca para volver. La partida sigue en el servidor."
            : paused ? "Toca para volver al mismo punto." : "Se conservará en pausa al cambiar de aplicación.";
        var builder = new Notification.Builder(this, CHANNEL)
            .setSmallIcon(android.R.drawable.ic_media_pause)
            .setContentTitle(title).setContentText(text)
            .setCategory(Notification.CATEGORY_SERVICE)
            .setOngoing(true).setOnlyAlertOnce(true).setShowWhen(false);
        Intent launch = getPackageManager().getLaunchIntentForPackage(getPackageName());
        if (launch != null) {
            launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP);
            builder.setContentIntent(PendingIntent.getActivity(this, 0, launch,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE));
        }
        return builder.build();
    }

    @Override public IBinder onBind(Intent intent) { return null; }
    @Override public void onDestroy() {
        if (current.get() == this) current.clear();
        stopForeground(STOP_FOREGROUND_REMOVE); foreground = false;
        super.onDestroy();
    }
}
