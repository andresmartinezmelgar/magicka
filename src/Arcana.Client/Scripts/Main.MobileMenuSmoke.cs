using Godot;
using Arcana.Core;

namespace Arcana.Client;

/// <summary>Prueba de geometría real del menú móvil. No toca servidores ni servicios externos.</summary>
public partial class Main
{
    private bool _mobileMenuSmoke;
    private bool _desktopHeaderSmoke;
    private int _desktopHeaderSmokeFrame;
    private int _mobileMenuSmokeFrame;
    private Hud? _smokeRivalHud;
    private bool _visualReview;
    private long _lastVisualDraw = -1;
    private string? _visualReviewDirectory;

    private void CaptureVisualReview(string stage)
    {
        if (!_visualReview || DisplayServer.GetName() == "headless") return;
        var screenshot = GetViewport().GetTexture().GetImage();
        if (screenshot.IsEmpty()) throw new InvalidOperationException("Captura gráfica vacía: " + stage);
        if (screenshot.GetWidth() < 640 || screenshot.GetHeight() < 450)
            throw new InvalidOperationException("Captura gráfica incompleta: " + stage);
        // Rechaza fotogramas antiguos del combate usados por error como capturas del menú.
        // En los menús predomina el azul oscuro; el mapa del juego es mayoritariamente verde.
        if (stage.StartsWith("03-", StringComparison.Ordinal) ||
            stage.StartsWith("04-", StringComparison.Ordinal) ||
            stage.StartsWith("05-", StringComparison.Ordinal))
        {
            int green = 0, samples = 0;
            for (int row = 2; row < 18; row++)
                for (int col = 2; col < 18; col++)
                {
                    var pixel = screenshot.GetPixel(col * screenshot.GetWidth() / 20,
                        row * screenshot.GetHeight() / 20);
                    if (pixel.G > pixel.R * 1.3f && pixel.G > pixel.B * 1.3f && pixel.G > 0.12f) green++;
                    samples++;
                }
            if (green > samples / 5)
                throw new InvalidOperationException($"VISUAL_STALE_FRAME: {stage} parece mostrar el combate en vez del menú ({green}/{samples} píxeles verdes).");
        }
        if (!string.IsNullOrWhiteSpace(_visualReviewDirectory))
        {
            DirAccess.MakeDirRecursiveAbsolute(_visualReviewDirectory);
            string file = System.IO.Path.Combine(_visualReviewDirectory, stage + ".png");
            if (screenshot.SavePng(file) != Error.Ok) throw new IOException("No se pudo guardar " + file);
            GD.Print($"VISUAL_CAPTURE_OK:{stage}:{screenshot.GetWidth()}x{screenshot.GetHeight()}:{file}");
        }
        screenshot.Resize(800, 450, Image.Interpolation.Lanczos);
        GD.Print("VISUAL_REVIEW_BASE64:" + stage + ":" + Convert.ToBase64String(screenshot.SaveJpgToBuffer(0.68f)));
    }

