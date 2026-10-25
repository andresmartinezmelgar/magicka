using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class CombatEffects
{
    private const int MaximumWaterChannels = 8;
    private sealed class WaterChannel
    {
        internal int Owner, Seen, Charges;
        internal Vector3 Origin, Center, Direction;
        internal float Range, Age, Released;
    }
    private readonly List<WaterChannel> _waterChannels = [];
    private readonly Stack<WaterChannel> _freeWaterChannels = [];
    private InstanceDrawBatch? _waterJetBatch;
    private int _waterGeneration;
    internal int ActiveWaterJets => _waterJetBatch?.Count ?? 0;

    private void PrepareWaterJets() => _waterJetBatch ??= new(this, "WaterJetBatch",
        ElementChannelVisuals.WaterJet(), ElementChannelVisuals.Material(9), MaximumWaterChannels * 3,
        custom: true, boundsPadding: .5f);

    private void AdvanceWaterJets(WorldSnapshot snapshot, float dt, bool playing)
    {
        _waterGeneration++;
        if (playing && EnhancedSpells)
        foreach (var beam in snapshot.Beams)
        {
            if (ElementChannelVisuals.ParticleShape(beam) != ElementChannelVisuals.Water ||
                V2.Distance(beam.From, beam.To) < .05f) continue;
            WaterChannel? channel = null;
            foreach (var item in _waterChannels)
                if (item.Owner == beam.Owner && item.Released == 0) { channel = item; break; }
            if (channel == null)
            {
                if (_waterChannels.Count >= MaximumWaterChannels) continue;
                channel = _freeWaterChannels.Count > 0 ? _freeWaterChannels.Pop() : new();
                channel.Owner = beam.Owner; channel.Age = channel.Released = 0;
                _waterChannels.Add(channel); PrepareWaterJets();
            }
            channel.Origin = FlameOrigin?.Invoke(beam) ?? ArenaRenderer.World(beam.From, 1);
            channel.Center = ArenaRenderer.World(beam.From, channel.Origin.Y);
            channel.Direction = ArenaRenderer.World(beam.To - beam.From).Normalized();
            channel.Range = V2.Distance(beam.From, beam.To);
            channel.Charges = SpellVisualStrength.For(beam.Elements).Charges;
            channel.Seen = _waterGeneration;
        }
        _waterJetBatch?.Begin();
        for (int i = _waterChannels.Count - 1; i >= 0; i--)
        {
            var item = _waterChannels[i]; item.Age += dt;
            if (item.Seen != _waterGeneration) item.Released += dt;
            if (item.Released >= .52f)
            {
                _freeWaterChannels.Push(item); _waterChannels[i] = _waterChannels[^1];
                _waterChannels.RemoveAt(_waterChannels.Count - 1); continue;
            }
            var side = Vector3.Up.Cross(item.Direction).Normalized();
            for (int branch = -1; branch <= 1; branch++)
            {
                float angle = branch * .18f;
                var ray = item.Direction * MathF.Cos(angle) + side * MathF.Sin(angle);
                var end = item.Center + ray * (item.Range * .97f);
                var path = end - item.Origin; float span = path.Length();
                if (span < .05f) continue;
                var forward = path / span;
                var lateral = Vector3.Up.Cross(forward).Normalized();
                var up = forward.Cross(lateral).Normalized();
                float width = branch == 0 ? 1 : .55f;
                var basis = new Basis(lateral * width, up * width, forward * span);
                _waterJetBatch!.Add(new Transform3D(basis, item.Origin), new(
                    item.Owner * .31f + branch * .7f,
                    Math.Clamp(item.Released / .52f, 0, 1),
                    Math.Clamp(item.Age / .52f, 0, 1), item.Charges / 3f));
            }
        }
        _waterJetBatch?.Commit();
    }

    private void ClearWaterJets()
    {
        foreach (var item in _waterChannels) _freeWaterChannels.Push(item);
        _waterChannels.Clear(); _waterJetBatch?.Clear(); _waterGeneration = 0;
    }
}
