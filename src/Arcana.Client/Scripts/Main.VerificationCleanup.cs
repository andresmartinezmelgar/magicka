using Godot;

namespace Arcana.Client;

public partial class Main
{
    private bool _verificationEnding;

    internal void QuitVerification(int exitCode = 0)
    {
        if (_verificationEnding) return;
        _verificationEnding = true;
        var tree = GetTree(); var errors = _engineLog;
        QueueFree();
        _ = FinishVisualVerificationAfterCleanup(tree, errors, "VISUAL_SHUTDOWN_OK", "VISUAL_SMOKE_FAILED", exitCode);
    }

    internal static async Task DrainVisualVerificationResources(SceneTree tree)
    {
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        // Finalizar un nodo puede soltar recursos que se recogen en la pasada siguiente.
        for (int pass = 0; pass < 3; pass++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    internal static async Task FinishVisualVerificationAfterCleanup(SceneTree tree, EngineLogBridge errors,
        string success, string failure, int exitCode = 0)
    {
        try
        {
            await DrainVisualVerificationResources(tree);
            if (errors.ErrorCount > 0) throw new InvalidOperationException("Godot registró errores al liberar los recursos de la simulación.");
            GD.Print(success);
            tree.Quit(exitCode);
        }
        catch (Exception error) { GD.PushError(failure + ": limpieza: " + error); tree.Quit(1); }
    }
}
