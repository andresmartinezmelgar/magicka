using System.Collections.Concurrent;
using System.Text.Json;
using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    public void StartRenderProfile(ArenaMapId map = ArenaMapId.Classic)
    {
        if (_profiler != null) return;
        ReturnToLauncher(); _launcher.Visible = false;
        _profiler = new RenderProfiler(); AddChild(_profiler);
        try { _profiler.Initialize(_renderer, _ui, _engineLog, OS.GetCmdlineUserArgs(), map, (status, report) =>
        {
            if (report.ValueKind != JsonValueKind.Undefined) _lastRenderProfile = report;
            var completed = _profiler; _profiler = null; completed?.QueueFree();
            ReturnToLauncher(); _launcher.ShowStatus(status);
        }); }
        catch (Exception error)
        {
            _profiler?.QueueFree(); _profiler = null; ReturnToLauncher();
            Diagnostics.Error("render.profile_failed", "No se pudo iniciar la medición gráfica.", error, Context());
            _launcher.ShowError("No se pudo iniciar la medición gráfica: " + error.Message);
            if (RenderProfiler.Requested(OS.GetCmdlineUserArgs())) { GD.PushError("PROFILE_FAILED: " + error.Message); GetTree().Quit(1); }
        }
    }
}
