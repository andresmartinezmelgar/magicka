package com.arcana.firebase;

public final class UpdateVersionPolicyTest {
    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }

    public static void main(String[] args) {
        check(UpdateVersionPolicy.accepts(29016, 29016, 29015, false), "Se bloqueó la nueva versión estable.");
        check(UpdateVersionPolicy.accepts(29016, 29016, 29015, true), "Se bloqueó la primera prueba corregida.");
        check(UpdateVersionPolicy.accepts(29016, 29016, 29016, true), "Se bloqueó una compilación posterior de igual versión.");
        check(!UpdateVersionPolicy.accepts(29016, 29016, 29016, false), "Se reinstaló la versión estable.");
        for (boolean preview : new boolean[] { false, true }) {
            check(!UpdateVersionPolicy.accepts(29015, 29015, 29016, preview), "Se permitió retroceder de versión.");
            check(!UpdateVersionPolicy.accepts(29016, 29017, 29015, preview), "Se ignoró la versión esperada de la descarga.");
            check(!UpdateVersionPolicy.accepts(0, 0, 0, preview), "Se aceptó una versión inválida.");
        }
        System.out.println("ANDROID_UPDATE_POLICY_OK: versión nueva, prueba de igual código, estable sin reinstalación y rechazo de retrocesos y APK incorrecta.");
    }
}