    private void AssertInventorySplit(EquipmentPanel panel)
    {
        var left = (Control)panel.FindChild("EquipmentAvatarPane", true, false);
        var right = (Control)panel.FindChild("EquipmentItemsPane", true, false);
        var screen = GetViewport().GetVisibleRect().Grow(2);
        if (!screen.Encloses(left.GetGlobalRect()) || !screen.Encloses(right.GetGlobalRect()) || left.GetGlobalRect().End.X > right.GetGlobalRect().Position.X + 1)
            throw new InvalidOperationException("El mago y los objetos no forman dos paneles visibles sin superposición: " + left.GetGlobalRect() + " / " + right.GetGlobalRect());
        var zones = panel.FindChildren("EquipmentSlot*", "Button", true, false).OfType<Button>().ToArray();
        if (zones.Length != 6 || zones.Any(b => !left.GetGlobalRect().Grow(1).Encloses(b.GetGlobalRect()) || b.Size.X < 44 || b.Size.Y < 44))
            throw new InvalidOperationException("Falta una de las seis zonas del mago o queda fuera de su panel.");
        for (int i = 0; i < zones.Length; i++) for (int j = i + 1; j < zones.Length; j++)
            if (zones[i].GetGlobalRect().Intersects(zones[j].GetGlobalRect())) throw new InvalidOperationException("Dos zonas de equipo se superponen.");
        if (panel.FindChild("EquipmentFilters", true, false) != null || panel.FindChild("EquipmentSort", true, false) != null)
            throw new InvalidOperationException("El inventario conserva filtros.");
        var stats = (Button)panel.FindChild("EquipmentTotalsTab", true, false);
        if (!right.GetGlobalRect().Encloses(stats.GetGlobalRect())) throw new InvalidOperationException("El botón de estadísticas no está a la derecha.");
        var table = (GridContainer)panel.FindChild("EquipmentTotalsTable", true, false);
        var numbers = table.GetChildren().OfType<ElementTextLabel>().Where(l => l.Name.ToString().StartsWith("EquipmentTotalApplied_"))
            .Select(l => float.Parse(l.Text.Split('\n')[0].TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (!numbers.SequenceEqual(numbers.OrderByDescending(n => n))) throw new InvalidOperationException("Las estadísticas no están ordenadas de mayor a menor.");
        GD.Print("INVENTORY_SPLIT_OK: seis zonas accesibles, mago a la izquierda, objetos y estadísticas a la derecha; filtros ausentes y totales descendentes.");
    }

    private void AssertHudBounds(Hud hud, string stage)
    {
        Rect2 screen = GetViewport().GetVisibleRect();
        foreach (var name in new[] { "MatchBanner", "MatchRewardNotice", "PlayerStatusPanel", "SpellControlsPanel", "MiniMapPanel", "MatchObjective" })
        {
            if (hud.FindChild(name, true, false) is not Control control || !control.IsVisibleInTree()) continue;
            var rect = control.GetGlobalRect();
            if (!screen.Encloses(rect))
                throw new InvalidOperationException($"HUD {stage}: {name} fuera de pantalla: {rect}, viewport={screen}");
        }
        var status = (Control)hud.FindChild("PlayerStatusPanel", true, false);
        var minimap = (Control)hud.FindChild("MiniMapPanel", true, false);
        if (status.IsVisibleInTree() && minimap.IsVisibleInTree() &&
            status.GetGlobalRect().Intersects(minimap.GetGlobalRect()))
            throw new InvalidOperationException($"HUD {stage}: el minimapa tapa la vida del jugador: {minimap.GetGlobalRect()} / {status.GetGlobalRect()}");
    }

    private void AssertVisibleTableData(Control root, string tableName, bool includeHeader = true)
    {
        var table = root.FindChild(tableName, true, false) as GridContainer
            ?? throw new InvalidOperationException("Falta la tabla " + tableName);
        var cells = table.GetChildren().OfType<ElementTextLabel>().Where(label => label.Visible).ToArray();
        Rect2 visible = GetViewport().GetVisibleRect();
        for (Node? parent = table.GetParent(); parent != null; parent = parent.GetParent())
            if (parent is ScrollContainer scroll) visible = visible.Intersection(scroll.GetGlobalRect());
        int[] required = includeHeader ? [0, table.Columns - 1, table.Columns, table.Columns * 2 - 1]
            : [table.Columns, table.Columns * 2 - 1];
        if (cells.Length < table.Columns * 2 || required
            .Any(index => !visible.Grow(1).Encloses(cells[index].GetGlobalRect())))
            throw new InvalidOperationException(tableName + ": no se ven la cabecera y una fila completa de datos. Área visible: " + visible + "; celdas: " + string.Join("; ", cells.Take(table.Columns * 2).Select(cell => cell.Text + " " + cell.GetGlobalRect())));
    }

    // En CI no basta con simular los controles móviles: hay que comprobar
    // la geometría Godot del escritorio real en 1600×900 y 1280×720.
    private void PrepareDesktopHeaderSmokeWindow()
    {
        var args = OS.GetCmdlineUserArgs();
        if (!(args.Contains("--desktop-header-smoke") || args.Contains("--visual-desktop")) || Ui.IsMobile) return;
        string? size = args.FirstOrDefault(arg => arg.StartsWith("--smoke-viewport=", StringComparison.Ordinal));
        if (size == null) return;
        var parts = size["--smoke-viewport=".Length..].Split('x');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int width) ||
            !int.TryParse(parts[1], out int height) ||
            width is < 800 or > 2560 || height is < 600 or > 1440)
            throw new ArgumentException("Resolución del smoke de escritorio inválida.");
        GetWindow().ContentScaleSize = new Vector2I(width, height);
        GetWindow().ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
        GetWindow().Size = new Vector2I(width, height);
    }

    private static void AssertHeaderAndHome(Launcher launcher, Rect2 screen, string mode)
    {
        if (launcher.FindChild("OptionAHeader", true, false) is not Control header ||
            launcher.FindChild("OptionAAccountSummary", true, false) is not ElementTextLabel summary ||
            launcher.FindChild("OptionAHomeCards", true, false) is not GridContainer home)
            throw new InvalidOperationException($"{mode}: faltan cabecera, resumen o tarjetas.");
        Rect2 head = header.GetGlobalRect();
        Rect2 account = summary.GetGlobalRect();
        Rect2 cards = home.GetGlobalRect();
        var buttons = home.GetChildren().OfType<Button>().ToArray();
        if (head.Size.Y is < 55 or > 80 || head.Position.Y < -1 || !screen.Encloses(head))
            throw new InvalidOperationException($"{mode}: la cabecera invade la pantalla: {head}");
        if (!summary.IsVisibleInTree() || summary.AutowrapMode != TextServer.AutowrapMode.Off ||
            account.Size.X < 140 || account.Size.Y > 56 || account.Position.Y < head.Position.Y ||
            account.End.Y > head.End.Y || account.End.X > screen.End.X)
            throw new InvalidOperationException($"{mode}: perfil inválido: rect={account}, header={head}, visible={summary.IsVisibleInTree()}, wrap={summary.AutowrapMode}, screen={screen}");
        if (home.Columns != 2 || buttons.Length != 4 ||
            cards.Position.Y < head.End.Y || cards.Position.X < 0 ||
            cards.End.X > screen.End.X || cards.Position.Y > screen.End.Y - 80)
            throw new InvalidOperationException($"{mode}: tarjetas 2×2 fuera del área central: {cards}, header={head}");
        foreach (var button in buttons)
        {
            var rect = button.GetGlobalRect();
            if (!button.IsVisibleInTree() || rect.Size.X < 120 || rect.Size.Y < 90 ||
                rect.Position.Y < head.End.Y || rect.Position.X < 0 || rect.End.X > screen.End.X ||
                rect.Position.Y > screen.End.Y - 65)
                throw new InvalidOperationException($"{mode}: tarjeta del inicio fuera de pantalla: {button.Text}, {rect}");
        }
        GD.Print($"HEADER_AND_HOME_OK:{mode}: header={head}, summary={account}, cards={cards}");
    }

