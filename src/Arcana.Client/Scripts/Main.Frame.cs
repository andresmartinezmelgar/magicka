using System.Collections.Concurrent;
using System.Text.Json;
using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    public override void _Process(double delta)
    {
        if (_applicationSuspended) return;
        if (_verification?.CheckSmokeErrors() == true) return;
        try { ProcessFrame(delta); }
        catch (Exception e)
        {
            Diagnostics.Error("client.frame_failed", "Fallo al actualizar el cliente.", e, Context());
            if (_verification?.FailFrame(e) == true) return;
            throw;
        }
    }

    private void ProcessFrame(double delta)
    {
        if (_verification?.CheckSmokeErrors() == true) return;
        while (_verification?.HoldPosted != true && _posted.TryDequeue(out var action))
            try { action(); }
            catch (Exception e)
            {
                Diagnostics.Error("client.callback_failed", "Fallo en una operación de interfaz.", e, Context());
                if (_verification?.FailFrame(e) == true) return;
            }
        TickGoogleAuth(delta);
        TickBestiary(delta);
        TickInventoryAutoSell(delta);
        TickItemShopPreload();
        TickMatchStatistics(delta);
        TickSurvivalAutosave(delta);
        TickAudio(delta);
        _renderFrames++;
        if (_survivalBiomeSmoke) return;
        if (CheckDesktopHeaderSmoke()) return;
        if (CheckMobileMenuSmoke()) return;
        if (CheckAccountEntrySmoke()) return;
        if (CheckPublicEquipmentSmoke()) return;
        if (CheckEquipmentSmoke()) return;
        if (CheckShopSmoke()) return;
        if (CheckSaleSmoke()) return;
        if (CheckUsabilitySmoke()) return;
        if (CheckDefenseSmoke()) return;
        if (CheckStatisticsSmoke()) return;
        if (CheckBestiarySmoke()) return;
        if (CheckSurvivalSaveSmoke()) return;
        if (_profiler?.Running == true || _reconnecting) return;
        if (Loading) { ProcessLoading(); _verification?.CheckLoadingFrame(); return; }
        _verification?.BeforeFrame(delta);
        _metricsTime += delta;
        if (_metricsTime >= 30)
        {
            Diagnostics.Info("client.metrics", "Estado periódico del cliente.", Context()); _metricsTime = 0;
        }
        if (_playing)
        {
            UpdateNetworkStats(delta);
            var latest = _connection?.Latest ?? _snapshot;
            if (latest == null) return;
            _snapshot = latest;
            if (!TutorialActive) ReportDefensePerformance(latest, delta);
            SynchronizeLocalLife();
            if (Ui.IsMobile) UpdateTouchAim();
            else
            {
                _aim = _renderer.Cursor(GetViewport().GetMousePosition());
                bool practiceUi = ViewingDamageSummary || InspectingEquipment || _practicePanel?.PointerOverPanel == true ||
                    _defensePanel?.PointerOverPanel == true || _hud?.PointerOverPanel == true || TutorialBlocksPointer(GetViewport().GetMousePosition());
                if (practiceUi && Input.IsMouseButtonPressed(MouseButton.Left)) _moveTo = false;
                UpdateMouseMovement(!practiceUi && Input.IsMouseButtonPressed(MouseButton.Left), Input.IsPhysicalKeyPressed(Key.Shift));
            }
            if (!ReferenceEquals(latest, _lastHealthSnapshot)) { _lastHealthSnapshot = latest; _lastTick = latest.Tick; _snapshotAge = 0; } else _snapshotAge += delta;
            if (_connection?.Connected == true && _snapshotAge > 8 && !latest.WaitingForPlayers)
            {
                Diagnostics.Warn("client.stale_snapshot", "El servidor dejó de enviar el estado.", context: Context());
                _connection.ReconnectAfterStall(); return;
            }
            _renderer.InterpolationDelay = _connection != null ? 0.075 : 1 / 60.0;
            if (_practiceSession != null) _renderer.PracticeCasterDescription = _practiceSession.CasterDescription;
            _renderer.Render(latest, _localId, _verification?.FixedRenderStep == true ? 1 / 60.0 : delta, true,
                _practicePanel?.OutfitPreview);
            _hud?.Update(latest, _localId, _serverName, delta);
            if (!TutorialActive) UpdatePersonalDamageSummary(latest, delta);
            InitializeSurvivalEquipmentSetup(latest, delta);
            if (_defensePanel != null)
            {
                _defensePanel.Visible = !_paused && !_help.Visible;
                _defensePanel.Update(latest, _localId, _connection != null, _connection?.Server?.RewardsAvailable == true);
                if (latest.MatchEnded && !_defenseEnded) _defensePanel.CloseShop();
                _defenseEnded = latest.MatchEnded;
            }
            _waitingPanel?.Update(latest, _localId, _connection?.Server?.RewardsAvailable == true);
            _hud?.SetRewardStatusBounds(_defensePanel?.SummaryRectForVerification ??
                (_networkPanel.IsVisibleInTree() ? (Rect2?)_networkPanel.GetGlobalRect() : null));
            UpdateTouchVisibility();
            if (_practicePanel != null)
            { _practicePanel.Visible = !_paused && !_help.Visible; _practicePanel.Update(_aim); }
            UpdateTouchLayout();
            _touch?.Update(latest.Players.FirstOrDefault(p => p.Id == _localId), latest);
            UpdateAimMarker(latest);
            UpdateTutorial(latest);
            _verification?.AfterRender(latest);
        }
        else _renderer.Render(_backdropSnapshot ??= _backdrop.Snapshot(), 0, delta, false);
        _verification?.AfterFrame();
    }

    private void UpdateNetworkStats(double delta)
    {
        if (_networkStats == null) return;
        _networkStatsTime += delta;
        if (_networkStatsTime < 0.5) return;
        double elapsed = _networkStatsTime;
        _networkStatsTime = 0;
        double fps = Engine.GetFramesPerSecond();
        if (_connection is not { Connected: true } connection)
        {
            _networkStats.Text = $"FPS: {fps:F0}  |  Ping: —\nRed: {(_practice != null ? "local" : "sin conexión")}";
            _networkPanel.Visible = _snapshot?.Defense == null;
            _defensePanel?.SetNetworkStats(_networkStats.Text);
            _statsConnection = null;
            return;
        }
        long sent = connection.BytesSent, received = connection.BytesReceived;
        if (!ReferenceEquals(_statsConnection, connection))
        {
            _statsConnection = connection;
            _previousSent = sent;
            _previousReceived = received;
        }
        double sentRate = Math.Max(0, sent - _previousSent) / elapsed / 1024.0;
        double receivedRate = Math.Max(0, received - _previousReceived) / elapsed / 1024.0;
        _previousSent = sent;
        _previousReceived = received;
        connection.RequestPing();
        string ping = connection.PingMilliseconds is { } milliseconds ? $"{milliseconds:F0} ms" : "—";
        _networkStats.Text = $"FPS: {fps:F0}  |  Ping: {ping}\n↑ {sentRate:F1} KB/s  ↓ {receivedRate:F1} KB/s\nTotal: ↑ {sent / 1024.0:F1} KB  ↓ {received / 1024.0:F1} KB";
        _networkPanel.Visible = _snapshot?.Defense == null;
        _defensePanel?.SetNetworkStats(_networkStats.Text);
    }
}
