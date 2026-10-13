#!/usr/bin/env python3
"""Static checks for the Google login integration; no live credentials are used."""
from pathlib import Path

root = Path(__file__).resolve().parents[1]
checks = {
    "src/Arcana.Server/AccountService.cs": [
        "VerifyIdTokenAsync", "sign_in_provider", "google.com",
        "StartLogin(", "CompleteLogin(", "Authenticate(",
    ],
    "src/Arcana.Core/AccountClient.cs": [
        "account/login/start", "account/login/poll", "AuthenticationHeaderValue",
    ],
    "src/Arcana.Core/LocalFirebaseHosting.cs": [
        "GOOGLE_APPLICATION_CREDENTIALS",
    ],
    "src/Arcana.Client/Scripts/Launcher.cs": [
        "Entrar o registrarse con Google",
    ],
    "scripts/test-accounts.sh": ["Arcana.Account.Tests"],
}
errors = []
for file, markers in checks.items():
    source = (root / file).read_text(encoding="utf-8")
    for marker in markers:
        if marker not in source:
            errors.append(f"{file}: missing {marker}")
for error in errors:
    print("FAIL:", error)
if errors:
    raise SystemExit(1)
print("FIREBASE_AUTH_PREFLIGHT_OK: integration references exist.")
print("A real Google sign-in and Firebase project configuration require a separate live test.")
