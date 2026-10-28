using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class DefensePanel
{
    private const int NoAffixPriority = 1000;
    private OptionButton _setupElement = null!;
    private readonly List<OptionButton> _setupPriorities = [];
    private Button _setupApply = null!, _setupDisable = null!;
    private ElementTextLabel _setupStatus = null!;
    private SurvivalEquipmentSetup? _setupDisplayed, _setupSubmitted;
    private bool _setupEditing, _setupSaveFailed;
    private string _setupError = "";
    private ulong _setupSubmittedAt;

    private void EditEquipmentSetup()
    { _setupEditing = true; _setupError = ""; }

    private void BuildEquipmentSetup(Control section)
    {
        Wrapped(section, "CONFIGURACIÓN PREDETERMINADA EN SUPERVIVENCIA", 19, Ui.Gold);
        Wrapped(section, "Elige el elemento que quieres potenciar. Se comparan las seis ranuras y los bonos de conjunto: Vida prioriza curación; Escudo, absorción; los demás, potencia y daño del elemento.", 16);
        Wrapped(section, "Primero intenta reunir los afijos Míticos indicados, por orden de prioridad y sin valorar dos veces el mismo efecto. Después maximiza el elemento. Si aún no tienes esos afijos, equipa la mejor combinación disponible.", 16);
        Wrapped(section, "AL ACTIVAR: equipa la combinación elegida y vende el resto por oro temporal, también las piezas sustituidas. Los favoritos se conservan. Mientras esté activa, sustituye los límites de venta por calidad y controla la selección de equipo. Se recuerda para futuras partidas y guardados.", 16, Ui.Gold);
        Wrapped(section, "Elemento principal", 17);
        _setupElement = new OptionButton { Name = "SurvivalSetupElement", CustomMinimumSize = new(0, 48), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var element in Enum.GetValues<Element>()) _setupElement.AddItem(Ui.ElementName(element), (int)element);
        section.AddChild(_setupElement); _setupElement.ItemSelected += _ => EditEquipmentSetup();
        Wrapped(section, "Afijos Míticos prioritarios · 1 es la prioridad más alta", 17);
        for (int i = 0; i < SurvivalEquipmentSetup.MaximumPriorities; i++)
        {
            Wrapped(section, $"Prioridad {i + 1}", 15);
            var choice = new OptionButton { Name = "SurvivalSetupPriority" + i, CustomMinimumSize = new(0, 48), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            choice.AddItem("Sin prioridad", NoAffixPriority);
            foreach (var kind in Enum.GetValues<ItemAffixKind>().Where(k => !ItemAffixes.IsGold(k)))
            {
                choice.AddItem(ItemAffixes.Name(kind), (int)kind);
                choice.SetItemTooltip(choice.ItemCount - 1, ItemAffixes.Describe(new(kind, kind == ItemAffixKind.BeamPierce ? 3 : kind is ItemAffixKind.DoubleBurn or ItemAffixKind.WaterShieldPool ? 1 : 2)));
            }
            choice.ItemSelected += _ => EditEquipmentSetup(); section.AddChild(choice); _setupPriorities.Add(choice);
        }
        _setupStatus = Wrapped(section, "", 16, Ui.Gold); _setupStatus.Name = "SurvivalSetupStatus";
        _setupApply = Ui.Button("Aplicar y activar · vende las piezas descartadas", () => SubmitEquipmentSetup(true));
        _setupApply.Name = "SurvivalSetupApply"; _setupApply.AutowrapMode = TextServer.AutowrapMode.WordSmart; section.AddChild(_setupApply);
        _setupDisable = Ui.Button("Desactivar selección y venta automáticas", () => SubmitEquipmentSetup(false));
        _setupDisable.Name = "SurvivalSetupDisable"; _setupDisable.AutowrapMode = TextServer.AutowrapMode.WordSmart; section.AddChild(_setupDisable);
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
        }
        _setupDisplayed = setup.Copy();
        if (confirmed) { _setupSubmitted = null; _setupError = ""; }
        if (_setupSubmitted == null)
            _setupStatus.Text = _setupError.Length > 0 ? _setupError : setup.Enabled ? "ACTIVA · " + Ui.ElementName(setup.Element) + $" · {_player.AutoSold} ventas automáticas. Los favoritos están protegidos."
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
