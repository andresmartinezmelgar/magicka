package com.arcana.firebase;

import android.app.Activity;
import android.content.SharedPreferences;
import android.content.pm.PackageInfo;
import android.os.Build;
import java.io.File;
import java.io.FileInputStream;
import java.security.MessageDigest;

/** Verifies the private APK, digest, package and version before opening an installation session. */
final class UpdateApkVerifier {
    static final class Verified {
        final File file;
        final byte[] buffer;
        Verified(File file, byte[] buffer) { this.file = file; this.buffer = buffer; }
    }

    static Verified verify(Activity activity, SharedPreferences preferences) throws Exception {
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
        return new Verified(apk, buffer);
    }
}
