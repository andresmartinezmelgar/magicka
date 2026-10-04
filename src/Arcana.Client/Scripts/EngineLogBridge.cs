using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Incluye errores del motor y de scripts; nunca captura valores de variables.</summary>
public partial class EngineLogBridge : Logger
{
    public override void _LogError(string function, string file, int line, string code, string rationale,
        bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        var frames = new List<string>();
        foreach (var trace in scriptBacktraces.Take(3))
            for (int i = 0; i < Math.Min(trace.GetFrameCount(), 12); i++)
                frames.Add($"{trace.GetFrameFunction(i)} ({trace.GetFrameFile(i)}:{trace.GetFrameLine(i)})");
        Diagnostics.Log.Write(errorType == 1 ? DiagnosticLevel.Warning : DiagnosticLevel.Error,
            "engine.error", string.IsNullOrEmpty(rationale) ? code : rationale, context:
            new Dictionary<string, object?> { ["function"] = function, ["file"] = file, ["line"] = line,
                ["errorType"] = errorType, ["stack"] = string.Join("\n", frames) });
    }
    public override void _LogMessage(string message, bool error)
    { if (error) Diagnostics.Error("engine.stderr", message); }
}
