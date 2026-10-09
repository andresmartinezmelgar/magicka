namespace Arcana.Core;

public sealed record PracticeTarget(PracticeTargetDefinition Definition, Wizard Wizard);

/// <summary>Estaciones permanentes sobre la misma simulación de combate, sin victoria ni captura.</summary>
public sealed class PracticeSession
{
    public GameWorld World { get; }
    public Wizard Player { get; }
    public Wizard Caster { get; }
    public IReadOnlyList<PracticeTarget> Targets { get; }
    public SpellCombination CasterSpell { get; private set; } = SpellCatalogue.FromKeys("SSS")!;
    public bool CasterEnabled { get; private set; } = true;
    private readonly bool[] _alive;
    private readonly int[] _barrierIds = new int[PracticeLayout.Barriers.Count];
    private readonly long[] _barrierReady = new long[PracticeLayout.Barriers.Count];
    private long _nextCast;
    public string CasterDescription => (CasterSpell.Keys.Length == 0 ? "Empuje" : CasterSpell.Keys + " · " + SpellCatalogue.Name(CasterSpell.Elements))
        + (Continuous ? " · continuo" : " · cada 2 s") + " →";
    private bool Continuous => CasterSpell.Recipe.Shape is SpellShape.Beam or SpellShape.Spray or SpellShape.Lightning;

    public PracticeSession(string name = "Mago", WizardType type = WizardType.Balanced)
    {
        World = new(new() { Map = ArenaMapId.Practice, Mode = GameMode.Warfare, MaxPlayers = 16, FriendlyFire = true });
        Player = World.AddPlayer(name, 0, wizardType: type)!;
        Targets = Array.AsReadOnly(PracticeLayout.Targets.Select(d =>
        {
            var wizard = World.AddPlayer(d.Name, 1)!;
            World.RegisterPracticeSpawn(wizard, d.Position);
            return new PracticeTarget(d, wizard);
        }).ToArray());
        Caster = World.AddPlayer("Mago lanzador", 1)!;
        World.RegisterPracticeSpawn(Caster, PracticeLayout.CasterPosition);
        _alive = new bool[Targets.Count];
        for (int i = 0; i < Targets.Count; i++) { InitializeTarget(Targets[i]); _alive[i] = true; }
        MaintainBarriers();
        World.ClearPracticeEvents();
    }

