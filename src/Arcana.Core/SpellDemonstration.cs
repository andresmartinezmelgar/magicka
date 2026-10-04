namespace Arcana.Core;

/// <summary>Repite un combate pequeño usando las mismas intenciones y reglas que una partida.</summary>
public sealed class SpellDemonstration
{
    private readonly SpellRecipe _spell;
    private readonly CastMode _mode;
    public DemonstrationKind Kind { get; }
    public GameWorld World { get; private set; } = null!;
    public int Cycle { get; private set; }
    public double Duration => Kind == DemonstrationKind.Capture ? 14 : 6;
    public double Time => World.Time;
    private Wizard _caster = null!, _rival = null!, _ally = null!, _other = null!;
    private V2 _aim;
    public int LocalId => _caster.Id;

    public SpellDemonstration(Element[] elements, CastMode mode = CastMode.Forward, DemonstrationKind kind = DemonstrationKind.Combination)
    {
        _spell = new(elements); _mode = mode; Kind = kind; Reset();
    }
    public void Reset()
    {
        Cycle++;
        World = new(new() { Mode = GameMode.Warfare, MaxPlayers = 8, FriendlyFire = false });
        _caster = World.AddPlayer("Lanzador", 0)!; _rival = World.AddPlayer("Enemigo", 1)!;
        _ally = World.AddPlayer("Aliado", 0)!; _other = World.AddPlayer("Vecino", 1)!;
        _caster.Position = new(-5, 0); _rival.Position = new(-1.5f, 0);
        _ally.Position = new(-1, 2); _other.Position = new(0, 1);
        foreach (var p in World.Players.Values) p.Invulnerable = 0;
        _ally.Health = 400;
        _aim = _spell.Healing && !_spell.Has(Element.Shield) ? _ally.Position : _rival.Position;
        if (_mode == CastMode.Self) _caster.Health = 450;
        if (_spell.Has(Element.Shield) && _spell.Has(Element.Arcane))
        { _rival.Position = new(1.2f, 0); _aim = _rival.Position; }
        if (_mode == CastMode.Nova) { _rival.Position = new(-2.3f, 0); _ally.Position = new(-5, 2.8f); _other.Position = new(-3, -2); }
        if (Kind == DemonstrationKind.CrossedBeams)
        {
            _caster.Position = new(-5, 0); _rival.Position = new(5, 0);
            _ally.Position = new(-8, -3); _other.Position = new(0, 4);
            _aim = _rival.Position;
        }
        if (Kind == DemonstrationKind.ShieldRefraction)
        {
            _caster.Position = new(-4, 0); _rival.Position = new(3, 0);
            _ally.Position = new(-1, -1.5f); _other.Position = new(10, 3);
            _aim = _rival.Position;
        }
        if (Kind == DemonstrationKind.Capture)
        {
            _caster.Position = V2.Zero; _rival.Position = new(18, 8);
            _ally.Position = new(-18, 8); _other.Position = new(18, -8);
        }
        _caster.Facing = (_aim - _caster.Position).Normalized;
    }
    private void Message(Wizard p, string type, Element element = Element.Water, V2? aim = null, V2? destination = null)
    {
        var target = aim ?? _aim;
        World.ApplyMessage(p.Id, new() { Type = type, Sequence = p.LastSequence + 1, Element = element,
            AimX = target.X, AimY = target.Y, MoveTo = destination.HasValue,
            TargetX = destination?.X ?? 0, TargetY = destination?.Y ?? 0 });
    }
    private void Cast(Wizard p, Element[] elements, string type, V2 aim)
    {
        p.Facing = (aim - p.Position).Normalized;
        Message(p, "input", aim: aim);
        foreach (var element in elements) Message(p, "element", element);
        Message(p, type);
    }
    public void Step()
    {
        if (World.Time >= Duration) Reset();
        long tick = World.Tick;
        var mine = World.DemonstrationMine;
        Message(_caster, "input", aim: _aim);
        Message(_rival, "input", aim: _caster.Position,
            destination: Kind == DemonstrationKind.Combination && mine?.Kind == "mine" && tick > 65 ? mine.Position : null);
        Message(_ally, "input", destination: Kind == DemonstrationKind.Combination && mine?.Kind == "healing_mine" && tick > 65 ? mine.Position : null);
        Message(_other, "input");
        if (tick == 12)
        {
            if (Kind == DemonstrationKind.Combination)
                Cast(_caster, _spell.Elements, _mode == CastMode.Forward ? "cast" : _mode == CastMode.Self ? "self" : "nova", _aim);
            if (Kind == DemonstrationKind.CrossedBeams)
            {
                Cast(_caster, [Element.Arcane, Element.Arcane, Element.Fire], "cast", _rival.Position);
                Cast(_rival, [Element.Arcane, Element.Arcane, Element.Cold], "cast", _caster.Position);
            }
            if (Kind == DemonstrationKind.ShieldRefraction)
            {
                Cast(_rival, [Element.Shield, Element.Fire], "self", _caster.Position);
                Cast(_caster, [Element.Arcane, Element.Arcane], "cast", _rival.Position);
            }
        }
        if (tick == 58 && _mode == CastMode.Forward && _spell.Shape == SpellShape.Rock) Message(_caster, "release");
        if (tick == 180) { Message(_caster, "release"); Message(_rival, "release"); }
        // La segunda mitad enseña cómo el rojo neutraliza el color azul antes de ocupar el punto.
        if (Kind == DemonstrationKind.Capture && tick == 300)
        { _caster.Position = new(-5, 0); _rival.Position = V2.Zero; }
        World.Step();
    }
}
