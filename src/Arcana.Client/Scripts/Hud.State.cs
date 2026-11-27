using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Hud
{
    public void Update(WorldSnapshot snapshot, int id, string serverName, double delta)
    {
        if (!_rewardNoticeBlocked && _rewardFits) _rewardSeconds = Math.Max(0, _rewardSeconds - (float)delta);
        UpdateRewardVisibility();
        _rewardBackdrop.Modulate = new Color(1, 1, 1, Math.Clamp(_rewardSeconds / .25f, 0, 1));
        _actionNoticeSeconds = Math.Max(0, _actionNoticeSeconds - (float)delta);
        _actionNotice.Visible = _actionNoticeSeconds > 0;
        _feedTime -= (float)delta; _feed.Text = _feedTime > 0 ? _feedText : "";
        bool showScore = snapshot.Map != ArenaMapId.Practice && (ScoreOpen || Input.IsPhysicalKeyPressed(Key.Tab));
        bool opened = !_scorePanel.Visible && showScore;
        _scorePanel.Visible = showScore; _scoreClock += (float)delta;
        if (opened) MoveChild(_scoreCenter, GetChildCount() - 1);
        _bannerBackdrop.Visible = !showScore && _banner.Text.Length > 0;
        if (_scorePanel.Visible && (opened || _scoreClock >= 0.25f))
        { _scoreClock = 0; UpdateScore(snapshot, id); }
        if (ReferenceEquals(snapshot, _lastSnapshot)) return;
        var previous = _lastSnapshot;
        _lastSnapshot = snapshot;
        if (previous?.Mode != snapshot.Mode) ResizeLayout();
        var player = snapshot.Players.FirstOrDefault(p => p.Id == id); if (player == null) return;
        var old = _lastPlayer; _lastPlayer = player;
        if (_lastServerName != serverName || previous?.Mode != snapshot.Mode || previous.Map != snapshot.Map)
        {
            _lastServerName = serverName;
            _server.Text = serverName.ToUpperInvariant() + (snapshot.Map == ArenaMapId.Practice ? "" : "  /  " + Ui.ModeName(snapshot.Mode).ToUpperInvariant());
        }
        if (old == null || old.Name != player.Name || old.Team != player.Team)
        {
            _name.Text = player.Name + (player.Team == 0 ? "  /  AZUL" : "  /  ROJO");
            _name.AddThemeColorOverride("font_color", player.Team == 0 ? Ui.Blue : Ui.Red);
        }
        if (old == null || old.WizardType != player.WizardType)
        {
            _affinity.Text = WizardTypes.Name(player.WizardType);
            _affinity.TooltipText = WizardTypes.Description(player.WizardType);
            _affinity.AddThemeColorOverride("font_color", Ui.WizardColor(player.WizardType));
        }
        if (old == null || !(old.Buffs ?? []).AsSpan().SequenceEqual(player.Buffs ?? []))
        {
            _buffs.Text = ""; _buffs.Visible = false; // Consultar las reliquias desde el menú de partida.
            _buffs.TooltipText = string.Join("\n", (player.Buffs ?? []).Select(b => RelicBuffs.Name(b.Kind) + " ×" + b.Stacks + ": " + RelicBuffs.Description(b.Kind)));
        }
        _objective.Text = MatchRules.Objective(snapshot.Mode, snapshot.ScoreLimit, snapshot.StartingTickets, snapshot.Map);
        _controls.Text = snapshot.Map == ArenaMapId.Practice ? "F1  Controles     ESC  Menú"
            : "F1  Controles     TAB  Clasificación     ESC  Menú";
        bool hold = snapshot.Mode == GameMode.Warfare && snapshot.Map == ArenaMapId.Citadel;
        bool conquest = snapshot.Map == ArenaMapId.Frontline;
        if (hold) _objective.Text += snapshot.HoldingTeam >= 0
            ? $"\n{(snapshot.HoldingTeam == 0 ? "AZUL" : "ROJO")} · {snapshot.HoldSeconds:0.0} / 15 s"
            : "\nControl total: 0 / 15 s";
        _health.MaxValue = player.MaxHealth; _health.Value = player.Health;
        _healthText.Text = $"VIDA  {Math.Ceiling(player.Health):0} / {player.MaxHealth:0}";
        if (snapshot.Map == ArenaMapId.Ascension) _healthText.Text += $"  (+{player.MaxHealth - Wizard.BaseMaxHealth - 100 * (player.Buffs ?? []).Where(b => b.Kind == BuffKind.Vitality).Sum(b => b.Stacks):0})";
        _shield.MaxValue = Ui.ShieldMaximum(player); _shield.Value = player.Shield;
        bool shielded = player.Alive && player.Shield > 0;
        if (old == null || shielded != (old.Alive && old.Shield > 0))
        {
            _shieldFill.BgColor = shielded ? Ui.Shield : Ui.Shield.Darkened(0.65f);
            _shieldTrack.BorderColor = shielded ? Ui.Shield : Ui.Muted.Darkened(0.5f);
            _shieldText.AddThemeColorOverride("font_color", shielded ? Ui.Text : Ui.Muted);
        }
        _shieldText.Text = shielded ? $"ESCUDO  {Math.Ceiling(player.Shield):0} / {_shield.MaxValue:0}"
            + (player.ShieldSeconds > 0 ? $"  ·  {player.ShieldSeconds:0.0} s" : "") : "ESCUDO  0  /  INACTIVO";
        _energy.Value = player.Energy; _charge.Value = player.Casting ? player.Charge : 0; _energyText.Text = $"ENERGÍA  {Math.Floor(player.Energy):0} / 100";
        _statuses.Clear(); var statuses = _statuses;
        if (player.Wet > 0) statuses.Add("Mojado"); if (player.Burning > 0) statuses.Add("Quemándose");
        if (player.Stunned) statuses.Add("Aturdido"); if (player.WaterSlowed) statuses.Add("Ralentizado por agua");
        if (player.ArcaneMarked) statuses.Add("Marca arcana");
        if (player.Chill > 20) statuses.Add("Frío"); if (player.Frozen > 0) statuses.Add("Congelado"); if (player.Haste > 0) statuses.Add("Celeridad");
        if (player.Ward.Length > 0) statuses.Add("Guarda: " + string.Join(", ", player.Ward.Select(Ui.ElementName)));
        if (player.RegenerationRate > 0) statuses.Add($"Regeneración +{player.RegenerationRate:0} vida/s");
        _status.Text = statuses.Count > 0 ? string.Join(" · ", statuses) : "Sin estados alterados";
        for (int i = 0; i < 3; i++)
        {
            bool selected = i < player.DisplayElements.Length;
            Element? value = selected ? player.DisplayElements[i] : null;
            if (old != null && _selectedIcons[i].Value == value) continue;
            _selected[i].Text = selected ? Ui.ElementKey(player.DisplayElements[i]) : "—";
            _selected[i].AddThemeColorOverride("font_color", selected ? Ui.Text : Ui.Muted);
            _selectedIcons[i].Value = value;
            _selectedStyles[i].BorderColor = selected ? Ui.ElementColor(player.DisplayElements[i]) : new Color("405372");
        }
        for (int i = 0; i < 4; i++)
        {
            var magick = SpellBalance.Magicks[i];
            float cost = i == 3 ? player.ReanimationCost : magick.Cost;
            int seconds = (int)Math.Ceiling(player.Cooldowns[i]);
            bool ready = player.Cooldowns[i] <= 0 && player.Energy >= cost;
            if (_magickReady[i] == null || _magickSeconds[i] != seconds || _magickCosts[i] != cost)
            {
                _magickSeconds[i] = seconds; _magickCosts[i] = cost;
                _magicks[i].Text = $"{i + 1} {magick.Name} " + (seconds > 0 ? $"{seconds}s" : $"{cost:0}");
            }
            if (_magickReady[i] != ready)
            { _magickReady[i] = ready; _magicks[i].AddThemeColorOverride("font_color", ready ? Ui.Gold : Ui.Muted); }
        }
        _match.Visible = snapshot.Mode == GameMode.Arena; _teams.Visible = !_match.Visible;
        if (snapshot.Map == ArenaMapId.Practice) _match.Visible = _teams.Visible = false;
        if (_match.Visible)
            _match.Text = "ARENA  ·  " + snapshot.Players.Max(p => p.Kills) + " BAJAS";
        else
        {
            _blueScore.Text = $"AZUL  {(hold || conquest ? snapshot.Points.Count(p => p.Owner == 0) + "/" + snapshot.Points.Length : snapshot.Scores[0].ToString())}";
            _redScore.Text = $"{(hold || conquest ? snapshot.Points.Count(p => p.Owner == 1) + "/" + snapshot.Points.Length : snapshot.Scores[1].ToString())}  ROJO";
        }
        if (snapshot.Defense is { } defense)
        {
            _teams.Visible = false; _match.Visible = true;
            int lives = Math.Max(0, DefenseRules.PlayerLives - player.Deaths);
            _buffs.Visible = false; // Las reliquias se consultan desde el menú.
            _match.Text = (defense.Paused ? "PAUSA · " : "") + $"SUPERVIVENCIA · VIDAS {lives}/2";
            _match.AddThemeColorOverride("font_color", lives <= 1 ? Ui.Red : Ui.Gold);
            _objective.Text = $"{DefenseRules.Clock(defense.Elapsed)} · Dificultad {defense.Difficulty} · Enemigos {defense.Minions.Length}/{defense.MinionLimit}\n" +
                (defense.Minions.Any(m => m.Kind == MinionKind.Boss) ? "¡COLOSO EN EL MAPA!" : $"Coloso en {Math.Ceiling(defense.NextBossIn)} s");
            if (!MobileLayout)
            {
                if (GetViewportRect().Size.Y < 700) _mapPanel.Visible = false;
                float mobileScale = Math.Min(1, GetViewportRect().Size.Y / 720f);
                _server.Visible = _status.Visible = _affinity.Visible = false;
                _playerPanel.CustomMinimumSize = new(310, 176); _playerPanel.Size = new(310, 176);
                _playerPanel.Scale = Vector2.One * mobileScale / GameplayScale;
                _playerPanel.Position = new Vector2(22 * mobileScale, 18 * mobileScale) / GameplayScale;
                LayoutSurvivalHeader(GetViewportRect().Size);
            }
            if (GetViewportRect().Size.X < 900)
            {
                _match.Text = (defense.Paused ? "PAUSA" : "SUPERVIVENCIA") + $"\nVIDAS {lives}/2";
                _objective.Text = $"{DefenseRules.Clock(defense.Elapsed)} · Dificultad {defense.Difficulty}\nEnemigos {defense.Minions.Length}/{defense.MinionLimit}";
                _objective.CustomMinimumSize = Vector2.Zero;
            }
            _feed.Visible = false;
        }
        // Todos los clientes ven exactamente el mismo resultado y la misma
        // cuenta atrás, pero el titular se expresa desde su propio equipo.
        _banner.Text = snapshot.MatchEnded
            ? RoundPresentation.Message(snapshot, id)
            : !player.Alive && snapshot.Defense != null && player.Deaths >= DefenseRules.PlayerLives ? "VIDAS AGOTADAS\nTus aliados continúan"
            : !player.Alive ? "HAS CAÍDO\nReapareces en " +
                (snapshot.Map == ArenaMapId.Ascension ? $"{player.Respawn:0.0}" : $"{Math.Ceiling(player.Respawn):0}") + " s" : "";
        _bannerBackdrop.Visible = !showScore && _banner.Text.Length > 0;
        _banner.AddThemeColorOverride("font_color", snapshot.MatchEnded
            ? RoundPresentation.IsWinner(snapshot, id) ? Ui.Gold : Ui.Red : Ui.Gold);
        if (snapshot.RewardStatus != _lastRewardStatus)
        {
            _lastRewardStatus = snapshot.RewardStatus;
            if (!string.IsNullOrWhiteSpace(snapshot.RewardStatus)) RewardNotice(snapshot.RewardStatus);
        }
        foreach (var e in snapshot.Events)
        {
            if (e.Id <= _lastEvent) continue;
            _lastEvent = Math.Max(_lastEvent, e.Id);
            if (e.Kind == "defense_oracle") ActionNotice(e.Text);
            if (e.Kind == "defense_item" && e.ActorId == id)
                RewardNotice(e.Text.StartsWith(player.Name + ": ", StringComparison.Ordinal) ? e.Text[(player.Name.Length + 2)..] : e.Text);
            if (e.Text.Length == 0) continue;
            var lines = (_feedText + "\n" + e.Text).Split('\n').Where(t => t.Length > 0).TakeLast(4);
            _feedText = string.Join("\n", lines); _feedTime = 7;
        }
        if (player.CastInterruption is { } interruption && (old == null || old.CastInterruption != interruption))
        {
            ActionNotice("HECHIZO INTERRUMPIDO\n" + interruption.Reason);
            _actionNoticeBackdrop.Visible = true;
            string source = interruption.SourceId == id ? "tú mismo" : snapshot.Players.FirstOrDefault(p => p.Id == interruption.SourceId)?.Name ?? "un enemigo";
            _actionNotice.TooltipText = interruption.Reason + (interruption.SourceId > 0 ? " · Origen: " + source : "") +
                (interruption.Cause == SpellInterruptionCause.WetLightning ? "\nEspera a secarte o usa Fuego Sobre mí para secarte antes de preparar Rayo." :
                    "\nEl canal ha terminado. Suelta el control, prepara los elementos y vuelve a lanzar.");
        }
        _feed.Text = _feedTime > 0 ? _feedText : "";
        _map.State = snapshot; _map.LocalId = id; _map.QueueRedraw();
    }

    private void UpdateScore(WorldSnapshot snapshot, int id)
    {
        // El nombre puede ocupar varias líneas; el padding y los bordes también cuentan.
        _scoreScroll.CustomMinimumSize = new(0, Math.Clamp((snapshot.Players.Length + 1) * 64, 150,
            Math.Max(150, (int)Size.Y - 200)));
        _seriesStatus.Text = snapshot.BestOf > 1 && snapshot.SeriesWins is { Length: >= 2 } wins
            ? $"{(wins[0] >= MatchSeries.WinsNeeded(snapshot.BestOf) || wins[1] >= MatchSeries.WinsNeeded(snapshot.BestOf) ? "SERIE FINALIZADA" : $"MEJOR DE {snapshot.BestOf}")} · AZUL {wins[0]} — {wins[1]} ROJO"
            : "CLASIFICACIÓN DE LA PARTIDA";
        int cell = 0;
        foreach (var p in snapshot.Players.OrderByDescending(p => p.Kills))
        {
            var color = p.Team == 0 ? Ui.Blue : Ui.Red;
            foreach (var text in new[] { p.Name + (p.Id == id ? " (tú)" : ""), p.Team == 0 ? "Azul" : "Rojo", p.Kills.ToString(), p.Deaths.ToString(), p.Alive
                ? snapshot.Map is ArenaMapId.Ascension or ArenaMapId.Relics ? $"{Math.Ceiling(p.Health):0} / {p.MaxHealth:0}" : Math.Ceiling(p.Health).ToString() : "Caído" })
            {
                if (cell == _scoreCells.Count)
                {
                    var label = Ui.TableCell(_scoreRows, "", cell % 5 >= 2, color, size: 17);
                    label.MouseFilter = MouseFilterEnum.Ignore;
                    _scoreCells.Add(label);
                }
                var current = _scoreCells[cell++]; current.Text = text; current.Visible = true;
                current.AddThemeColorOverride("font_color", color);
            }
        }
        for (int i = cell; i < _scoreCells.Count; i++) _scoreCells[i].Visible = false;
    }
}