    private bool CheckDesktopHeaderSmoke()
    {
        if (!_desktopHeaderSmoke) return false;
        int frame = ++_desktopHeaderSmokeFrame;
        try
        {
            if (frame == 2)
            {
                _googleIdentityKnown = true;
                _googleIdentityName = "andresmartinez";
                _launcher.UpdateAccountStatus();
            }
            if (frame == 20)
            {
                AssertHeaderAndHome(_launcher, GetViewport().GetVisibleRect(), "DESKTOP");
                GD.Print("DESKTOP_HEADER_LAYOUT_OK");
                _desktopHeaderSmoke = false;
                GetTree().Quit(0);
            }
            return true;
        }
        catch (Exception error)
        {
            GD.PushError("DESKTOP_HEADER_LAYOUT_FAILED: " + error);
            _desktopHeaderSmoke = false;
            GetTree().Quit(1);
            return true;
        }
    }
    private RoomLobby? _smokeLobby;
    private Hud? _smokeScoreHud;

    private void AssertMobileActions(string stage, IEnumerable<Button> buttons, int minimumCount)
    {
        Rect2 screen = GetViewport().GetVisibleRect();
        Button[] visible = buttons.ToArray();
        if (visible.Length < minimumCount)
            throw new InvalidOperationException($"{stage}: faltan botones ({visible.Length}/{minimumCount}).");
        foreach (var button in visible)
        {
            var bounds = button.GetGlobalRect();
            if (!button.IsVisibleInTree() || bounds.Size.Y < 60 || bounds.Size.X < 95 ||
                !screen.Encloses(bounds))
                throw new InvalidOperationException(
                    $"{stage}: botón fuera de pantalla o demasiado pequeño: {button.Text} rect={bounds} pantalla={screen}.");
            GD.Print($"MOBILE_BUTTON_OK:{stage}: {button.Text} {bounds}");
        }
    }

    private void AssertOptionAScreen(string stage, Control root)
    {
        Rect2 screen = GetViewport().GetVisibleRect();
        if (root.FindChild("OptionAHeader", true, false) is not Control ||
            root.FindChild("OptionAFooter", true, false) is not Control)
            throw new InvalidOperationException($"{stage}: faltan barras de navegación de la opción A.");
        var header = ((Control)root.FindChild("OptionAHeader", true, false)).GetGlobalRect();
        var footer = ((Control)root.FindChild("OptionAFooter", true, false)).GetGlobalRect();
        var content = ((Control)root.FindChild("ResponsiveContentHost", true, false)).GetGlobalRect();
        if (!screen.Grow(1).Encloses(header) || !screen.Grow(1).Encloses(footer) ||
            content.Position.Y < header.End.Y - 1 || content.End.Y > footer.Position.Y + 1)
            throw new InvalidOperationException($"{stage}: las barras comunes tapan el contenido o salen de pantalla.");
        for (int page = 0; page < 8; page++)
        {
            var navigation = (Button)root.FindChild("OptionANav" + page, true, false);
            if (!navigation.IsVisibleInTree() || !footer.Grow(1).Encloses(navigation.GetGlobalRect()))
                throw new InvalidOperationException($"{stage}: la opción inferior {page} no es accesible.");
        }
        foreach (var control in root.FindChildren("*", "Control", true, false).OfType<Control>())
        {
            if (!control.IsVisibleInTree()) continue;
            if (control is ScrollContainer sc && sc.HorizontalScrollMode != ScrollContainer.ScrollMode.Disabled)
                throw new InvalidOperationException($"{stage}: el panel {sc.Name} permite desbordamiento horizontal.");
            if (control is not BaseButton && control is not LineEdit &&
                control is not SpinBox && control is not ItemList) continue;
            var rect = control.GetGlobalRect();
            if (!screen.Encloses(rect))
            {
                // El contenido puede desplazarse verticalmente, pero nunca
                // desbordarse horizontalmente. Las acciones fijas se prueban
                // por separado y siempre deben quedar dentro de pantalla.
                bool inVerticalScroll = false;
                for (Node? parent = control.GetParent(); parent != null; parent = parent.GetParent())
                    if (parent is ScrollContainer container && container.VerticalScrollMode != ScrollContainer.ScrollMode.Disabled)
                    { inVerticalScroll = true; break; }
                if (inVerticalScroll && rect.Position.X >= screen.Position.X &&
                    rect.End.X <= screen.End.X) continue;
                throw new InvalidOperationException($"{stage}: control fuera de pantalla: {control.Name} {rect} / {screen}");
            }
        }
        GD.Print($"OPTION_A_SCREEN_OK:{stage}");
    }

