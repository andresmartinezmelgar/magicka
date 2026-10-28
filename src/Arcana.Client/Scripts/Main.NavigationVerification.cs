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
                OpenPersonalDamageSummary(); await Frames();
                var damageCard = (Control)_personalDamagePanel!.FindChild("PersonalDamageCard", true, false);
                Check(ViewingDamageSummary && _practice!.DefensePaused && GetViewport().GetVisibleRect().Encloses(damageCard.GetGlobalRect()),
                    "El resumen personal no pausa al humano vivo o queda fuera de la pantalla.");
                Check(((GridContainer)_personalDamagePanel.FindChild("PersonalDamageTable", true, false)).GetChildCount() == 40 &&
                    ((StatisticsBarChart)_personalDamagePanel.FindChild("PersonalElementDamageBars", true, false)).Categories.Length == 9,
                    "El resumen personal no incluye los ocho elementos y el daño sin elemento.");
                var damageBars = (StatisticsBarChart)_personalDamagePanel.FindChild("PersonalElementDamageBars", true, false);
                Check(damageBars.Percentage && !damageBars.DataScale.Logarithmic && damageBars.ScaleMaximum == 100 &&
                    ((ElementTextLabel)_personalDamagePanel.FindChild("PersonalDamageAbsorbed", true, false)).Text.Contains("tu escudo"),
                    "El resumen personal pierde la escala porcentual lineal o la absorción propia.");
                ((ScrollContainer)_personalDamagePanel.FindChild("PersonalDamageScroll", true, false)).ScrollVertical = int.MaxValue;
                await Frames(); Capture(size.X + "-dano-personal");
                await Click(Exit(_personalDamagePanel, "PersonalDamageClose"));
                Check(!ViewingDamageSummary && !_practice.DefensePaused, "Cerrar el resumen dejó pausada la partida.");
                var player = _practice!.Players[_localId];
                var grant = typeof(GameWorld).GetMethod("GrantDefenseItem", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var boots = EquipmentCatalogue.Generate(new(961), tier: ItemTier.Silver, slot: EquipmentSlot.Boots);
                var otherBoots = EquipmentCatalogue.Generate(new(962), tier: ItemTier.Gold, slot: EquipmentSlot.Boots);
                grant.Invoke(_practice, [player, boots]); grant.Invoke(_practice, [player, otherBoots]);
                await Frames(); var shop = _defensePanel!;
                foreach (int section in new[] { 0, 2, 3, 4, 5, 6 })
                {
                    shop.OpenShop(); shop.ShowSection(section); await Frames();
                    Check(!((Control)shop.FindChild("DefenseMenu", true, false)).IsVisibleInTree() &&
                        ((Control)shop.FindChild("DefenseSection" + section, true, false)).IsVisibleInTree(),
                        "Un apartado mezcla el menú con su contenido: " + section);
                    ((ScrollContainer)shop.FindChild("DefenseShopScroll", true, false)).ScrollVertical = int.MaxValue;
                    await Frames(); await Click(Exit(shop, "DefenseShopBack"));
                    Check(shop.ShopOpen && _practice.DefensePaused && ((Control)shop.FindChild("DefenseMenu", true, false)).IsVisibleInTree() &&
                        !((Control)shop.FindChild("DefenseSection" + section, true, false)).IsVisibleInTree(),
                        "Atrás no vuelve al menú o reanuda el combate: " + section);
                    await Frames(); await Click(Exit(shop, "DefenseShopClose"));
                    Check(!shop.ShopOpen && !_practice.DefensePaused, "Una pestaña de Supervivencia no permite continuar: " + section);
                }
                shop.OpenShop(); shop.ShowSection(1); await Frames();
                var inventory = shop.EquipmentInventory!;
                Exit(inventory, "EquipmentResumeGame"); await Click(Exit(inventory, "EquipmentBackToShop"));
                Check(shop.EquipmentInventory == null && shop.ShopOpen && _practice.DefensePaused &&
                    ((Control)shop.FindChild("DefenseMenu", true, false)).IsVisibleInTree(), "Atrás del inventario no devuelve al menú pausado.");
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

                var markBoots = EquipmentCatalogue.Generate(new(963), tier: ItemTier.Mythic, slot: EquipmentSlot.Boots)
                    with { Affix = new(ItemAffixKind.ArcaneMark, 2) };
                var echoHat = EquipmentCatalogue.Generate(new(964), tier: ItemTier.Mythic, slot: EquipmentSlot.Hat)
                    with { Affix = new(ItemAffixKind.ArcaneEcho, 2) };
                grant.Invoke(_practice, [player, markBoots]); grant.Invoke(_practice, [player, echoHat]); await Frames();
                shop.OpenShop(); shop.ShowSection(6); await Frames();
                var setupScroll = (ScrollContainer)shop.FindChild("DefenseShopScroll", true, false);
                Check(setupScroll.Size.Y > 70, "Las pestañas dejan sin espacio la configuración predeterminada.");
                void SelectSetup(string name, int id)
                {
                    var option = (OptionButton)shop.FindChild(name, true, false);
                    int index = option.GetItemIndex(id); option.Select(index); option.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
                }
                SelectSetup("SurvivalSetupElement", (int)Element.Arcane);
                SelectSetup("SurvivalSetupPriority0", (int)ItemAffixKind.ArcaneMark);
                var priority = (OptionButton)shop.FindChild("SurvivalSetupPriority0", true, false);
                var elementChoice = (OptionButton)shop.FindChild("SurvivalSetupElement", true, false);
                int mark = priority.GetItemIndex((int)ItemAffixKind.ArcaneMark), chain = priority.GetItemIndex((int)ItemAffixKind.LightningChain);
                Check(priority.GetItemIcon(mark) != null && priority.GetItemIcon(chain) == null, "No resalta los afijos de Arcano.");
                SelectSetup("SurvivalSetupElement", (int)Element.Lightning); await Frames();
                Check(priority.GetItemIcon(mark) == null && priority.GetItemIcon(chain) != null &&
                    priority.GetSelectedId() == (int)ItemAffixKind.ArcaneMark && elementChoice.GetSelectedId() == (int)Element.Lightning &&
                    !_practice.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == _localId).EquipmentSetup.Enabled,
                    "Cambiar el elemento no actualiza el resaltado, pierde la prioridad o activa ventas sin aplicar.");
                SelectSetup("SurvivalSetupElement", (int)Element.Arcane);
                SelectSetup("SurvivalSetupPriority1", (int)ItemAffixKind.ArcaneMark);
                setupScroll.ScrollVertical = int.MaxValue; await Frames();
                var setupApply = (Button)shop.FindChild("SurvivalSetupApply", true, false);
                await Click(setupApply);
                Check(((ElementTextLabel)shop.FindChild("SurvivalSetupStatus", true, false)).Text.Contains("una sola vez") &&
                    !_practice.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == _localId).EquipmentSetup.Enabled,
                    "La configuración admite prioridades duplicadas o borra el error antes de poder leerlo.");
                SelectSetup("SurvivalSetupPriority1", (int)ItemAffixKind.ArcaneEcho);
                setupScroll.ScrollVertical = int.MaxValue; await Frames(); await Click(setupApply);
                var selectedSetup = _practice.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == _localId);
                Check(selectedSetup.EquipmentSetup.Enabled && selectedSetup.Modifiers.ArcaneMark == 2 && selectedSetup.Modifiers.ArcaneEcho == 2 &&
                    selectedSetup.Items.Length == 2 && selectedSetup.AutoSold == 2, "La nueva pestaña no equipa Marca y Eco o no vende los descartes.");
                shop.ShowSection(1); await Frames(); inventory = shop.EquipmentInventory!; inventory.PressInspect(EquipmentSlot.Boots); await Frames();
                Check(((Button)inventory.FindChild("EquipmentAutoSell_" + markBoots.Id, true, false)).Disabled,
                    "El filtro por calidad compite con la configuración automática.");
                var manualBoots = EquipmentCatalogue.Generate(new(965), tier: ItemTier.Mythic, slot: EquipmentSlot.Boots)
                    with { Affix = new(ItemAffixKind.DoubleBurn) };
                grant.Invoke(_practice, [player, manualBoots]); await Frames();
                Check(!((Button)inventory.FindChild("EquipmentEquip_" + manualBoots.Id, true, false)).Disabled,
                    "La selección automática bloquea el botón de equipar.");
                inventory.PressChoice(manualBoots.Id); await Frames();
                var manualSetup = _practice.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == _localId);
                Check(manualSetup.EquipmentSetup.Enabled && manualSetup.ManualEquipmentSlots.Contains(EquipmentSlot.Boots) &&
                    manualSetup.Items.Single(i => i.Item.Id == manualBoots.Id).Equipped && manualSetup.Items.Single(i => i.Item.Id == echoHat.Id).Equipped,
                    "Equipar manualmente desactiva el automático, no conserva la elección o cambia otras ranuras.");
                await Click(Exit(inventory, "EquipmentBackToShop")); shop.ShowSection(6); await Frames();
                setupScroll.ScrollVertical = int.MaxValue; await Frames();
                await Click(setupApply);
                var resetSetup = _practice.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == _localId);
                Check(resetSetup.ManualEquipmentSlots.Length == 0 && resetSetup.Items.Single(i => i.Item.Id == markBoots.Id).Equipped,
                    "Aplicar no restablece la selección automática de una ranura manual.");
                setupScroll.ScrollVertical = int.MaxValue; await Frames();
                await Click((Button)shop.FindChild("SurvivalSetupDisable", true, false));
                Check(!_practice.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == _localId).EquipmentSetup.Enabled &&
                    !SurvivalPreferences.SurvivalEquipment.Enabled, "Desactivar no se aplica o no se recuerda.");
                shop.CloseShop();

                ToggleHelp(); await Frames(); _responsiveHelpScroll!.ScrollVertical = int.MaxValue; await Frames();
                Capture(size.X + "-ayuda"); await Click(Exit(_help, "InGameHelpClose")); Check(!_help.Visible, "La ayuda no cierra tras desplazarla.");
                TogglePause(); await Frames(); _responsivePauseScroll!.ScrollVertical = int.MaxValue; await Frames();
                Capture(size.X + "-pausa"); await Click(Exit(_pause, "PauseResume")); Check(!_paused && !_pause.Visible, "Continuar queda fuera del menú de pausa.");
                TogglePause(); OpenRelics(); await Frames(); Check(_relicWindow?.Visible == true, "No se abrió Reliquias.");
                Back(); await Frames(); Check(_relicWindow?.Visible != true && _paused, "Atrás de Reliquias no vuelve al menú de pausa.");
                Back(); await Frames(); Check(!_paused, "Atrás de la pausa no restaura el juego.");
                player.Health = 0; player.Deaths = 1; player.Respawn = 4.5f; player.Invulnerable = 0; await Frames();
                Check(ViewingDamageSummary && !_practice!.DefensePaused && player.Respawn < 4.5f,
                    "El resumen de muerte no se abre o impide avanzar la reaparición.");
                typeof(GameWorld).GetMethod("Respawn", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(_practice, [player]); await Frames();
                Check(!ViewingDamageSummary, "El resumen automático no se cierra al reaparecer.");
                player.Health = 0; player.Deaths = DefenseRules.PlayerLives; player.Invulnerable = 0;
                typeof(GameWorld).GetMethod("CheckSurvivalEnd", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(_practice, []); await Frames();
                Check(ViewingDamageSummary && !_defensePanel!.ShopOpen, "La tienda tapa el resumen final de Supervivencia.");
                Back(); await Frames(); Check(!ViewingDamageSummary, "Atrás no cierra el resumen final.");
                var finalSnapshot = _practice.Snapshot();
                _hud!.ScoreOpen = true; _hud.Update(finalSnapshot, _localId, "Local", 0);
                Check(((Control)_hud.FindChild("ScorePanel", true, false)).Visible && !_hud.RoundAnnouncementVisibleForSmoke &&
                    _hud.FindChild("ScoreCenter", true, false).GetIndex() > _hud.FindChild("HudCenteredNotices", true, false).GetIndex(),
                    "El aviso de fin de Supervivencia tapa la clasificación.");
                _hud.ScoreOpen = false; _hud.Update(finalSnapshot, _localId, "Local", 0);
                Check(_hud.RoundAnnouncementVisibleForSmoke, "Cerrar la tabla con el mismo estado no recupera el aviso final.");
                ReturnToLauncher(); await Frames();
                StartPractice("Navegación", 0, GameMode.Duel, 1); await Ready(); OpenPlayerEquipment(); await Frames();
                await Click(Exit(_playerEquipmentPanel!, "PlayerEquipmentBack")); Check(!InspectingEquipment, "La ficha del jugador no cierra.");
                ReturnToLauncher(); await Frames();
                GD.Print("NAVIGATION_VIEWPORT_OK: " + size + " · menús, estadísticas, daño porcentual y absorción, controles, salas, carga, siete apartados con Atrás, afijos resaltados por elemento, equipo predeterminado con Marca/Eco y venta, inventario, descarte, ayuda, pausa, reliquias y fichas.");
            }
            Check(_engineLog.ErrorCount == 0, "Godot registró errores durante la navegación.");
            GD.Print("NAVIGATION_SMOKE_OK: salidas visibles fuera del scroll, clic real, Escape/Atrás y guardado autoritativo antes de continuar.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("NAVIGATION_SMOKE_FAILED: " + error); GetTree().Quit(1); }
    }
}
