namespace Arcana.Core;

public enum WizardType { Balanced, Pyromancer, Cryomancer, Stormcaller, Geomancer }

/// <summary>La afinidad se elige al entrar. El servidor calcula cada componente elemental del daño.</summary>
public static class WizardTypes
{
    public const float Bonus = 1.25f;
    public static Element? Affinity(WizardType type) => type switch
    {
        WizardType.Pyromancer => Element.Fire, WizardType.Cryomancer => Element.Cold,
        WizardType.Stormcaller => Element.Lightning, WizardType.Geomancer => Element.Earth, _ => null
    };
    public static Element? Weakness(WizardType type) => type switch
    {
        WizardType.Pyromancer => Element.Cold, WizardType.Cryomancer => Element.Fire,
        WizardType.Stormcaller => Element.Earth, WizardType.Geomancer => Element.Lightning, _ => null
    };
    public static float Outgoing(WizardType type, Element element) => Affinity(type) == element ? Bonus : 1;
    public static float Incoming(WizardType type, Element element) => Weakness(type) == element ? Bonus : 1;
    public static string Name(WizardType type) => type switch
    {
        WizardType.Pyromancer => "Piromante", WizardType.Cryomancer => "Criomante",
        WizardType.Stormcaller => "Tempestario", WizardType.Geomancer => "Geomante", _ => "Equilibrado"
    };
    public static string Description(WizardType type) => type switch
    {
        WizardType.Pyromancer => "+25 % daño de fuego · recibe +25 % de frío",
        WizardType.Cryomancer => "+25 % daño de frío · recibe +25 % de fuego",
        WizardType.Stormcaller => "+25 % daño de rayo · recibe +25 % de tierra",
        WizardType.Geomancer => "+25 % daño de tierra · recibe +25 % de rayo",
        _ => "Daño normal · sin debilidad adicional"
    };
}
