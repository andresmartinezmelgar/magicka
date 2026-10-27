namespace Arcana.Core;

public enum BuffKind { Power, Protection, Regeneration, Vitality, Speed, Energy, Healing }

public static class RelicBuffs
{
    public const int Count = 7;
    public const int SpawnCount = Count * 2;
    public const float RespawnSeconds = 12, PickupRadius = 1.1f;
    public static string Name(BuffKind kind) => kind switch
    {
        BuffKind.Power => "Potencia", BuffKind.Protection => "Protección", BuffKind.Regeneration => "Regeneración",
        BuffKind.Vitality => "Vitalidad", BuffKind.Speed => "Agilidad", BuffKind.Energy => "Energía", _ => "Sanación"
    };
    public static string Description(BuffKind kind) => kind switch
    {
        BuffKind.Power => "+10 % de daño por acumulación, también en bastón y quemaduras.",
        BuffKind.Protection => "Recibes un 8 % menos de daño por acumulación. Se combina multiplicando; máximo de reducción: 80 %.",
        BuffKind.Regeneration => "+20 puntos de vida por segundo por acumulación, hasta tu vida máxima.",
        BuffKind.Vitality => "+100 de vida máxima y actual por acumulación. La capacidad extra desaparece al morir.",
        BuffKind.Speed => "+5 % de velocidad por acumulación, hasta +100 %.",
        BuffKind.Energy => "+1 punto de energía por segundo por acumulación.",
        _ => "+10 % a las curaciones propias, rayos, minas y regeneración de escudo por acumulación. No amplifica la regeneración de objetos."
    };
    public static float Power(Wizard p) => 1 + p.BuffStacks[(int)BuffKind.Power] * 0.1f;
    public static float Incoming(Wizard p) => Math.Max(0.2f, MathF.Pow(0.92f, p.BuffStacks[(int)BuffKind.Protection]));
    public static float Regeneration(Wizard p) => p.BuffStacks[(int)BuffKind.Regeneration] * 20f;
    public static float Speed(Wizard p) => 1 + Math.Min(1, p.BuffStacks[(int)BuffKind.Speed] * 0.05f);
    public static float Energy(Wizard p) => p.BuffStacks[(int)BuffKind.Energy];
    public static float Healing(Wizard p) => 1 + p.BuffStacks[(int)BuffKind.Healing] * 0.1f;
    public static string DescribeTotal(BuffKind kind, int stacks) => kind switch
    {
        BuffKind.Power => $"+{stacks * 10f:0.##} % de daño.",
        BuffKind.Protection => $"-{(1 - Math.Max(.2f, MathF.Pow(.92f, stacks))) * 100:0.##} % de daño recibido; límite del 80 %.",
        BuffKind.Regeneration => $"+{stacks * 20f:0.##} vida/s. Cura sin escudo, sin recibir ataques y fuera de las auras.",
        BuffKind.Vitality => $"+{stacks * 100f:0.##} de vida máxima.",
        BuffKind.Speed => $"+{Math.Min(100, stacks * 5f):0.##} % de velocidad; límite del 100 %.",
        BuffKind.Energy => $"+{stacks} energía/s base; se aplica el modificador de regeneración de energía del equipo.",
        _ => $"+{stacks * 10f:0.##} % a tus curaciones. No amplifica la regeneración de reliquias."
    };
    public static BuffSnapshot[] Snapshot(Wizard p)
    {
        int count = 0;
        foreach (int stacks in p.BuffStacks) if (stacks > 0) count++;
        if (count == 0) return [];
        var result = new BuffSnapshot[count];
        for (int i = 0, next = 0; i < Count; i++)
            if (p.BuffStacks[i] > 0) result[next++] = new((BuffKind)i, p.BuffStacks[i]);
        return result;
    }
    public static string Summary(BuffSnapshot[]? buffs) => buffs is { Length: > 0 }
        ? string.Join(" · ", buffs.Select(b => Name(b.Kind) + " ×" + b.Stacks)) : "";
    public static string DescribeCollected(BuffSnapshot[]? buffs, bool survival)
    {
        var active = (buffs ?? []).Where(b => b.Stacks > 0).OrderBy(b => b.Kind).ToArray();
        if (active.Length == 0) return "Todavía no has recogido reliquias.";
        string entries = string.Join("\n\n", active.Select((b, index) =>
            $"{index + 1}. {Name(b.Kind)} ×{b.Stacks}\n" + DescribeTotal(b.Kind, b.Stacks)));
        return entries + "\n\n" + (survival
            ? "En Supervivencia, las reliquias se conservan al morir y se pierden al iniciar otra partida."
            : "Las reliquias recogidas desaparecen al morir.");
    }
    internal static void Add(Wizard p, BuffKind kind)
    {
        int i = (int)kind;
        if (p.BuffStacks[i] == int.MaxValue) return;
        p.BuffStacks[i]++;
        if (kind == BuffKind.Vitality) { p.MaxHealth += 100; p.Health = Math.Min(p.MaxHealth, p.Health + 100); }
    }
    internal static void Clear(Wizard p)
    {
        p.MaxHealth = Math.Max(Wizard.BaseMaxHealth, p.MaxHealth - 100f * p.BuffStacks[(int)BuffKind.Vitality]);
        p.Health = Math.Min(p.Health, p.MaxHealth); Array.Clear(p.BuffStacks);
    }
}
