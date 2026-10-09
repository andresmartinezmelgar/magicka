using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private int _practiceTicks;
    private void PracticeChoice(string keys)
    {
        var choice = (OptionButton)_runtime.UiRoot.FindChild("PracticeCasterChoice", true, false);
        int index = PracticeLayout.CasterSpells.ToList().FindIndex(c => c.Keys == keys);
        choice.Select(index); choice.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
    }
    private void StepPracticeSmoke()
    {
        _practiceTicks++;
        if (_practiceTicks == 60) PracticeChoice("QR");
        if (_practiceTicks == 180)
        {
            PracticeChoice("W"); var session = _runtime.PracticeSession!;
            session.PrepareHealing(); session.Player.Position = PracticeLayout.CasterPosition + new V2(3, 0);
        }
    }
    private void CheckPractice(WorldSnapshot snapshot)
    {
        if (_practiceTicks < 2) return;
        var session = _runtime.PracticeSession!; var renderer = _runtime.Renderer;
        var panel = (PracticePanel)_runtime.UiRoot.FindChild("PracticePanel", true, false);
        var choice = (OptionButton)panel.FindChild("PracticeCasterChoice", true, false);
        var cadence = (Label)panel.FindChild("PracticeCasterCadence", true, false);
        var objective = (Label)_runtime.Hud!.FindChild("MatchObjective", true, false);
        SnapshotValidation.Check(snapshot);
        bool valid = snapshot.Map == ArenaMapId.Practice && snapshot.Points.Length == 0 && renderer.Points.Count == 0 &&
            renderer.FindChildren("PracticeRespawn", "", true, false).Count == 1 &&
            renderer.FindChildren("PracticeBarrierLabel*", "", true, false).Count == PracticeLayout.Barriers.Count &&
            choice.ItemCount == PracticeLayout.CasterSpells.Count && objective.Text.Contains("Sin puntos") &&
            session.Targets.All(t => t.Wizard.Position == t.Definition.Position) && panel.IsVisibleInTree();
        if (_practiceTicks is >= 3 and < 60) valid &= snapshot.Beams.Any(b => b.Owner == session.Caster.Id);
        if (_practiceTicks is > 61 and < 180) valid &= cadence.Text.Contains("cada 2 s");
        if (_practiceTicks > 181) valid &= cadence.Text.Contains("continuo") && snapshot.Beams.Any(b => b.Owner == session.Caster.Id);
        if (!valid) { GD.PushError("PRACTICE_SMOKE_FAILED: estaciones, selector, geometría o cadencia incorrectos."); GetTree().Quit(1); return; }
        if (_practiceTicks >= 270)
        {
            if (session.World.PracticeReading(session.Player.Id).Healing < 200)
            { GD.PushError("PRACTICE_SMOKE_FAILED: el lanzador no cura con la combinación elegida en la interfaz."); GetTree().Quit(1); return; }
            CompleteSmoke($"PRACTICE_SMOKE_OK: {session.Targets.Count} objetivos, {PracticeLayout.Barriers.Count} barreras/minas, lanzador configurable, medición y respawn único.");
        }
    }
}
