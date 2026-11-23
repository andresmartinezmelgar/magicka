using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class DefensePanel
{
    private void BuyPower(bool automatic, int slot)
    {
        var command = new ClientMessage { Type = automatic ? "defense_passive" : "defense_aura", Slot = slot };
        if (_powerBotId != 0)
        {
            if (!ShopOpen || FindBotInventory(_powerBotId) is not { } bot || !CanManageBotPowers(bot.Player) ||
                (automatic ? _passives : _auras)[slot].Disabled) return;
            command.Type = automatic ? "defense_bot_passive" : "defense_bot_aura";
            command.DefenseBotId = _powerBotId;
            command.DefenseUpgradeFromLevel = (automatic ? bot.Player.Passives : bot.Player.Auras)[slot];
        }
        _submit(command);
    }

    private bool CanManageBotPowers(DefensePlayerSnapshot bot) => !_ended && _wizard?.Alive == true && bot.LivesRemaining > 0;

    public void OpenShop() { if (!ShopOpen) ShowMenu(); _shop.Visible = true; OpenChanged?.Invoke(); }

    public void CloseShop()
    {
        if (_equipmentInventory != null) { _equipmentInventory.Close(FinishClosingShop); return; }
        FinishClosingShop();
    }

    private void FinishClosingShop() { _shop.Visible = false; OpenChanged?.Invoke(); }

    internal void HandleBack()
    {
        if (_equipmentInventory != null) _equipmentInventory.HandleBack();
        else if (_powerBotId != 0) ShowSection(BotEquipmentSection);
        else if (_sectionIndex >= 0) ShowMenu();
        else CloseShop();
    }

    private void OpenEquipmentInventory()
    {
        if (_player != null && _wizard != null) OpenEquipmentInventory(_player, _wizard);
    }

    private void OpenEquipmentInventory(DefensePlayerSnapshot player, WizardSnapshot wizard)
    {
        if (!ShopOpen || _equipmentInventory != null || _wizard == null) return;
        var inventory = new EquipmentPanel(); _equipmentInventory = inventory; _shop.AddChild(inventory);
        inventory.BuildDefense(_main, player, wizard, _ended, _submit, FinishClosingShop, _wizard.Alive);
        inventory.Closed += () =>
        {
            if (_equipmentInventory != inventory) return;
            _equipmentInventory = null;
            if (!wizard.Bot) ShowMenu();
        };
    }

    private (DefensePlayerSnapshot Player, WizardSnapshot Wizard)? FindBotInventory(int id)
    {
        if (_wizard is not { Bot: false } human ||
            _lastSnapshot?.Players.FirstOrDefault(w => w.Id == id && w.Bot && w.Alive && w.Team == human.Team) is not { } bot ||
            _state?.Defenders.FirstOrDefault(p => p.PlayerId == id && p.BotInventoryAvailable) is not { } player) return null;
        return (player, bot);
    }

    private void UpdateBotInventories()
    {
        var bots = _lastSnapshot?.Players.Where(w => w.Bot && FindBotInventory(w.Id).HasValue).ToArray() ?? [];
        string roster = string.Join('|', bots.Select(w => $"{w.Id}:{w.Name}"));
        if (_botInventoryRoster == roster) return;
        _botInventoryRoster = roster;
        foreach (var child in _botInventories.GetChildren())
        {
            if (child == _noBotInventories) continue;
            _botInventories.RemoveChild(child);
            child.QueueFree();
        }
        _noBotInventories.Visible = bots.Length == 0;
        foreach (var bot in bots)
        {
            var card = Card(_botInventories);
            Wrapped(card, bot.Name, 18, Ui.Gold);
            var actions = new HBoxContainer(); card.AddChild(actions);
            var inventory = Ui.ExitButton("Inventario", () =>
            {
                if (FindBotInventory(bot.Id) is { } entry) OpenEquipmentInventory(entry.Player, entry.Wizard);
            }, "DefenseBotInventory" + bot.Id);
            var powers = Ui.ExitButton("Auras y habilidades", () => ShowSection(PowersSection, bot.Id), "DefenseBotPowers" + bot.Id);
            foreach (var button in new[] { inventory, powers })
            {
                button.CustomMinimumSize = new(0, 54);
                button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                actions.AddChild(button);
            }
        }
    }

    private void Send(string type) => _submit(new() { Type = type });

    private void Resize()
    {
        var size = GetViewportRect().Size;
        float scale = Math.Min(1, size.Y / 720f);
        _summary.Scale = Vector2.One * scale;
        _summary.Size = new(296, 0);
        _summary.Position = new(22 * scale, 194 * scale + ScreenPlacement.MinimumGap);
        _window.CustomMinimumSize = new(Math.Min(790, size.X - 28), Math.Min(700, size.Y - 28));
        _navigation.Columns = size.X < 600 ? 1 : 2;
        _window.Size = _window.CustomMinimumSize;
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Resize;
}
