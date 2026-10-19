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
import androidx.credentials.exceptions.NoCredentialException;
import androidx.credentials.CredentialManagerCallback;
import com.google.android.libraries.identity.googleid.GetGoogleIdOption;
import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
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
    private long generation;
    private boolean signedOut;
    private CancellationSignal credentialCancellation;

    public ArcanaFirebasePlugin(Godot godot) { super(godot); }

    @NonNull @Override public String getPluginName() { return "ArcanaFirebase"; }

    @NonNull @Override public Set<SignalInfo> getPluginSignals() {
        return new HashSet<>(Arrays.asList(
            new SignalInfo(SESSION_READY, String.class, String.class, String.class, String.class, String.class),
            new SignalInfo(LOGIN_FAILED, String.class, String.class),
            new SignalInfo(SESSION_CLEARED, String.class)));
    }

    @UsedByGodot public synchronized String sessionGeneration() { return String.valueOf(generation); }

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

    private synchronized void failed(long request, Exception error) {
        if (request != generation) return;
        loginBusy = false;
        emitSignal(LOGIN_FAILED, error.getLocalizedMessage() == null ? "No se pudo iniciar sesión." : error.getLocalizedMessage(), String.valueOf(request));
    }

    private synchronized void sendSession(boolean forceRefresh, long request) {
        if (request != generation || signedOut) return;
        FirebaseUser user = firebase().getCurrentUser();
        if (user == null) {
            emitSignal(SESSION_CLEARED, String.valueOf(generation));
            return;
        }
        user.getIdToken(forceRefresh).addOnCompleteListener(task -> {
            synchronized (ArcanaFirebasePlugin.this) {
                FirebaseUser current = firebase().getCurrentUser();
                if (request != generation || signedOut || current == null || !current.getUid().equals(user.getUid())) return;
                if (!task.isSuccessful() || task.getResult() == null || task.getResult().getToken() == null) {
                    failed(request, task.getException() == null ? new IllegalStateException("No se pudo renovar el token.") : task.getException());
                    return;
                }
                String expiry = String.valueOf(task.getResult().getExpirationTimestamp());
                emitSignal(SESSION_READY, user.getUid(), user.getDisplayName() == null ? "Mago" : user.getDisplayName(), task.getResult().getToken(), expiry, String.valueOf(request));
            }
        });
    }

    /** Load SDK-persisted Firebase session; no C# accounts server and no Google prompt. */
    @UsedByGodot public synchronized void requestSession() {
        if (loginBusy) return;
        signedOut = false;
        try { sendSession(false, generation); } catch (Exception error) { failed(generation, error); }
    }

    /** Renew Firebase ID token before it expires, including when joining PvP. */
    @UsedByGodot public synchronized void refreshSession() {
        if (loginBusy || signedOut) return;
        try { sendSession(true, generation); } catch (Exception error) { failed(generation, error); }
    }

    /**
     * Explicit Google sign-in button: first use the dedicated Sign in with Google
     * option. Some providers return NoCredentialException for passive discovery
     * despite the phone having Google accounts; retry using unrestricted selection.
     */
    private void requestGoogleCredential(Activity activity, boolean explicitButton, long request) {
        if (request != generation || signedOut) return;
        GetCredentialRequest.Builder builder = new GetCredentialRequest.Builder();
        if (explicitButton) {
            builder.addCredentialOption(new GetSignInWithGoogleOption.Builder(WEB_CLIENT_ID).build());
        } else {
            builder.addCredentialOption(new GetGoogleIdOption.Builder()
                .setServerClientId(WEB_CLIENT_ID)
                .setFilterByAuthorizedAccounts(false)
                .setAutoSelectEnabled(false)
                .build());
        }
        credentialCancellation = new CancellationSignal();
        CredentialManager.create(activity).getCredentialAsync(activity, builder.build(), credentialCancellation,
            command -> activity.runOnUiThread(command),
            new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                @Override public void onResult(GetCredentialResponse response) {
                    synchronized (ArcanaFirebasePlugin.this) {
                        if (request != generation || signedOut) return;
                        try {
                            Credential credential = response.getCredential();
                            if (!(credential instanceof CustomCredential)
                                    || !GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(credential.getType()))
                                throw new IllegalArgumentException("Google no devolvió una credencial válida.");
                            GoogleIdTokenCredential googleToken = GoogleIdTokenCredential.createFrom(credential.getData());
                            firebase().signInWithCredential(GoogleAuthProvider.getCredential(googleToken.getIdToken(), null))
                                .addOnCompleteListener(activity, result -> {
                                    synchronized (ArcanaFirebasePlugin.this) {
                                        if (request != generation || signedOut) {
                                            if (signedOut) firebase().signOut();
                                            return;
                                        }
                                        loginBusy = false;
                                        if (!result.isSuccessful()) {
                                            failed(request, result.getException() == null
                                                ? new IllegalStateException("Error de Firebase Auth.") : result.getException());
                                            return;
                                        }
                                        sendSession(false, request);
                                    }
                                });
                        } catch (Exception error) { failed(request, error); }
                    }
                }
                @Override public void onError(@NonNull GetCredentialException error) {
                    synchronized (ArcanaFirebasePlugin.this) {
                        if (request != generation || signedOut) return;
                        if (error instanceof NoCredentialException && explicitButton) {
                            try { requestGoogleCredential(activity, false, request); }
                            catch (Exception retryError) { failed(request, retryError); }
                            return;
                        }
                        if (error instanceof NoCredentialException) {
                            failed(request, new IllegalStateException(
                                "Google no ofrece cuentas a esta APK. Comprueba que tenga una firma SHA-1 " +
                                "registrada en Firebase para com.arcana.arena y que Google Play Services esté actualizado."));
                        } else failed(request, error);
                    }
                }
            });
    }

    /** Show the native account chooser, then exchange the Google ID token with Firebase Auth. */
    @UsedByGodot public synchronized void signIn() {
        if (loginBusy) return;
        Activity activity = getActivity();
        if (activity == null) { failed(generation, new IllegalStateException("Android no está preparado.")); return; }
        loginBusy = true;
        signedOut = false;
        long request = ++generation;
        try { requestGoogleCredential(activity, true, request); }
        catch (Exception error) { failed(request, error); }
    }

    @UsedByGodot public synchronized void signOut() {
        ++generation;
        signedOut = true;
        loginBusy = false;
        if (credentialCancellation != null) credentialCancellation.cancel();
        credentialCancellation = null;
        try {
            firebase().signOut();
            loginBusy = false;
            emitSignal(SESSION_CLEARED, String.valueOf(generation));
        } catch (Exception error) { failed(generation, error); }
    }
}
