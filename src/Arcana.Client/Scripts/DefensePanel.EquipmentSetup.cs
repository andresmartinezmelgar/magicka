using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class DefensePanel
{
    private const int NoAffixPriority = 1000;
    private OptionButton _setupElement = null!;
    private readonly List<OptionButton> _setupPriorities = [];
    private readonly List<ElementTextLabel> _setupPriorityDescriptions = [];
    private Button _setupApply = null!, _setupDisable = null!;
    private ElementTextLabel _setupStatus = null!, _setupAffixHint = null!;
    private SurvivalEquipmentSetup? _setupDisplayed, _setupSubmitted;
    private bool _setupEditing, _setupSaveFailed;
    private string _setupError = "";
    private ulong _setupSubmittedAt;

    private void EditEquipmentSetup()
    { _setupEditing = true; _setupError = ""; RefreshSetupAffixHighlights(); }

    private void BuildEquipmentSetup(Control section)
    {
        Wrapped(section, "CONFIGURACIÓN PREDETERMINADA EN SUPERVIVENCIA", 19, Ui.Gold);
        Wrapped(section, "Elige el elemento que quieres potenciar. Se comparan las seis ranuras y los bonos de conjunto: Vida prioriza curación; Escudo, absorción; los demás, potencia y daño del elemento.", 16);
        Wrapped(section, "Primero intenta reunir los afijos Míticos indicados, por orden de prioridad y sin valorar dos veces el mismo efecto. Después maximiza el elemento. Si aún no tienes esos afijos, equipa la mejor combinación disponible.", 16);
        Wrapped(section, "AL ACTIVAR: equipa la combinación elegida y vende los descartes por oro temporal. Nunca vende Míticos ni favoritos, aunque no coincidan con tu selección. Mientras esté activa, sustituye los límites de venta por calidad y controla el equipo. Se recuerda para futuras partidas y guardados.", 16, Ui.Gold);
        Wrapped(section, "Elemento principal", 17);
        _setupElement = new OptionButton { Name = "SurvivalSetupElement", CustomMinimumSize = new(0, 48), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var element in Enum.GetValues<Element>()) _setupElement.AddItem(Ui.ElementName(element), (int)element);
        section.AddChild(_setupElement); _setupElement.ItemSelected += _ => EditEquipmentSetup();
        Wrapped(section, "Afijos Míticos prioritarios · 1 es la prioridad más alta", 17);
        _setupAffixHint = Wrapped(section, "", 15, Ui.Muted);
        _setupAffixHint.Name = "SurvivalSetupAffixHint";
        for (int i = 0; i < SurvivalEquipmentSetup.MaximumPriorities; i++)
        {
            Wrapped(section, $"Prioridad {i + 1}", 15);
            var choice = new OptionButton { Name = "SurvivalSetupPriority" + i, CustomMinimumSize = new(0, 48), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            choice.AddThemeConstantOverride("icon_max_width", 24);
            choice.AddItem("Sin prioridad", NoAffixPriority);
            foreach (var kind in Enum.GetValues<ItemAffixKind>())
            {
                choice.AddItem(ItemAffixes.Name(kind), (int)kind);
                choice.SetItemTooltip(choice.ItemCount - 1, SetupAffixDescription(kind));
            }
            choice.ItemSelected += _ => EditEquipmentSetup(); section.AddChild(choice); _setupPriorities.Add(choice);
            var description = Wrapped(section, "", 15, Ui.Muted);
            description.Name = "SurvivalSetupPriorityDescription" + i;
            description.Visible = false; _setupPriorityDescriptions.Add(description);
        }
        _setupStatus = Wrapped(section, "", 16, Ui.Gold); _setupStatus.Name = "SurvivalSetupStatus";
        _setupApply = Ui.Button("Aplicar y activar · vende las piezas descartadas", () => SubmitEquipmentSetup(true));
        _setupApply.Name = "SurvivalSetupApply"; _setupApply.AutowrapMode = TextServer.AutowrapMode.WordSmart; section.AddChild(_setupApply);
        _setupDisable = Ui.Button("Desactivar selección y venta automáticas", () => SubmitEquipmentSetup(false));
        _setupDisable.Name = "SurvivalSetupDisable"; _setupDisable.AutowrapMode = TextServer.AutowrapMode.WordSmart; section.AddChild(_setupDisable);
        RefreshSetupAffixHighlights();
    }
    private static string SetupAffixDescription(ItemAffixKind kind)
    {
        int value = kind switch { ItemAffixKind.GoldBonus => 20, ItemAffixKind.BeamPierce => 3,
            ItemAffixKind.DoubleBurn or ItemAffixKind.WaterShieldPool or ItemAffixKind.CoinDividend => 1, _ => 2 };
        return ItemAffixes.Describe(new(kind, value));
    }
    private static string SetupAffixRelevance(ItemAffixKind kind, Element element) => (kind, element) switch
    {
        (ItemAffixKind.BeamPierce or ItemAffixKind.ArcaneExecution or ItemAffixKind.ArcaneMark or ItemAffixKind.ArcaneEcho, Element.Arcane)
            => "Potencia tus hechizos de Arcano.",
        (ItemAffixKind.LightningChain, Element.Lightning) => "Añade saltos a los relámpagos de Rayo.",
        (ItemAffixKind.DoubleBurn or ItemAffixKind.SteamImpact, Element.Fire) => "Potencia el daño de Fuego; consulta sus condiciones.",
        (ItemAffixKind.WaterShieldPool, Element.Water or Element.Shield) => "Mejora la combinación de Agua y Escudo.",
        (ItemAffixKind.MendingWard, Element.Life or Element.Shield) => "Tus curaciones directas reparan un escudo ya activo.",
        (ItemAffixKind.ShieldBattery or ItemAffixKind.ShieldReprisal or ItemAffixKind.ShieldLeech or ItemAffixKind.ShieldReflection, Element.Shield)
            => "Añade un efecto al escudo personal.",
        (ItemAffixKind.Icebreaker, Element.Earth) => "Potencia el impacto de Tierra contra enemigos congelados.",
        (ItemAffixKind.RockFragmentation, Element.Earth) => "Añade fragmentos a tus rocas.",
        (ItemAffixKind.RockFragmentation, Element.Water or Element.Cold or Element.Fire)
            => "Requiere una mezcla con Tierra: los fragmentos conservan este elemento y dejan su área al impactar.",
        (ItemAffixKind.RockFragmentation, Element.Arcane or Element.Life)
            => "Requiere una mezcla con Tierra: los fragmentos conservan el impacto de este elemento.",
        _ => ""
    };
    private void RefreshSetupAffixHighlights()
    {
        var element = (Element)_setupElement.GetSelectedId();
        var color = Ui.ElementColor(element);
        var icon = ArcanaSpriteAtlas.Icon(element);
        _setupElement.AddThemeColorOverride("font_color", color);
        _setupAffixHint.Text = $"El icono de {Ui.ElementName(element)} resalta los afijos que lo potencian. Su descripción indica si necesitan una mezcla.";
        for (int priority = 0; priority < _setupPriorities.Count; priority++)
        {
            var choice = _setupPriorities[priority];
            for (int index = 1; index < choice.ItemCount; index++)
            {
                var kind = (ItemAffixKind)choice.GetItemId(index);
                string reason = SetupAffixRelevance(kind, element);
                choice.SetItemIcon(index, reason.Length > 0 ? icon : null);
                choice.GetPopup().SetItemIconMaxWidth(index, 24);
                choice.SetItemTooltip(index, (reason.Length > 0 ? reason + "\n\n" : "") + SetupAffixDescription(kind));
            }
            int selected = choice.GetSelectedId();
            var description = _setupPriorityDescriptions[priority];
            description.Visible = selected != NoAffixPriority;
            description.Text = selected == NoAffixPriority ? "" : SetupAffixDescription((ItemAffixKind)selected);
            if ((ItemAffixKind)selected is ItemAffixKind.BeamPierce or ItemAffixKind.LightningChain)
                description.Text = "Valor máximo en Mítico · " + description.Text;
            var tint = selected != NoAffixPriority && SetupAffixRelevance((ItemAffixKind)selected, element).Length > 0 ? color : Ui.Text;
            foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
                choice.AddThemeColorOverride(state, tint);
        }
    }
    private void UpdateEquipmentSetup()
    {
        if (_player == null) return;
        var setup = _player.EquipmentSetup;
        bool changed = _setupDisplayed?.SameAs(setup) != true;
        bool confirmed = _setupSubmitted?.SameAs(setup) == true;
        if (!confirmed && _setupSubmitted != null && Time.GetTicksMsec() - _setupSubmittedAt > 6000)
        { _setupSubmitted = null; _setupEditing = true; _setupError = "El servidor no ha confirmado el cambio. Puedes volver a aplicarlo."; }
        if (changed && (!_setupEditing || confirmed))
        {
            _setupElement.Select(_setupElement.GetItemIndex((int)setup.Element));
            for (int i = 0; i < _setupPriorities.Count; i++)
                _setupPriorities[i].Select(_setupPriorities[i].GetItemIndex(i < setup.Priorities.Length ? (int)setup.Priorities[i] : NoAffixPriority));
            _setupEditing = false;
            RefreshSetupAffixHighlights();
        }
        _setupDisplayed = setup.Copy();
        if (confirmed) { _setupSubmitted = null; _setupError = ""; }
        if (_setupSubmitted == null)
            _setupStatus.Text = _setupError.Length > 0 ? _setupError : setup.Enabled ? "ACTIVA · " + Ui.ElementName(setup.Element) + $" · {_player.AutoSold} ventas automáticas. Míticos y favoritos protegidos."
                : "DESACTIVADA · Puedes elegir y conservar equipo manualmente.";
        if (_setupSubmitted == null && _setupSaveFailed) _setupStatus.Text += "\nNo se pudo guardar la preferencia en el dispositivo; se conserva en esta partida.";
        _setupElement.AddThemeColorOverride("font_color", Ui.ElementColor((Element)_setupElement.GetSelectedId()));
        _setupElement.Disabled = _ended || _setupSubmitted != null;
        foreach (var priority in _setupPriorities) priority.Disabled = _ended || _setupSubmitted != null;
        _setupApply.Disabled = _ended || _setupSubmitted != null;
        _setupDisable.Disabled = _ended || !setup.Enabled || _setupSubmitted != null;
    }
    private void SubmitEquipmentSetup(bool enabled)
    {
        if (_player == null || _ended || _setupSubmitted != null) return;
        var settings = enabled ? new SurvivalEquipmentSetup { Enabled = true, Element = (Element)_setupElement.GetSelectedId(),
            Priorities = _setupPriorities.Select(p => p.GetSelectedId()).Where(id => id != NoAffixPriority).Select(id => (ItemAffixKind)id).ToArray() }
            : _player.EquipmentSetup with { Enabled = false };
        if (!settings.Valid) { _setupError = "Elige cada afijo una sola vez; las prioridades no pueden repetirse."; _setupStatus.Text = _setupError; return; }
        bool saved = _main.RememberSurvivalEquipmentSetup(settings);
        _setupSaveFailed = !saved;
        _setupSubmitted = settings.Copy(); _setupSubmittedAt = Time.GetTicksMsec(); _setupEditing = false; _setupError = "";
        _setupStatus.Text = saved ? "Aplicando configuración…" : "Aplicando en esta partida. No se pudo guardar la preferencia en el dispositivo.";
        _submit(new() { Type = "defense_equipment_setup", SurvivalEquipmentSetup = settings });
    }
}
