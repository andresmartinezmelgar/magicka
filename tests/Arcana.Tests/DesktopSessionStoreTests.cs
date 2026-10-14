using Arcana.Core;

static class DesktopSessionStoreTests
{
    public static void Run(Action<string, Action> test)
    {
        test("Windows: token Firebase protegido, recuperado y eliminado con DPAPI", () =>
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = Path.Combine(Path.GetTempPath(), "arcana-desktop-session-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new DesktopSessionStore(dir);
                string refresh = "test-refresh-" + Guid.NewGuid().ToString("N");
                store.Save("uid-de-prueba", "Mago de pruebas", refresh);
                string filename = Path.Combine(dir, "google-desktop-session.dat");
                byte[] ciphertext = File.ReadAllBytes(filename);
                if (System.Text.Encoding.UTF8.GetString(ciphertext).Contains(refresh, StringComparison.Ordinal))
                    throw new Exception("El refresh token quedó en texto plano.");
                var restored = new DesktopSessionStore(dir).Load();
                if (restored?.Uid != "uid-de-prueba" || restored.DisplayName != "Mago de pruebas" ||
                    restored.RefreshToken != refresh || restored.ProjectId != DesktopSessionStore.FirebaseProjectId)
                    throw new Exception("La sesión cifrada no se recuperó correctamente.");
                store.Delete();
                if (store.Load() != null || File.Exists(filename))
                    throw new Exception("Cerrar sesión dejó credenciales persistidas.");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        });
        test("Windows: no acepta sesión cifrada alterada", () =>
        {
            if (!OperatingSystem.IsWindows()) return;
            string dir = Path.Combine(Path.GetTempPath(), "arcana-desktop-session-bad-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new DesktopSessionStore(dir);
                store.Save("uid-de-prueba", "Mago", "refresh-token-de-prueba-123456789");
                string filename = Path.Combine(dir, "google-desktop-session.dat");
                byte[] ciphertext = File.ReadAllBytes(filename);
                ciphertext[ciphertext.Length / 2] ^= 0x55;
                File.WriteAllBytes(filename, ciphertext);
                if (store.Load() != null || File.Exists(filename))
                    throw new Exception("Archivo cifrado modificado aceptado.");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        });
        test("Windows: no guarda credenciales Google inválidas", () =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var store = new DesktopSessionStore(Path.Combine(Path.GetTempPath(), "arcana-validation-" + Guid.NewGuid().ToString("N")));
            bool rejected = false;
            try { store.Save("", "Mago", "refresh-token-de-prueba-123456789"); }
            catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Se ha admitido una sesión sin UID.");
        });
    }
}
