using Godot;

namespace Arcana.Client;

public partial class Main
{
    private static async Task FinishVisualVerificationAfterCleanup(SceneTree tree, EngineLogBridge errors,
        string success, string failure)
    {
        try
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            GC.Collect(); GC.WaitForPendingFinalizers();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (errors.ErrorCount > 0) throw new InvalidOperationException("Godot registró errores al liberar los recursos de la simulación.");
            GD.Print(success);
            tree.Quit();
        }
        catch (Exception error) { GD.PushError(failure + ": limpieza: " + error); tree.Quit(1); }
    }
}