    private bool CheckMobileMenuSmoke()
    {
        if (!_mobileMenuSmoke) return false;
        if (_visualReview)
        {
            // Xvfb+OpenGL por software puede dibujar menos fotogramas que _Process.
            // El escenario avanza exclusivamente tras un FramePostDraw completado.
            long drawn = Interlocked.Read(ref _drawnFrames);
            if (drawn <= _lastVisualDraw) return true;
            _lastVisualDraw = drawn;
        }
        int frame = ++_mobileMenuSmokeFrame;
        try
        {
            string? scene = frame switch
            {
                14 => "01-clasificacion",
                16 => "02-victoria-3-de-5",
                17 => "02-derrota-3-de-5",
                21 => "03-inicio",
                36 => "04-pvp",
                56 => "05-versus-ia",
                76 => "06-lista-salas",
                96 => "07-crear-sala",
                116 => "08-perfil",
                137 => "09-ayuda",
                146 => "09-combinaciones",
                156 => "10-equipacion",
                163 => "12-suma-bonificaciones",
                _ => null
            };
            if (scene != null) CaptureVisualReview(scene);
            if (frame == 10 && _visualReview) _launcher.Modulate = new Color(1, 1, 1, 0);
            if (frame == 9)
            {
                _smokeScoreHud = new Hud();
                _ui.AddChild(_smokeScoreHud);
                _smokeScoreHud.Build();
                var simulated = new GameWorld(new WorldOptions { Mode = GameMode.Warfare, Bots = 0 });
                var player = simulated.AddPlayer("Mago de prueba", 0)!;
                _smokeScoreHud.ScoreOpen = true;
                _smokeScoreHud.Update(simulated.Snapshot(), player.Id, "Servidor de prueba", 0.016);
            }
            if (frame == 15)
            {
                // Nombre similar al que provocó el error real en Windows.
                _googleIdentityKnown = true;
                _googleIdentityName = "andresmartinez";
                _launcher.UpdateAccountStatus();
                var tableRect = _smokeScoreHud!.ScoreboardRectForSmoke;
                if (!GetViewport().GetVisibleRect().Encloses(tableRect))
                    throw new InvalidOperationException("Clasificación fuera de la pantalla: " + tableRect +
                        " pantalla=" + GetViewport().GetVisibleRect());
                GD.Print("MOBILE_SCOREBOARD_LAYOUT_OK");
                AssertHudBounds(_smokeScoreHud, "clasificacion");
                AssertVisibleTableData(_smokeScoreHud, "ScoreTable");
                _smokeScoreHud.ScoreOpen = false;
                var simulated = new GameWorld(new WorldOptions { Mode = GameMode.Warfare, Bots = 0 });
                var player = simulated.AddPlayer("Mago de prueba", 0)!;
                var won = simulated.Snapshot() with
                {
                    BestOf = 5, SeriesWins = [3, 2], Winner = 0, MatchEnded = true,
                    RestartIn = 8, RewardStatus = "Recompensa pendiente de confirmar en Firebase."
                };
                _smokeScoreHud.Update(won, player.Id, "Servidor LAN", 0.016);
                Rect2 screen = GetViewport().GetVisibleRect();
                var banner = (ElementTextLabel)_smokeScoreHud.FindChild("MatchBanner", true, false);
                var reward = (ElementTextLabel)_smokeScoreHud.FindChild("MatchRewardNotice", true, false);
                if (!banner.Text.Contains("HAS GANADO LA SERIE", StringComparison.Ordinal) ||
                    !reward.Text.Contains("pendiente", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("El resultado de la serie o la recompensa no tienen el texto correcto.");
                // La etiqueta interna «Opción A» no debe mostrarse al jugador.
                var visibleLabels = _launcher.FindChildren("*", "Control", true, false).OfType<ElementTextLabel>()
                    .Where(label => label.IsVisibleInTree()).Select(label => label.Text);
                if (visibleLabels.Any(text => text.Contains("O P C I Ó N", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("OPCIÓN A", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("INICIO: se muestra una etiqueta interna de diseño.");
            }
            if (frame == 18)
            {
                _smokeScoreHud?.QueueFree();
                _smokeScoreHud = null;
                _smokeRivalHud?.QueueFree();
                _smokeRivalHud = null;
                _launcher.Modulate = new Color(1, 1, 1, 1);
            }
            if (frame == 16)
            {
                // Los Container de Godot distribuyen a sus hijos en el siguiente fotograma;
                // comprobar tamaños en el mismo _Process genera falsas posiciones de 1 píxel.
                var banner = (ElementTextLabel)_smokeScoreHud!.FindChild("MatchBanner", true, false);
                var reward = (ElementTextLabel)_smokeScoreHud.FindChild("MatchRewardNotice", true, false);
                Rect2 screen = GetViewport().GetVisibleRect();
                if (!screen.Encloses(banner.GetGlobalRect()) || !screen.Encloses(reward.GetGlobalRect()))
                    throw new InvalidOperationException($"Resultado/recompensa fuera de pantalla: banner={banner.GetGlobalRect()}, recompensa={reward.GetGlobalRect()}, pantalla={screen}");
                GD.Print("MOBILE_SERIES_REWARD_LAYOUT_OK");
                AssertHudBounds(_smokeScoreHud, "victoria");
                // Se representa la perspectiva del rival con un HUD distinto,
                // como ocurre con dos móviles/PC independientes en la misma LAN.
                var simulated = new GameWorld(new WorldOptions { Mode = GameMode.Duel, MaxPlayers = 2, Bots = 0 });
                var blue = simulated.AddPlayer("Anfitrión", 0)!;
                var red = simulated.AddPlayer("Invitado", 1)!;
                var sameResult = simulated.Snapshot() with {
                    BestOf = 5, SeriesWins = [3, 2], Winner = 0,
                    MatchEnded = true, RestartIn = 7,
                    RewardStatus = "Recompensa pendiente de confirmar en Firebase."
                };
                _smokeRivalHud = new Hud();
                _ui.AddChild(_smokeRivalHud);
                _smokeRivalHud.Build();
                _smokeRivalHud.Update(sameResult, red.Id, "Servidor LAN", 0.016);
                if (!_smokeRivalHud.RoundAnnouncementForSmoke.Contains("HAS PERDIDO LA SERIE") ||
                    !_smokeRivalHud.RoundAnnouncementForSmoke.Contains("AZUL 3 — 2 ROJO") ||
                    !_smokeRivalHud.RoundAnnouncementForSmoke.Contains("en 7 s"))
                    throw new InvalidOperationException("El equipo rojo no recibe derrota, marcador y cuenta atrás.");
            }
            if (frame == 17)
            {
                var losing = _smokeRivalHud ?? throw new InvalidOperationException("HUD del perdedor ausente.");
                if (!losing.RoundAnnouncementVisibleForSmoke)
                    throw new InvalidOperationException("El aviso del equipo perdedor no es visible.");
                var title = (ElementTextLabel)losing.FindChild("MatchBanner", true, false);
                if (!GetViewport().GetVisibleRect().Encloses(title.GetGlobalRect()))
                    throw new InvalidOperationException("Aviso del perdedor fuera del viewport: " + title.GetGlobalRect());
                AssertHudBounds(losing, "derrota");
                GD.Print("MOBILE_SERIES_LOSS_LAYOUT_OK");
            }
            if (frame == 20)
            {
                AssertHeaderAndHome(_launcher, GetViewport().GetVisibleRect(), "MOBILE");
                var fullPanel = _launcher.FindChild("OptionAFullWidthPanel", true, false) as Control;
                var choices = _launcher.FindChild("OptionAHomeCards", true, false) as Control;
                if (fullPanel == null || choices == null ||
                    fullPanel.Size.X < GetViewport().GetVisibleRect().Size.X * 0.75f ||
                    choices.Size.X < GetViewport().GetVisibleRect().Size.X * 0.65f)
                    throw new InvalidOperationException("INICIO: las cuatro tarjetas han quedado encogidas.");
                if (choices is not GridContainer home || home.Columns != 2)
                    throw new InvalidOperationException("INICIO: las cuatro tarjetas deben organizarse 2×2.");
                var cards = choices.GetChildren().OfType<Button>().ToArray();
                if (cards.Length != 4)
                    throw new InvalidOperationException("INICIO: faltan tarjetas principales.");
                foreach (Button card in cards)
                    if (card.Size.X < 170 || card.GetGlobalRect().Position.X < 0 ||
                        card.GetGlobalRect().End.X > GetViewport().GetVisibleRect().End.X)
                        throw new InvalidOperationException("INICIO: tarjeta demasiado estrecha o desbordada: " + card.Text);
                if (_launcher.FindChild("OptionAAccountSummary", true, false) is not ElementTextLabel accountHeader ||
                    !accountHeader.IsVisibleInTree() || accountHeader.Text.Length < 3 ||
                    accountHeader.Text.Contains("V:") || accountHeader.Text.Contains("D:"))
                    throw new InvalidOperationException("INICIO: falta el estado de Google o se duplican los resultados del perfil.");
                if (_launcher.FindChildren("*", "Control", true, false).OfType<ElementTextLabel>()
                    .Any(label => label.IsVisibleInTree() && label.Text.Contains("victorias ·") &&
                        label.Name != "OptionAAccountSummary"))
                    throw new InvalidOperationException("INICIO: se muestra el resumen de cuenta dentro del menú.");
            }
            if (frame == 22)
            {
                _googleIdentityKnown = false;
                _googleIdentityName = "";
                _launcher.UpdateAccountStatus();
                // Verificación de la salida visible en sala de espera sin un servidor real.
                bool left = false;
                var wait = new RoomWaitingPanel();
                _ui.AddChild(wait);
                wait.Build(_ => { }, () => left = true);
                var exit = wait.FindChild("WaitingRoomExit", true, false) as Button;
                if (exit == null || !exit.IsVisibleInTree() || exit.Text != "Salir de la sala")
                    throw new InvalidOperationException("SALA DE ESPERA: no hay botón Salir visible.");
                exit.EmitSignal(Button.SignalName.Pressed);
                if (!left) throw new InvalidOperationException("SALA DE ESPERA: el botón Salir no responde.");
                wait.QueueFree();
            }
            if (frame == 25) _launcher.OpenMobileSmokePage(1);
            if (frame == 35)
            {
                var buttons = _launcher.MobilePrimaryButtons;
                AssertMobileActions("PVP", buttons, 2);
                AssertOptionAScreen("PVP", _launcher);
                AssertTouchScroll(_launcher, "LauncherSettingsScroll", "PVP");
                if (_launcher.FindChild("OnlineFirebaseButton", true, false) is not Button online ||
                    !online.IsVisibleInTree() || online.Disabled)
                    throw new InvalidOperationException("PVP: falta el acceso al servidor online por Firebase.");
                if (!buttons.Any(button => button.Text == "Ver salas del servidor LAN" || button.Text == "Ver salas por IP"))
                    throw new InvalidOperationException("PVP: la acción principal apunta a una pestaña oculta o a Alojar.");
            }
            if (frame == 45) _launcher.OpenMobileSmokePage(2);
            if (frame == 55)
            {
                if (_launcher.FindChild("PracticeLaboratoryButton", true, false) != null)
                    throw new InvalidOperationException("El acceso redundante al laboratorio sigue visible.");
                foreach (string nodeName in new[] { "OptionAFullWidthPanel", "OptionASettingsColumns",
                    "OptionAPrimaryActions", "LauncherSettingsScroll", "OptionAHomeCards", "OptionAIaSettingsColumns" })
                {
                    if (_launcher.FindChild(nodeName, true, false) is Control part)
                        GD.Print($"RESPONSIVE_DEBUG:{nodeName}: minimum={part.GetCombinedMinimumSize()} size={part.Size} rect={part.GetGlobalRect()}");
                }
                foreach (TabContainer tab in _launcher.FindChildren("*", "TabContainer", true, false).OfType<TabContainer>())
                    GD.Print($"RESPONSIVE_DEBUG:TABS: minimum={tab.GetCombinedMinimumSize()} size={tab.Size} rect={tab.GetGlobalRect()}");
                AssertMobileActions("VERSUS_IA", _launcher.MobilePrimaryButtons, 2);
                AssertOptionAScreen("VERSUS_IA", _launcher);
                AssertTouchScroll(_launcher, "LauncherSettingsScroll", "VERSUS_IA");
            }
            if (frame == 65)
            {
                _smokeLobby = new RoomLobby();
                _ui.AddChild(_smokeLobby);
                _smokeLobby.Build(this, "127.0.0.1:27015", "", "Mago", -1, WizardType.Balanced);
            }
            if (frame == 75)
            {
                AssertMobileActions("SALAS_PVP", _smokeLobby!.MobileRoomButtons.Append(_smokeLobby.MobileBackButton!), 4);
                AssertOptionAScreen("SALAS_PVP", _smokeLobby);
                _smokeLobby!.SetBusy(true);
                _smokeLobby.ShowError("Contraseña de la sala incorrecta.");
                if (_smokeLobby.IsBusyForSmoke || _smokeLobby.StatusForSmoke != "Contraseña de la sala incorrecta.")
                    throw new InvalidOperationException("SALAS_PVP: un error de clave no permite reintentar o no aparece en pantalla.");
            }
            if (frame == 85) _smokeLobby!.OpenCreation();
            if (frame == 95)
            {
                AssertMobileActions("CREAR_SALA", _smokeLobby!.MobileRoomButtons.Append(_smokeLobby.MobileBackButton!), 3);
                AssertOptionAScreen("CREAR_SALA", _smokeLobby);
            }
            if (frame == 105)
            {
                _smokeLobby!.QueueFree();
                _googleIdentityKnown = true; // Simula Google autenticado sin tocar cuentas reales.
                _googleIdentityName = "Mago de pruebas";
                OpenAccountProfile();
                _accountProfilePanel!.UpdateGoogleStatus(
                    "Error al leer Firestore: Firestore (403 PERMISSION_DENIED) ha denegado la lectura de users/{tu cuenta}.");
            }
            if (frame == 115)
            {
                var profileScroll = (ScrollContainer)_accountProfilePanel!.FindChild("ProfileContentScroll", true, false);
                foreach (var action in _accountProfilePanel.VisibleFooterButtons)
                    if (action.Disabled || action.Size.Y < 60) throw new InvalidOperationException("El perfil desactiva una acción local o reduce su tamaño táctil.");
                profileScroll.EnsureControlVisible((Control)_accountProfilePanel.FindChild("ProfileTouchControls", true, false));
                AssertOptionAScreen("PERFIL_403", _accountProfilePanel);
                string[] labels = _accountProfilePanel.FindChildren("*", "Control", true, false)
                    .OfType<ElementTextLabel>().Select(label => label.Text).ToArray();
                if (!labels.Any(t => t.Contains("Google conectado", StringComparison.Ordinal)) ||
                    !labels.Any(t => t.Contains("403", StringComparison.Ordinal)))
                    throw new InvalidOperationException(
                        "El perfil oculta que Google está autenticado cuando Firestore rechaza sus permisos.");
                var update = (Button)_accountProfilePanel.FindChild("ProfileCheckUpdates", true, false);
                if (update.Disabled || !update.IsVisibleInTree()) throw new InvalidOperationException("El perfil no permite buscar actualizaciones.");
                update.EmitSignal(Button.SignalName.Pressed);
                var updateStatus = (ElementTextLabel)_accountProfilePanel.FindChild("ProfileUpdateStatus", true, false);
                if (updateStatus.Text.Length == 0 || update.Disabled)
                    throw new InvalidOperationException("La búsqueda manual no confirma su estado o deja el botón bloqueado.");
                GD.Print("PROFILE_UPDATE_BUTTON_OK: botón accesible sin depender de Firestore y resultado visible.");
            }
            if (frame == 117)
            {
                var action = (Button)_accountProfilePanel!.FindChild("ProfileTouchControls", true, false);
                var scroll = (ScrollContainer)_accountProfilePanel.FindChild("ProfileContentScroll", true, false);
                if (!scroll.GetGlobalRect().Grow(1).Encloses(action.GetGlobalRect()))
                    throw new InvalidOperationException("Los ajustes táctiles no son accesibles desde Mi perfil.");
                action.EmitSignal(Button.SignalName.Pressed);
            }
            if (frame == 120)
            {
                if (!ControlsOpen) throw new InvalidOperationException("Mi perfil no abre los ajustes táctiles.");
                AssertOptionAScreen("AJUSTES_DESDE_PERFIL", _controlsSettings!);
                float originalSize = _touchSettings!.Size;
                ((HSlider)_controlsSettings!.FindChild("TouchSize", true, false)).Value = originalSize > 1 ? .8 : 1.2;
                ((Button)_controlsSettings.FindChild("OptionANav3", true, false)).EmitSignal(Button.SignalName.Pressed);
                if (ControlsOpen || _accountProfilePanel?.IsVisibleInTree() != true || _touchSettings.Size != originalSize)
                    throw new InvalidOperationException("Volver a Mi perfil guarda cambios sin confirmar o pierde el perfil.");
                GD.Print("PROFILE_TOUCH_SETTINGS_OK: acceso local, navegación común y cancelación sin guardar.");
            }
            if (frame == 125)
            {
                _accountProfilePanel!.QueueFree();
                _accountProfilePanel = null;
                ToggleHelp();
            }
            if (frame == 135)
            {
                AssertOptionAScreen("AYUDA", _optionAHelp!);
                AssertVisibleTableData(_optionAHelp!, "MenuHelpControlsTable");
                AssertTouchScroll(_optionAHelp!, "HelpContentScroll", "AYUDA_CONTROLES");
            }
            if (frame == 135 && _optionAHelp!.ContentWidthFraction < 0.78f)
                throw new InvalidOperationException("AYUDA: las tarjetas no ocupan el ancho disponible.");
            if (frame == 136) _optionAHelp!.SelectForSmoke(2);
            if (frame == 138)
            {
                AssertOptionAScreen("AYUDA_MAGOS", _optionAHelp!);
                AssertTouchScroll(_optionAHelp!, "HelpContentScroll", "AYUDA_MAGOS", overCard: true);
            }
            if (frame == 139) _optionAHelp!.SelectForSmoke(3);
            if (frame == 141)
            {
                AssertOptionAScreen("AYUDA_ELEMENTOS", _optionAHelp!);
                AssertTouchScroll(_optionAHelp!, "HelpContentScroll", "AYUDA_ELEMENTOS", overCard: true);
            }
            if (frame == 142) _optionAHelp!.SelectForSmoke(4);
            if (frame == 144) AssertOptionAScreen("AYUDA_RELIQUIAS", _optionAHelp!);
            if (frame == 144) _optionAHelp!.SelectForSmoke(5);
            if (frame == 145)
            {
                AssertOptionAScreen("AYUDA_COMBINACIONES", _optionAHelp!);
                var book = (SpellBook)_optionAHelp!.FindChild("MenuSpellCombinations", true, false);
                void Press(string name) => ((Button)book.FindChild(name, true, false)).EmitSignal(Button.SignalName.Pressed);
                Press("SpellElementWater"); Press("SpellElementCold");
                var damage = (ElementTextLabel)book.FindChild("SpellDamageValues", true, false);
                if (damage.Text != SpellBalance.Format(new SpellRecipe([Element.Water, Element.Cold]), CastMode.Forward) ||
                    book.FindChildren("*", "ItemList", true, false).Count != 0)
                    throw new InvalidOperationException("La ayuda no combina directamente o conserva la lista compleja.");
            }
            if (frame == 147)
            {
                var book = (SpellBook)_optionAHelp!.FindChild("MenuSpellCombinations", true, false);
                void Press(string name) => ((Button)book.FindChild(name, true, false)).EmitSignal(Button.SignalName.Pressed);
                var damage = (ElementTextLabel)book.FindChild("SpellDamageValues", true, false);
                Press("SpellElementCold");
                Press("SpellCastMode1");
                if (damage.Text != SpellBalance.Format(new SpellRecipe([Element.Water, Element.Cold, Element.Cold]), CastMode.Self))
                    throw new InvalidOperationException("La ayuda no cambia la forma de lanzamiento.");
                Press("SpellClear");
                if (damage.Visible) throw new InvalidOperationException("Limpiar conserva los datos del hechizo anterior.");
                GD.Print("SIMPLE_COMBINATIONS_OK: selección directa, repetición de elementos, tres modos y limpieza.");
                _optionAHelp!.SelectForSmoke(6);
            }
            if (frame == 149)
            {
                AssertOptionAScreen("AYUDA_BONIFICACIONES", _optionAHelp!);
                AssertTouchScroll(_optionAHelp!, "HelpContentScroll", "AYUDA_BONIFICACIONES");
                var bonusLabels = _optionAHelp!.FindChildren("*", "Control", true, false).OfType<ElementTextLabel>()
                    .Select(l => l.Text).ToArray();
                if (!bonusLabels.Any(t => t.Contains("DEFENSA NO ES CAPACIDAD")) ||
                    !bonusLabels.Any(t => t.Contains("100 de daño")) ||
                    !bonusLabels.Any(t => t.Contains("Absorción del escudo")))
                    throw new InvalidOperationException("Ayuda no documenta defensa y capacidad del escudo.");
                CloseOptionAHelp();
                EquipmentFixture(false, copies: 12);
            }
            if (frame == 155)
            {
                AssertOptionAScreen("EQUIPACION", _equipmentPanel!);
                AssertInventorySplit(_equipmentPanel!);
                AssertTouchScroll(_equipmentPanel!, "EquipmentPickerScroll", "EQUIPACION");
            }
            if (frame == 156) _equipmentPanel!.PressInspect(EquipmentSlot.Chest);
            if (frame == 158)
            {
                var picker = _equipmentPanel!;
                AssertOptionAScreen("SELECTOR_EQUIPO", picker);
                AssertTouchScroll(picker, "EquipmentPickerScroll", "SELECTOR_EQUIPO", overButton: true);
                if (!picker.PickerVisible || picker.HasChanges || picker.PickerItems.Length != 60 || picker.PickerColumns is < 1 or > 3)
                    throw new InvalidOperationException("El gesto seleccionó un objeto o el selector perdió objetos/columnas.");
                picker.HandleBack();
                var sum = (Button)_equipmentPanel!.FindChild("EquipmentTotalsTab", true, false);
                sum.EmitSignal(Button.SignalName.Pressed);
            }
            if (frame == 160)
            {
                var table = (GridContainer)_equipmentPanel!.FindChild("EquipmentTotalsTable", true, false);
                var scroll = (ScrollContainer)_equipmentPanel.FindChild("EquipmentTotalsScroll", true, false);
                // En 450 px el desglose de cuatro líneas cabe entero al desplazarlo,
                // aunque no quepa simultáneamente con su cabecera.
                if (GetViewport().GetVisibleRect().Size.Y <= 480)
                {
                    foreach (var heading in table.GetChildren().OfType<ElementTextLabel>().Take(table.Columns))
                        if (!scroll.GetGlobalRect().Grow(1).Encloses(heading.GetGlobalRect()))
                            throw new InvalidOperationException("La cabecera de bonificaciones no es accesible.");
                    scroll.EnsureControlVisible(table.GetChildren().OfType<ElementTextLabel>().ElementAt(table.Columns * 2 - 1));
                }
            }
            if (frame == 162)
            {
                AssertOptionAScreen("SUMA_BONIFICACIONES", _equipmentPanel!);
                AssertVisibleTableData(_equipmentPanel!, "EquipmentTotalsTable", includeHeader: GetViewport().GetVisibleRect().Size.Y > 480);
                AssertTouchScroll(_equipmentPanel!, "EquipmentTotalsScroll", "SUMA_BONIFICACIONES");
            }
            if (frame < 165) return false;
            _googleIdentityKnown = false;
            GD.Print("MOBILE_MENU_LAYOUT_OK: PvP, IA, salas, perfil, ajustes, ayuda y equipación; navegación común y contenido accesible.");
            _mobileMenuSmoke = false;
            GetTree().Quit(0);
            return true;
        }
        catch (Exception error)
        {
            GD.PushError("MOBILE_MENU_LAYOUT_FAILED: " + error);
            _mobileMenuSmoke = false;
            GetTree().Quit(1);
            return true;
        }
    }
}
