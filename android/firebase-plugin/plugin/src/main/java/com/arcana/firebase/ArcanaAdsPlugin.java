package com.arcana.firebase;

import android.app.Activity;
import android.content.pm.PackageManager;
import androidx.annotation.NonNull;
import com.google.android.gms.ads.AdError;
import com.google.android.gms.ads.AdRequest;
import com.google.android.gms.ads.FullScreenContentCallback;
import com.google.android.gms.ads.LoadAdError;
import com.google.android.gms.ads.MobileAds;
import com.google.android.gms.ads.rewarded.RewardedAd;
import com.google.android.gms.ads.rewarded.RewardedAdLoadCallback;
import com.google.android.gms.ads.rewarded.ServerSideVerificationOptions;
import com.google.android.ump.ConsentInformation;
import com.google.android.ump.ConsentRequestParameters;
import com.google.android.ump.UserMessagingPlatform;
import org.godotengine.godot.Godot;
import org.godotengine.godot.plugin.GodotPlugin;
import org.godotengine.godot.plugin.SignalInfo;
import org.godotengine.godot.plugin.UsedByGodot;
import java.util.Collections;
import java.util.Set;
import java.util.ArrayList;
import java.util.List;

/** Solo un anuncio solicitado por el jugador; UMP se resuelve antes de inicializar publicidad. */
public final class ArcanaAdsPlugin extends GodotPlugin {
    private boolean busy, initialized, privacyPrepared, privacyBusy;
    private final List<Runnable> privacyWaiters = new ArrayList<>();
    private long generation;
    private volatile boolean privacyRequired;
    public ArcanaAdsPlugin(Godot godot) { super(godot); }
    @NonNull @Override public String getPluginName() { return "ArcanaAds"; }
    @NonNull @Override public Set<SignalInfo> getPluginSignals() {
        return Collections.singleton(new SignalInfo("rewarded_result", String.class, String.class));
    }
    @UsedByGodot public String configuredAppId() {
        try {
            return getContext().getPackageManager().getApplicationInfo(getContext().getPackageName(), PackageManager.GET_META_DATA)
                .metaData.getString("com.google.android.gms.ads.APPLICATION_ID", "");
        } catch (Exception error) { return ""; }
    }
    @UsedByGodot public void preparePrivacy() { runOnHostThread(() -> consent(() -> { })); }
    @UsedByGodot public boolean privacyOptionsRequired() { return privacyRequired; }
    @UsedByGodot public void showPrivacyOptions() {
        runOnHostThread(() -> {
            Activity activity = getActivity(); if (activity == null) return;
            UserMessagingPlatform.showPrivacyOptionsForm(activity, error -> {
                privacyPrepared = false;
                privacyRequired = UserMessagingPlatform.getConsentInformation(activity).getPrivacyOptionsRequirementStatus()
                    == ConsentInformation.PrivacyOptionsRequirementStatus.REQUIRED;
            });
        });
    }
    private void consent(Runnable ready) {
        Activity activity = getActivity(); if (activity == null) { fail("La actividad no está disponible."); return; }
        ConsentInformation info = UserMessagingPlatform.getConsentInformation(activity);
        if (privacyPrepared) {
            if (info.canRequestAds()) ready.run(); else fail("Los anuncios no están disponibles con las opciones actuales.");
            return;
        }
        privacyWaiters.add(ready);
        if (privacyBusy) return;
        privacyBusy = true;
        info.requestConsentInfoUpdate(activity, new ConsentRequestParameters.Builder().build(), () ->
            UserMessagingPlatform.loadAndShowConsentFormIfRequired(activity, error -> {
                privacyPrepared = info.canRequestAds();
                finishPrivacy(info.canRequestAds());
            }), error -> {
                finishPrivacy(info.canRequestAds());
            });
    }
    private void finishPrivacy(boolean allowed) {
        privacyBusy = false;
        privacyRequired = UserMessagingPlatform.getConsentInformation(getContext()).getPrivacyOptionsRequirementStatus()
            == ConsentInformation.PrivacyOptionsRequirementStatus.REQUIRED;
        List<Runnable> callbacks = new ArrayList<>(privacyWaiters); privacyWaiters.clear();
        if (allowed) for (Runnable ready : callbacks) ready.run();
        else fail("No se pudo consultar la privacidad de anuncios.");
    }
    @UsedByGodot public void showRewarded(String unit, String uid, String ticket) {
        runOnHostThread(() -> {
            if (busy) { fail("Ya hay un anuncio en curso."); return; }
            busy = true;
            long request = ++generation;
            consent(() -> {
                if (request != generation) return;
                if (initialized) load(unit, uid, ticket, request);
                else MobileAds.initialize(getContext(), status -> runOnHostThread(() -> {
                    initialized = true; if (request == generation) load(unit, uid, ticket, request);
                }));
            });
        });
    }
    @UsedByGodot public void cancelRewarded() { runOnHostThread(() -> { generation++; busy = false; }); }
    private void load(String unit, String uid, String ticket, long request) {
        RewardedAd.load(getContext(), unit, new AdRequest.Builder().build(), new RewardedAdLoadCallback() {
            @Override public void onAdFailedToLoad(@NonNull LoadAdError error) { if (request == generation) fail("No hay anuncios disponibles. Inténtalo más tarde."); }
            @Override public void onAdLoaded(@NonNull RewardedAd ad) {
                if (request != generation) return;
                Activity activity = getActivity(); if (activity == null) { fail("La actividad no está disponible."); return; }
                ad.setServerSideVerificationOptions(new ServerSideVerificationOptions.Builder().setUserId(uid).setCustomData(ticket).build());
                final boolean[] earned = { false };
                ad.setFullScreenContentCallback(new FullScreenContentCallback() {
                    @Override public void onAdDismissedFullScreenContent() {
                        if (request == generation) { busy = false; emitSignal("rewarded_result", earned[0] ? "rewarded" : "closed", ""); }
                    }
                    @Override public void onAdFailedToShowFullScreenContent(@NonNull AdError error) { if (request == generation) fail("No se pudo mostrar el anuncio."); }
                });
                ad.show(activity, reward -> earned[0] = true);
            }
        });
    }
    private void fail(String message) { busy = false; emitSignal("rewarded_result", "failed", message); }
    @Override public void onMainDestroy() { generation++; busy = false; privacyWaiters.clear(); }
}
