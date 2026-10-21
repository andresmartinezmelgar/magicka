namespace Arcana.Core;

public sealed partial class GameWorld
{
    private static readonly float[] OracleRetreatAngles = [0, 0.55f, -0.55f, 1.1f, -1.1f, 1.5f, -1.5f];
    private static bool AcceptsDefenseElement(Wizard target, Element element) =>
        target.VulnerableElement is not { } weakness || weakness == element;

    // Los hechizos se filtran por componentes en Hit; los poderes de daño fijo
    // declaran sus elementos y solo aportan la fracción del elemento permitido.
    private static float DefenseElementFraction(Wizard target, Element[]? elements)
    {
        if (target.VulnerableElement is not { } weakness) return 1;
        if (elements == null || elements.Length == 0) return 0;
        int matching = 0, offensive = 0;
        foreach (var element in elements)
        {
            if (element is Element.Life or Element.Shield) continue;
            offensive++; if (element == weakness) matching++;
        }
        return offensive == 0 ? 0 : matching / (float)offensive;
    }

    private Wizard? DefenseTarget(DefenseMinion minion, float dt)
    {
        var body = minion.Body;
        minion.FocusTime = Math.Max(0, minion.FocusTime - dt);
        if (minion.Kind == MinionKind.Prism && minion.FocusTime > 0 &&
            _players.TryGetValue(minion.FocusTargetId, out var locked) && locked.Alive &&
            V2.Distance(body.Position, locked.Position) < 40) return locked;
        Wizard? nearest = null, focused = null;
        float distance = float.PositiveInfinity, focusScore = float.PositiveInfinity;
        foreach (var player in _players.Values)
        {
            if (!player.Alive) continue;
            float separation = V2.Distance(body.Position, player.Position);
            if (separation < distance) { nearest = player; distance = separation; }
            if (separation > 32 || player.Id == minion.FocusTargetId) continue;
            float score = player.Health / player.MaxHealth * 10 + separation * 0.12f;
            if (score < focusScore) { focused = player; focusScore = score; }
        }
        if (minion.Kind != MinionKind.Prism) return nearest;
        var target = focused ?? nearest;
        minion.FocusTargetId = target?.Id ?? 0;
        minion.FocusTime = DefenseRules.PrismFocusSeconds;
        return target;
    }

    private V2 OracleRetreat(Wizard body, Wizard target)
    {
        var away = (body.Position - target.Position).Normalized;
        if (away.LengthSquared < 0.01f) away = new(1, 0);
        // Elegir una salida transitable en esquinas; nunca avanzar hacia el atacante
        // simplemente porque la dirección de retirada está bloqueada.
        foreach (float angle in OracleRetreatAngles)
        {
            var direction = new V2(away.X * MathF.Cos(angle) - away.Y * MathF.Sin(angle),
                away.X * MathF.Sin(angle) + away.Y * MathF.Cos(angle));
            if (Walkable(body.Position + direction * 1.5f)) return direction;
        }
        return V2.Zero;
    }

    private void TickOracleShield(DefenseMinion oracle, float dt)
    {
        oracle.SupportCooldown -= dt;
        if (oracle.SupportCooldown > 0 || oracle.Body.Frozen > 0) return;
        oracle.SupportCooldown = DefenseRules.OracleShieldInterval;
        foreach (var ally in _minions.Values)
        {
            var body = ally.Body;
            if (!body.Alive || ally.AuraRepairCooldown > 0.02f ||
                V2.Distance(body.Position, oracle.Body.Position) > DefenseRules.OracleAuraRadius ||
                !Visible(oracle.Body.Position, body.Position, oracle.Body.Id)) continue;
            float shieldBefore = body.Shield;
            float capacity = DefenseRules.OracleShield * oracle.Strength;
            bool first = body.ShieldMax <= 0;
            body.ShieldMax = Math.Max(body.ShieldMax, capacity);
            body.Shield = Math.Min(body.ShieldMax, body.Shield + (first ? capacity : DefenseRules.OracleShieldRepair * oracle.Strength));
            _statistics.Shield(oracle.Body.Id, body.Shield - shieldBefore, !first);
            body.ShieldTime = Math.Max(body.ShieldTime, DefenseRules.OracleShieldDuration);
            // Varias auras no multiplican la frecuencia de reparación de un aliado.
            ally.AuraRepairCooldown = DefenseRules.OracleShieldInterval;
        }
        Emit("nova", oracle.Body.Position, [Element.Shield, Element.Arcane], "Reparación rúnica", DefenseRules.OracleAuraRadius);
    }

    private void CastOracleBombardment(DefenseMinion oracle, Wizard target)
    {
        if (_fields.Count > 37) return;
        _statistics.Cast(oracle.Body.Id, StatisticAttack.OracleBombardment, [Element.Earth, Element.Arcane]);
        var forward = (target.Position - oracle.Body.Position).Normalized;
        var side = forward.Perpendicular;
        // En los pasillos estrechos, orientar la línea a lo largo del suelo disponible.
        if (!Walkable(target.Position + side * 3.6f) || !Walkable(target.Position - side * 3.6f)) side = forward;
        for (int i = -1; i <= 1; i++)
        {
            V2 at = target.Position + side * (i * 3.6f);
            if (!Walkable(at)) continue;
            float delay = DefenseRules.OracleStrikeDelay + (i + 1) * 0.3f;
            _fields.Add(new Field { Id = _nextEntity++, Owner = oracle.Body.Id, Position = at,
                Radius = DefenseRules.OracleStrikeRadius, Delay = delay, Lifetime = delay + 0.2f,
                Power = DefenseRules.OracleStrikeDamage, Kind = "oracle_bombardment",
                Recipe = new([Element.Earth, Element.Arcane]) });
        }
        Emit("cast", oracle.Body.Position, [Element.Earth, Element.Arcane], "Bombardeo rúnico");
    }
}
