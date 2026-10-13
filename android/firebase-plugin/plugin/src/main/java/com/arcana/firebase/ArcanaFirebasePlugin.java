package com.arcana.firebase;

import android.app.Activity;
import android.os.CancellationSignal;
import androidx.annotation.NonNull;
import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialException;
import androidx.credentials.CredentialManagerCallback;
import com.google.android.libraries.identity.googleid.GetGoogleIdOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;
import com.google.firebase.FirebaseApp;
import com.google.firebase.FirebaseOptions;
import com.google.firebase.auth.FirebaseAuth;
import com.google.firebase.auth.FirebaseUser;
import com.google.firebase.auth.GoogleAuthProvider;
import org.godotengine.godot.Godot;
import org.godotengine.godot.plugin.GodotPlugin;
import org.godotengine.godot.plugin.SignalInfo;
import org.godotengine.godot.plugin.UsedByGodot;
import java.util.Arrays;
import java.util.HashSet;
import java.util.Set;

/** Android-native Google account chooser, Firebase SDK token lifecycle and persisted sessions. */
public final class ArcanaFirebasePlugin extends GodotPlugin {
    // Public Firebase Android and OAuth configuration; no service-account credentials or OAuth secrets.
    private static final String APP_ID = "1:876144133098:android:64fc3b983abf939fcce893";
    private static final String PROJECT_ID = "arcana-arena-f2997";
    private static final String API_KEY = "AIzaSyAq5LTlzFLFswFdfPAlvPtkbkjqUj3yM4M";
    private static final String WEB_CLIENT_ID = "876144133098-agbajvvsg9uv3bbjn8t861tpb37gfh3e.apps.googleusercontent.com";
    private static final String SESSION_READY = "session_ready";
    private static final String LOGIN_FAILED = "login_failed";
    private static final String SESSION_CLEARED = "session_cleared";

    private FirebaseAuth auth;
    private boolean loginBusy;

    public ArcanaFirebasePlugin(Godot godot) { super(godot); }

    @NonNull @Override public String getPluginName() { return "ArcanaFirebase"; }

    @NonNull @Override public Set<SignalInfo> getPluginSignals() {
        return new HashSet<>(Arrays.asList(
            new SignalInfo(SESSION_READY, String.class, String.class, String.class, String.class),
            new SignalInfo(LOGIN_FAILED, String.class),
            new SignalInfo(SESSION_CLEARED)));
    }

    private FirebaseAuth firebase() {
        if (auth != null) return auth;
        Activity activity = getActivity();
        if (activity == null) throw new IllegalStateException("Android no ha iniciado la actividad.");
        FirebaseApp app;
        try {
            app = FirebaseApp.getInstance("arcana-arena");
        } catch (IllegalStateException missing) {
            FirebaseOptions options = new FirebaseOptions.Builder()
                .setApplicationId(APP_ID).setProjectId(PROJECT_ID).setApiKey(API_KEY).build();
            app = FirebaseApp.initializeApp(activity.getApplicationContext(), options, "arcana-arena");
        }
        auth = FirebaseAuth.getInstance(app);
        return auth;
    }

    private void failed(Exception error) {
        loginBusy = false;
        emitSignal(LOGIN_FAILED, error.getLocalizedMessage() == null ? "No se pudo iniciar sesión." : error.getLocalizedMessage());
    }

    private void sendSession(boolean forceRefresh) {
        FirebaseUser user = firebase().getCurrentUser();
        if (user == null) {
            emitSignal(SESSION_CLEARED);
            return;
        }
        user.getIdToken(forceRefresh).addOnCompleteListener(task -> {
            if (!task.isSuccessful() || task.getResult() == null || task.getResult().getToken() == null) {
                failed(task.getException() == null ? new IllegalStateException("No se pudo renovar el token.") : task.getException());
                return;
            }
            String expiry = String.valueOf(task.getResult().getExpirationTimestamp());
            emitSignal(SESSION_READY, user.getUid(), user.getDisplayName() == null ? "Mago" : user.getDisplayName(), task.getResult().getToken(), expiry);
        });
    }

    /** Load SDK-persisted Firebase session; no C# accounts server and no Google prompt. */
    @UsedByGodot public void requestSession() {
        try { sendSession(false); } catch (Exception error) { failed(error); }
    }

    /** Renew Firebase ID token before it expires, including when joining PvP. */
    @UsedByGodot public void refreshSession() {
        try { sendSession(true); } catch (Exception error) { failed(error); }
    }

    /** Show native Google chooser via Credential Manager, then exchange token with Firebase Auth. */
    @UsedByGodot public void signIn() {
        if (loginBusy) return;
        Activity activity = getActivity();
        if (activity == null) { failed(new IllegalStateException("Android no está preparado.")); return; }
        loginBusy = true;
        try {
            GetGoogleIdOption google = new GetGoogleIdOption.Builder()
                .setServerClientId(WEB_CLIENT_ID)
                .setFilterByAuthorizedAccounts(false)
                .setAutoSelectEnabled(false)
                .build();
            GetCredentialRequest request = new GetCredentialRequest.Builder().addCredentialOption(google).build();
            CredentialManager.create(activity).getCredentialAsync(activity, request, new CancellationSignal(),
                command -> activity.runOnUiThread(command),
                new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                    @Override public void onResult(GetCredentialResponse response) {
                        try {
                            Credential credential = response.getCredential();
                            if (!(credential instanceof CustomCredential)
                                    || !GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(credential.getType()))
                                throw new IllegalArgumentException("Google no devolvió una credencial válida.");
                            GoogleIdTokenCredential googleToken = GoogleIdTokenCredential.createFrom(credential.getData());
                            firebase().signInWithCredential(GoogleAuthProvider.getCredential(googleToken.getIdToken(), null))
                                .addOnCompleteListener(activity, result -> {
                                    loginBusy = false;
                                    if (!result.isSuccessful()) {
                                        failed(result.getException() == null ? new IllegalStateException("Error de Firebase Auth.") : result.getException());
                                        return;
                                    }
                                    sendSession(false);
                                });
                        } catch (Exception error) { failed(error); }
                    }
                    @Override public void onError(@NonNull GetCredentialException error) { failed(error); }
                });
        } catch (Exception error) { failed(error); }
    }

    @UsedByGodot public void signOut() {
        try {
            firebase().signOut();
            loginBusy = false;
            emitSignal(SESSION_CLEARED);
        } catch (Exception error) { failed(error); }
    }
}