    private void Message(Wizard wizard, string type, Element element = Element.Water)
    {
        var aim = wizard.Position + PracticeLayout.CasterDirection * 30;
        World.ApplyMessage(wizard.Id, new() { Type = type, Sequence = wizard.LastSequence + 1,
            Element = element, AimX = aim.X, AimY = aim.Y });
    }
    private void InitializeTarget(PracticeTarget target)
    {
        var wizard = target.Wizard;
        switch (target.Definition.Status)
        {
            case PracticeStatus.Wet: wizard.Wet = 6; break;
            case PracticeStatus.Burning: wizard.Burning = SpellBalance.BurnDuration; wizard.BurnOwner = wizard.Id; break;
            case PracticeStatus.Chilled: wizard.Chill = 60; break;
            case PracticeStatus.Frozen: wizard.Frozen = 1.4f; wizard.Chill = 100; break;
            case PracticeStatus.Haste: wizard.Haste = 4; break;
            case PracticeStatus.Protection:
                foreach (var element in SpellCatalogue.FromKeys(target.Definition.Keys)!.Elements) Message(wizard, "element", element);
                Message(wizard, "self"); break;
        }
    }
    private void PreserveStatus(PracticeTarget target)
    {
        var p = target.Wizard;
        // Compensa solo el paso del tiempo. Agua/fuego/frío aún pueden neutralizar el estado.
        if (target.Definition.Status == PracticeStatus.Wet && p.Wet > 0) p.Wet += GameWorld.StepSeconds;
        if (target.Definition.Status == PracticeStatus.Burning && p.Burning > 0) p.Burning += GameWorld.StepSeconds;
        if (target.Definition.Status is PracticeStatus.Chilled or PracticeStatus.Frozen && p.Chill > 0)
            p.Chill = Math.Min(100, p.Chill + 10 * GameWorld.StepSeconds);
        if (target.Definition.Status == PracticeStatus.Frozen && p.Frozen > 0) p.Frozen += GameWorld.StepSeconds;
        if (target.Definition.Status == PracticeStatus.Haste && p.Haste > 0) p.Haste += GameWorld.StepSeconds;
        if (target.Definition.Status == PracticeStatus.Protection)
        { if (p.ShieldTime > 0) p.ShieldTime += GameWorld.StepSeconds; if (p.WardTime > 0) p.WardTime += GameWorld.StepSeconds; }
    }
    public void SetCasterSpell(string keys)
    {
        var spell = PracticeLayout.CasterSpells.FirstOrDefault(c => c.Keys == keys);
        if (spell == null) throw new ArgumentException("Combinación no disponible para el lanzador.", nameof(keys));
        World.StopPracticeCaster(Caster); CasterSpell = spell; _nextCast = World.Tick;
    }
    public void SetCasterEnabled(bool enabled)
    { CasterEnabled = enabled; if (!enabled) World.StopPracticeCaster(Caster); else _nextCast = World.Tick; }
    public void ResetTarget(int id)
    {
        var target = Targets.FirstOrDefault(t => t.Wizard.Id == id);
        if (target != null)
        {
            World.ResetPracticeActor(target.Wizard); InitializeTarget(target);
            for (int i = 0; i < Targets.Count; i++) if (Targets[i] == target) _alive[i] = true;
            return;
        }
        if (id == Player.Id || id == Caster.Id) World.ResetPracticeActor(World.Players[id]);
    }
    public void PrepareHealing()
    { ResetTarget(Player.Id); Player.Health = Player.MaxHealth * 0.4f; }
    private void MaintainBarriers()
    {
        foreach (var station in PracticeLayout.Barriers)
        {
            int i = station.Index;
            if (_barrierIds[i] > 0)
            {
                if (World.KeepPracticeBarrier(_barrierIds[i])) continue;
                _barrierIds[i] = 0; _barrierReady[i] = World.Tick + 120;
            }
            if (World.Tick < _barrierReady[i]) continue;
            _barrierIds[i] = World.CreatePracticeBarrier(station, station.Combination.Recipe.Healing ? Player : Targets[0].Wizard);
        }
    }
    public void Step()
    {
        for (int i = 0; i < Targets.Count; i++)
        {
            var target = Targets[i]; target.Wizard.Knockback = V2.Zero;
            target.Wizard.Position = target.Definition.Position; Message(target.Wizard, "input");
            if (target.Wizard.Alive) PreserveStatus(target);
        }
        Caster.Knockback = V2.Zero; Caster.Position = PracticeLayout.CasterPosition;
        Message(Caster, "input");
        if (CasterEnabled && Caster.Alive && Caster.Frozen <= 0 && Caster.ActionCooldown <= 0)
        {
            if (Continuous ? Caster.Channel == null : World.Tick >= _nextCast && Caster.Charging == null)
            {
                Message(Caster, "clear");
                foreach (var element in CasterSpell.Elements) Message(Caster, "element", element);
                Message(Caster, "cast"); _nextCast = World.Tick + 120;
            }
            else if (!Continuous && CasterSpell.Recipe.Shape == SpellShape.Force && Caster.Channel != null)
                Message(Caster, "release");
        }
        MaintainBarriers(); World.Step();
        for (int i = 0; i < Targets.Count; i++)
        {
            var target = Targets[i];
            if (target.Wizard.Alive && !_alive[i]) InitializeTarget(target);
            _alive[i] = target.Wizard.Alive; target.Wizard.Knockback = V2.Zero;
            target.Wizard.Position = target.Definition.Position;
        }
        Caster.Position = PracticeLayout.CasterPosition; Caster.Knockback = V2.Zero;
    }
}
