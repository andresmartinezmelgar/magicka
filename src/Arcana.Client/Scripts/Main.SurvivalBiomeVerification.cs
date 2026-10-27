using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private bool _survivalBiomeSmoke;

    private async Task RunSurvivalBiomeSmoke(string[] arguments)
    {
        _survivalBiomeSmoke = true; _playing = false; _launcher.Hide();
        try
        {
            string? requested = arguments.FirstOrDefault(a => a.StartsWith("--smoke-viewport="));
            if (requested != null)
            {
                var dimensions = requested["--smoke-viewport=".Length..].Split('x');
                var size = new Vector2I(int.Parse(dimensions[0]), int.Parse(dimensions[1]));
                GetWindow().ContentScaleSize = size; GetWindow().Size = size;
            }
            string? captures = arguments.FirstOrDefault(a => a.StartsWith("--survival-biome-captures="))?["--survival-biome-captures=".Length..];
            if (captures != null) System.IO.Directory.CreateDirectory(captures);
            foreach (var theme in SurvivalBiomes.Available)
            {
                int seed = Enumerable.Range(0, 10_000).First(s => SurvivalBiomes.ThemeFor(new(s)) == theme);
                var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion,
                    SurvivalMap = new(seed), MaxPlayers = 1 });
                var player = world.AddPlayer("Bioma")!; var snapshot = world.Snapshot();
                foreach (var step in _renderer.PrepareSurvivalBiomeVerification(world.Map))
                { step(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
                _renderer.PreviewFocus = ArenaRenderer.World(V2.Zero);
                for (int frame = 0; frame < 4; frame++)
                { _renderer.Render(snapshot, player.Id, 1 / 60.0, true); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
                _renderer.VerifySurvivalBiome(theme);
                if (captures != null)
                {
                    using var image = GetViewport().GetTexture().GetImage();
                    if (image.SavePng(System.IO.Path.Combine(captures, theme + ".png")) != Error.Ok)
                        throw new InvalidOperationException("No se pudo guardar la captura del bioma " + theme);
                }
                GD.Print($"SURVIVAL_BIOME_OK: {theme} · semilla {seed} · decoración {_renderer.SurvivalDecorations}");
            }
            GD.Print("SURVIVAL_BIOMES_SMOKE_OK: " + SurvivalBiomes.Available.Count);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr("SURVIVAL_BIOMES_SMOKE_FAILED: " + error);
            GetTree().Quit(1);
        }
    }
}
