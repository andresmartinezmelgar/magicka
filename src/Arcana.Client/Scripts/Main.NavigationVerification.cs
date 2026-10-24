using Arcana.Core;
using Godot;
using System.Reflection;

namespace Arcana.Client;

public partial class Main
{
    private async Task RunNavigationSmoke(string[] args)
    {
        async Task Frames(int count = 5)
        { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        Button Exit(Control root, string name)
        {
            var button = root.FindChild(name, true, false) as Button
                ?? throw new InvalidOperationException("Falta la salida " + name);
            Check(button.IsVisibleInTree() && !button.Disabled && button.Size.X >= 44 && button.Size.Y >= 44 &&
                GetViewport().GetVisibleRect().Grow(1).Encloses(button.GetGlobalRect()),
                "Salida inaccesible: " + name + " " + button.GetGlobalRect());
            for (Node? parent = button.GetParent(); parent != root; parent = parent?.GetParent())
            {
                Check(parent != null, "La salida no pertenece a la pantalla.");
                Check(parent is not ScrollContainer, "La salida depende de desplazar contenido: " + name);
            }
            return button;
        }
        async Task Click(Button button)
        {
            var point = button.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, GlobalPosition = point, Pressed = true });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, GlobalPosition = point, Pressed = false });
            await Frames();
        }
        void Back()
        {
            if (Ui.IsMobile) _Notification((int)NotificationWMGoBackRequest);
            else _UnhandledInput(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Escape });
        }
        string? captures = args.FirstOrDefault(a => a.StartsWith("--navigation-captures="))?["--navigation-captures=".Length..];
        void Capture(string name)
        {
            if (captures == null || DisplayServer.GetName() == "headless") return;
            Directory.CreateDirectory(captures);
            using var image = GetViewport().GetTexture().GetImage();
            Check(image.SavePng(Path.Combine(captures, name + ".png")) == Error.Ok, "No se pudo capturar " + name);
        }
        async Task Ready()
        {
            for (int i = 0; Loading && i < 500; i++) await Frames(1);
            Check(_playing && !Loading, "No terminó la carga del modo de prueba."); await Frames();
        }
        try
        {
            foreach (var size in new[] { new Vector2I(640, 450), new(960, 450), new(1440, 900) })
            {
                GetWindow().ContentScaleSize = size; GetWindow().Size = size; await Frames();
                Accounts.RestoreOfflineProfile(new("hg7F74kCNVQU3tU2itdjWu9SkyS2", "Navegación local", 0, 0, 0, new(), []));
                foreach (int page in new[] { 0, 1, 2, 3, 4, 5, 7 })
                {
                    NavigateOptionAMenu(page); await Frames();
                    Control screen = page switch { 3 => _accountProfilePanel!, 4 => _equipmentPanel!,
                        5 => _optionAHelp!, 7 => _itemShopPanel!, _ => _launcher };
                    foreach (var scroll in screen.FindChildren("*", "ScrollContainer", true, false).OfType<ScrollContainer>())
                        scroll.ScrollVertical = int.MaxValue;
                    await Frames(); await Click(Exit(screen, "OptionANav0"));
                    Check(_equipmentPanel == null && _accountProfilePanel == null && _optionAHelp == null &&
                        _itemShopPanel == null && _launcher.PageForVerification == 0, "La navegación Inicio queda bloqueada en " + page);
                }
                _launcher.OpenMobileSmokePage(2); Back(); await Frames();
                Check(_launcher.PageForVerification == 0, "Atrás no sale de la selección de modo.");
                OpenAccountProfile(); OpenMatchStatistics(); await Frames();
                Check(_statisticsPanel != null, "No se abrió la pantalla de estadísticas de prueba.");
                Back(); await Frames(); Check(_statisticsPanel == null && _accountProfilePanel != null,
                    "Atrás cierra Perfil por debajo de las estadísticas y deja la pantalla bloqueada.");
                OpenTouchSettings(); await Frames();
                ((ScrollContainer)_controlsSettings!.FindChild("TouchSettingsScroll", true, false)).ScrollVertical = int.MaxValue;
                await Frames(); await Click(Exit(_controlsSettings, "TouchSettingsCancel"));
                Check(!ControlsOpen && _accountProfilePanel != null, "Cancelar controles no vuelve a Perfil.");
                CloseAccountProfile(); OpenBestiary(); await Frames();
                await Click(Exit(_bestiaryPanel!, "OptionANav0")); Check(_bestiaryPanel == null, "No se puede salir del bestiario.");
                var loading = new LoadingScreen(); _ui.AddChild(loading); bool canceled = false;
                loading.Build("Mapa con un nombre largo para comprobar que no desplaza la cancelación", () => canceled = true);
                loading.UpdateProgress(75, new string('X', 300)); await Frames();
                await Click(Exit(loading, "LoadingCancel")); Check(canceled, "Cancelar carga no responde."); loading.QueueFree(); await Frames();

                var lobby = new RoomLobby(); _ui.AddChild(lobby);
                lobby.Build(this, "127.0.0.1:1", "", "Navegación", 0, WizardType.Balanced); await Frames();
                await Click(Exit(lobby, "RoomLobbyBack")); Check(!GodotObject.IsInstanceValid(lobby) || lobby.IsQueuedForDeletion(), "No se sale de las salas.");
                bool left = false; var waiting = new RoomWaitingPanel(); _ui.AddChild(waiting); waiting.Build(_ => { }, () => left = true);
                var room = new GameWorld(new() { Mode = GameMode.Duel }); var host = room.AddPlayer("Anfitrión", 0)!;
                room.AddPlayer("Rival", 1); waiting.Update(room.Snapshot() with { WaitingForPlayers = true, BestOf = 3, LobbyHostId = host.Id }, host.Id, true);
                await Frames(); await Click(Exit(waiting, "WaitingRoomExit")); Check(left, "Salir de la sala de espera no responde."); waiting.QueueFree();

                StartPractice("Navegación", 0, GameMode.Defense, 0, map: ArenaMapId.Bastion); await Ready();
                var player = _practice!.Players[_localId];
                var grant = typeof(GameWorld).GetMethod("GrantDefenseItem", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var boots = EquipmentCatalogue.Generate(new(961), tier: ItemTier.Silver, slot: EquipmentSlot.Boots);
                var otherBoots = EquipmentCatalogue.Generate(new(962), tier: ItemTier.Gold, slot: EquipmentSlot.Boots);
                grant.Invoke(_practice, [player, boots]); grant.Invoke(_practice, [player, otherBoots]);
                await Frames(); var shop = _defensePanel!;
                foreach (int section in new[] { 0, 2, 3, 4, 5 })
                {
                    shop.OpenShop(); shop.ShowSection(section); await Frames();
                    ((ScrollContainer)shop.FindChild("DefenseShopScroll", true, false)).ScrollVertical = int.MaxValue;
                    await Frames(); await Click(Exit(shop, "DefenseShopClose"));
                    Check(!shop.ShopOpen && !_practice.DefensePaused, "Una pestaña de Supervivencia no permite continuar: " + section);
                }
                shop.OpenShop(); shop.ShowSection(1); await Frames();
                var inventory = shop.EquipmentInventory!;
                Exit(inventory, "EquipmentResumeGame"); await Click(Exit(inventory, "EquipmentBackToShop"));
                Check(shop.EquipmentInventory == null && shop.ShopOpen && _practice.DefensePaused, "Volver a tienda no cierra solo el inventario.");
                shop.ShowSection(1); await Frames(); inventory = shop.EquipmentInventory!;
                inventory.PressInspect(EquipmentSlot.Boots); inventory.PressChoice(otherBoots.Id);
                ((Button)inventory.FindChild("EquipmentTotalsTab", true, false)).EmitSignal(Button.SignalName.Pressed);
                await Frames(); Capture(size.X + "-supervivencia-inventario");
                await Click(Exit(inventory, "EquipmentResumeGame"));
                Check(shop.EquipmentInventory == null && !shop.ShopOpen && !_practice.DefensePaused &&
                    _practice.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == _localId).Items.Single(i => i.Item.Id == otherBoots.Id).Equipped,
                    "Continuar no guarda la selección, no cierra ambas pantallas o no reanuda el combate.");

                shop.OpenShop(); shop.ShowSection(1); await Frames(); inventory = shop.EquipmentInventory!;
                inventory.PressInspect(EquipmentSlot.Boots); inventory.PressChoice(boots.Id);
                var menu = ((MenuButton)inventory.FindChild("EquipmentActions", true, false)).GetPopup();
                menu.EmitSignal(PopupMenu.SignalName.IdPressed, 4L); await Frames();
                var discard = (ConfirmationDialog)inventory.FindChild("DiscardEquipmentChanges", true, false);
                Check(discard.Visible, "Falta la salida para descartar una configuración sin guardar.");
                Back(); await Frames(); Check(!discard.Visible && shop.EquipmentInventory == inventory, "Atrás descarta cambios sin confirmar.");
                menu.EmitSignal(PopupMenu.SignalName.IdPressed, 4L); discard.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Frames();
                Check(shop.EquipmentInventory == null && _practice.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == _localId)
                    .Items.Single(i => i.Item.Id == otherBoots.Id).Equipped, "Descartar modifica la configuración confirmada."); shop.CloseShop();

                ToggleHelp(); await Frames(); _responsiveHelpScroll!.ScrollVertical = int.MaxValue; await Frames();
                Capture(size.X + "-ayuda"); await Click(Exit(_help, "InGameHelpClose")); Check(!_help.Visible, "La ayuda no cierra tras desplazarla.");
                TogglePause(); await Frames(); _responsivePauseScroll!.ScrollVertical = int.MaxValue; await Frames();
                Capture(size.X + "-pausa"); await Click(Exit(_pause, "PauseResume")); Check(!_paused && !_pause.Visible, "Continuar queda fuera del menú de pausa.");
                TogglePause(); OpenRelics(); await Frames(); Check(_relicWindow?.Visible == true, "No se abrió Reliquias.");
                Back(); await Frames(); Check(_relicWindow?.Visible != true && _paused, "Atrás de Reliquias no vuelve al menú de pausa.");
                Back(); await Frames(); Check(!_paused, "Atrás de la pausa no restaura el juego."); ReturnToLauncher(); await Frames();
                StartPractice("Navegación", 0, GameMode.Duel, 1); await Ready(); OpenPlayerEquipment(); await Frames();
                await Click(Exit(_playerEquipmentPanel!, "PlayerEquipmentBack")); Check(!InspectingEquipment, "La ficha del jugador no cierra.");
                ReturnToLauncher(); await Frames();
                GD.Print("NAVIGATION_VIEWPORT_OK: " + size + " · menús, estadísticas, controles, salas, carga, seis pestañas de Supervivencia, inventario, descarte, ayuda, pausa, reliquias y fichas.");
            }
            Check(_engineLog.ErrorCount == 0, "Godot registró errores durante la navegación.");
            GD.Print("NAVIGATION_SMOKE_OK: salidas visibles fuera del scroll, clic real, Escape/Atrás y guardado autoritativo antes de continuar.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("NAVIGATION_SMOKE_FAILED: " + error); GetTree().Quit(1); }
    }
}
