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
            // Finalizar un nodo puede soltar recursos que se recogen en la pasada siguiente.
            for (int pass = 0; pass < 3; pass++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers();
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
            if (errors.ErrorCount > 0) throw new InvalidOperationException("Godot registró errores al liberar los recursos de la simulación.");
            GD.Print(success);
            tree.Quit();
        }
        catch (Exception error) { GD.PushError(failure + ": limpieza: " + error); tree.Quit(1); }
    }
}
