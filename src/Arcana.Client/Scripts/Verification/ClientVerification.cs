using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Coordinador de CI y capturas. Main lo crea únicamente con argumentos de verificación.</summary>
public partial class ClientVerification : Node
{
    private bool _smoke;
    private bool _diagnosticsSmoke;
    private bool _hostSmoke;
    private bool _holdPostedForVerification;
    private bool _hudSmoke;
    private bool _effectsSmoke, _previewEffects;
    private int _previewStrength;
    private string? _previewSpells;
    private bool _spritesSmoke;
    private bool _combinationsSmoke;
    private bool _mapsSmoke;
    private bool _practiceSmoke;
    private string? _previewPractice;
    private ArenaMapId? _previewMap;
    private string? _previewCombination;
    private CastMode _previewCastMode;
    private DemonstrationKind _previewInteraction;
    private static readonly (string Keys, CastMode Mode, DemonstrationKind Kind)[] CombinationCases =
    [
        ("D", CastMode.Forward, DemonstrationKind.Combination), ("DF", CastMode.Forward, DemonstrationKind.Combination),
        ("DQ", CastMode.Forward, DemonstrationKind.Combination), ("DR", CastMode.Forward, DemonstrationKind.Combination),
        ("EW", CastMode.Self, DemonstrationKind.Combination), ("ES", CastMode.Forward, DemonstrationKind.Combination),
        ("EW", CastMode.Forward, DemonstrationKind.Combination), ("SS", CastMode.Forward, DemonstrationKind.CrossedBeams),
        ("SS", CastMode.Forward, DemonstrationKind.ShieldRefraction), ("SS", CastMode.Forward, DemonstrationKind.Capture)
    ];
    private string? _previewWizards;
    private readonly HashSet<(WizardType Type, int Team, int Row, int Pose)> _spriteCoverage = [];
    private int _previewHelp = -1;
    private string? _previewShield;
    private static readonly (string State, float Shield, float Maximum)[] HudCases =
        [("none", 0, 420), ("full", 420, 420), ("half", 210, 420), ("low", 40, 420), ("ward", 90, 180), ("stone", 325, 650)];
    private int _smokeFrames, _renderFrames;
    private string? _capturePath;
    private bool _captureReview, _mapOverview;
    private bool _smokeFailed;
    private bool SmokeMode => _smoke || _hostSmoke || _diagnosticsSmoke || _hudSmoke || _effectsSmoke || _spritesSmoke || _combinationsSmoke || _mapsSmoke || _mobileSmoke || _practiceSmoke || _loadingSmoke || _lobbySmoke != null;

    internal bool CheckSmokeErrors()
    {
        if (_smokeFailed) return true;
        if (!SmokeMode || _runtime.EngineLog.ErrorCount == 0) return false;
        _smokeFailed = true;
        GD.PushError($"SMOKE_FAILED: {_runtime.EngineLog.ErrorCount} errores del motor o de scripts.");
        GetTree().Quit(1);
        return true;
    }
    private void CompleteSmoke(string message)
    {
        if (CheckSmokeErrors()) return;
        GD.Print(message); GetTree().Quit();
    }

    private Main.VerificationAccess _runtime = null!;
    internal bool HoldPosted => _holdPostedForVerification;
    internal bool DiagnosticsSmoke => _diagnosticsSmoke;
    internal bool FixedRenderStep => _effectsSmoke || _spritesSmoke || _previewStrength > 0 || _previewSpells != null;
    internal bool FreezeSimulation => _hudSmoke || _previewShield != null || _effectsSmoke || _previewEffects || _previewStrength > 0 ||
        _previewSpells != null || _spritesSmoke || _previewWizards != null || _combinationsSmoke || _mapsSmoke || _previewMap != null;

    internal static bool Requested(string[] arguments) => arguments.Any(a => a is "--smoke" or "--host-smoke" or "--diagnostics-smoke" or
        "--hud-smoke" or "--effects-smoke" or "--preview-effects" or "--sprites-smoke" or "--combinations-smoke" or "--maps-smoke" or
        "--mobile-smoke" or "--mobile-fixture" or "--practice-smoke" or "--loading-smoke" or "--preview-loading" or "--preview-wizards" or "--capture-review" or "--map-overview" ||
        a.StartsWith("--preview-") || a.StartsWith("--capture=") || a.StartsWith("--lobby-smoke="));

    internal void Initialize(Main.VerificationAccess runtime, string[] arguments)
    {
        _runtime = runtime;
        foreach (string arg in arguments)
        {
            if (arg == "--smoke") _smoke = true;
            if (arg == "--host-smoke") _hostSmoke = true;
            if (arg == "--diagnostics-smoke") _diagnosticsSmoke = true;
            if (arg == "--hud-smoke") _hudSmoke = true;
            if (arg == "--effects-smoke") _effectsSmoke = true;
            if (arg == "--preview-effects") _previewEffects = true;
            if (arg.StartsWith("--preview-strength=") && int.TryParse(arg[19..], out int strength)) _previewStrength = Math.Clamp(strength, 1, 3);
            if (arg is "--preview-spells=barriers" or "--preview-spells=beams" or "--preview-spells=beam-charge"
                or "--preview-spells=shield-elements" or "--preview-spells=shield-strength" or "--preview-spells=meteor"
                or "--preview-spells=ground-contrast" or "--preview-spells=ground-contrast-stone"
                or "--preview-spells=stone" or "--preview-spells=stone-close") _previewSpells = arg[17..];
            if (arg == "--sprites-smoke") _spritesSmoke = true;
            if (arg == "--combinations-smoke") _combinationsSmoke = true;
            if (arg == "--maps-smoke") _mapsSmoke = true;
            if (arg == "--practice-smoke") _practiceSmoke = true;
            if (arg.StartsWith("--preview-practice=")) _previewPractice = arg[19..];
            if (arg == "--mobile-smoke") _mobileSmoke = true;
            if (arg == "--loading-smoke") _loadingSmoke = true;
            if (arg == "--preview-loading") _previewLoading = true;
            if (arg.StartsWith("--lobby-smoke=")) _lobbySmoke = arg[14..];
            if (arg.StartsWith("--preview-rooms=")) _previewRooms = arg[16..];
            if (arg.StartsWith("--preview-map=") && Enum.TryParse<ArenaMapId>(arg[14..], true, out var map) && Enum.IsDefined(map)) _previewMap = map;
            if (arg.StartsWith("--preview-combination=")) _previewCombination = arg[22..];
            if (arg.StartsWith("--preview-cast=") && Enum.TryParse<CastMode>(arg[15..], true, out var castMode)) _previewCastMode = castMode;
            if (arg.StartsWith("--preview-interaction=") && Enum.TryParse<DemonstrationKind>(arg[22..], true, out var interaction)) _previewInteraction = interaction;
            if (arg == "--preview-wizards") _previewWizards = "front";
            if (arg.StartsWith("--preview-wizards=")) _previewWizards = arg[18..];
            if (arg.StartsWith("--preview-help=") && int.TryParse(arg[15..], out int page)) _previewHelp = Math.Clamp(page, 0, _runtime.HelpPages.GetTabCount() - 1);
            if (arg.StartsWith("--preview-shield=")) _previewShield = arg[17..];
            if (arg.StartsWith("--capture=")) _capturePath = arg[10..];
            if (arg == "--capture-review") _captureReview = true;
            if (arg == "--map-overview") _mapOverview = true;
        }
        if (_lobbySmoke != null || _previewRooms != null)
        { _runtime.Launcher.BrowseAddress(_lobbySmoke ?? _previewRooms!, true); return; }
        if (_hostSmoke) _ = HostedSmoke();
        if (_loadingSmoke || _previewLoading)
            _runtime.StartPractice("Carga móvil", 1, GameMode.Warfare, 2, ArenaMapId.Citadel);
        if (_practiceSmoke || _previewPractice != null)
        {
            _runtime.StartPractice("Mago de prácticas", 0, GameMode.Warfare, 0, ArenaMapId.Practice);
            if (_previewPractice != null)
                _runtime.PracticeSession!.Player.Position = _previewPractice == "states" ? new(0, -31)
                    : _previewPractice == "barriers" ? new(-14, 35) : PracticeLayout.CasterPosition + new V2(3, 0);
        }
        if (_mapsSmoke || _previewMap != null) { _runtime.LocalId = 1; _runtime.StartGame(MapFixture(_previewMap ?? ArenaMapId.Citadel)); }
        if (_hudSmoke || _previewShield != null || _effectsSmoke || _previewEffects || _spritesSmoke || _previewWizards != null)
        {
            _runtime.Practice = null; _runtime.LocalId = 1; _runtime.ServerName = "Práctica · Colores y escudo";
            if (_spritesSmoke || _previewWizards != null)
            {
                _runtime.LocalId = 3; _runtime.ServerName = "Práctica · Galería de magos";
                _runtime.StartGame(VisualFixture.Wizards(0, _previewWizards));
            }
            else _runtime.StartGame(VisualFixture.Create(_previewShield ?? "none"));
        }
        if (_previewStrength > 0)
        {
            _runtime.Practice = null; _runtime.LocalId = 1; _runtime.StartGame(VisualFixture.Strength(0, _previewStrength));
            _runtime.Hud!.Visible = false;
            _runtime.Renderer.SetPreviewZoom(24); _runtime.Renderer.ShowControlPoints = false;
            _runtime.UiRoot.AddChild(Ui.Label($"{_previewStrength} {(_previewStrength == 1 ? "carga" : "cargas")}", 28));
        }
        if (_previewSpells != null)
        {
            _runtime.Practice = null; _runtime.LocalId = _previewSpells == "stone" ? 2 : 1;
            _runtime.StartGame(VisualFixture.Spells(0, _previewSpells));
            _runtime.Hud!.Visible = false; _runtime.Renderer.ShowControlPoints = _previewSpells.StartsWith("ground-contrast"); _runtime.Renderer.SetPreviewZoom(24);
            if (_previewSpells == "ground-contrast-stone") _runtime.Renderer.SetPreviewZoom(32);
            if (_previewSpells.StartsWith("stone"))
            {
                _runtime.Renderer.SetPreviewZoom(_previewSpells == "stone-close" ? 10 : 22);
                var legend = new HBoxContainer { Position = new(24, 24) };
                legend.AddThemeConstantOverride("separation", 12);
                legend.AddChild(new ElementIcon { Value = Element.Earth, CustomMinimumSize = new(40, 40) });
                legend.AddChild(Ui.Label("TIERRA · roca, armadura y fragmentos", Ui.IsMobile ? 20 : 24));
                _runtime.UiRoot.AddChild(legend);
            }
            if (_previewSpells == "shield-strength")
            {
                var legend = Ui.Label("ARRIBA: E · E+Q · E+Q+Q\nABAJO: E+D · E+D+D · E+D+F", 20);
                legend.Position = new(24, 24); _runtime.UiRoot.AddChild(legend);
            }
            GD.Print($"SPELL_PREVIEW_READY: {_previewSpells}");
        }
        if (_previewHelp >= 0) { _runtime.ToggleHelp(); _runtime.HelpPages.CurrentTab = _previewHelp; }
        if (_combinationsSmoke || _previewCombination != null || _previewInteraction != DemonstrationKind.Combination)
        {
            _runtime.Help.Visible = true; _runtime.HelpPages.CurrentTab = _runtime.HelpPages.GetTabIdxFromControl(_runtime.SpellBook);
            _runtime.SpellBook.FixedStepForVerification = _combinationsSmoke || _capturePath != null;
            _runtime.SpellBook.SelectCombination(_previewCombination ?? "SS", _previewCastMode, _previewInteraction);
        }
        if (_smoke)
        {
            _runtime.StartPractice("Aprendiz", 0, GameMode.Warfare, 3);
            var players = _runtime.Practice!.Players.Values.ToArray();
            players[0].Position = new(-4, 0); players[1].Position = new(4, 0);
            players[2].Position = new(0, -4); players[3].Position = new(3, 4);
        }
        if (_mobileSmoke || arguments.Contains("--mobile-fixture"))
        {
            _runtime.StartPractice("Mago móvil", 0, GameMode.Warfare, 1);
            _runtime.Practice!.Players[_runtime.LocalId].Position = V2.Zero;
        }
    }
    private async Task HostedSmoke()
    {
        try
        {
            var room = await _runtime.Launcher.StartSmokeRoom();
            _holdPostedForVerification = true;
            var canceled = _runtime.ConnectOnline("127.0.0.1:29025", "Cancelado", -1, "", room.Id);
            _runtime.CancelPendingConnection(); await canceled;
            await _runtime.ConnectOnline("127.0.0.1:29025", "Respuesta tardía", -1, "", room.Id);
            _runtime.ReturnToLauncher();
            _runtime.DrainPosted();
            if (_runtime.Connection != null || _runtime.Playing) throw new InvalidOperationException("Se entró en una conexión cancelada.");
            await _runtime.ConnectOnline("127.0.0.1:29025", "Anterior", -1, "", room.Id);
            var previous = _runtime.PendingConnection!;
            await _runtime.ConnectOnline("127.0.0.1:29025", "Launcher", -1, "", room.Id);
            var current = _runtime.PendingConnection;
            _runtime.DrainPosted();
            if (current == null || _runtime.Connection != current || !_runtime.Loading || _runtime.Playing)
                throw new InvalidOperationException("Una respuesta anterior sustituyó la conexión actual.");
            await previous.DisposeAsync();
            GD.Print("CONNECTION_LIFECYCLE_OK: cancelación antes del saludo, respuesta tardía y sustitución de conexión.");
        }
        catch (Exception e) { _runtime.Post(() => { GD.PushError("HOST_SMOKE_FAILED: " + e.Message); GetTree().Quit(1); }); }
        finally { _holdPostedForVerification = false; }
    }

    internal bool FailFrame(Exception error)
    {
        if (!SmokeMode) return false;
        _smokeFailed = true; GD.PushError("SMOKE_FAILED: " + error.Message); GetTree().Quit(1); return true;
    }
    internal void BeforeFrame(double delta)
    {
        _renderFrames++;
        if (_lobbySmoke != null || _previewRooms != null) CheckLobbyFrame();
        if (_mapOverview && _previewMap != null && _renderFrames == 2)
        {
            var overview = ArenaMap.Get(_previewMap.Value);
            _runtime.Renderer.SetPreviewZoom(Math.Max(overview.HalfWidth * 2 / (GetViewport().GetVisibleRect().Size.X / GetViewport().GetVisibleRect().Size.Y),
                overview.HalfHeight * 1.58f) + 6);
        }
        if (_mapsSmoke && _renderFrames <= 210 && _renderFrames % 30 == 1)
            _runtime.Snapshot = MapFixture(_renderFrames <= 30 || _renderFrames > 180 ? ArenaMapId.Citadel
                : _renderFrames <= 60 ? ArenaMapId.Classic : _renderFrames <= 90 ? ArenaMapId.Ascension
                : _renderFrames <= 120 ? ArenaMapId.Relics : _renderFrames <= 150 ? ArenaMapId.Frontline : ArenaMapId.Practice);
        if (_mapsSmoke && _renderFrames == 76)
            _runtime.Snapshot = _runtime.Snapshot! with { Pickups = _runtime.Snapshot!.Pickups!.Select(i => i.Id == 1 ? i with { Position = new V2(0, 20) } : i).ToArray() };
        if (_combinationsSmoke) CheckCombinations();
        if (_effectsSmoke || _previewEffects) _runtime.Snapshot = VisualFixture.Effects(_renderFrames);
        if (_previewStrength > 0) _runtime.Snapshot = VisualFixture.Strength(_renderFrames, _previewStrength);
        if (_previewSpells != null) _runtime.Snapshot = VisualFixture.Spells(_renderFrames, _previewSpells);
        if (_spritesSmoke || _previewWizards != null) _runtime.Snapshot = VisualFixture.Wizards(_renderFrames, _previewWizards);
        if (_hudSmoke && _renderFrames <= HudCases.Length * 3 && _renderFrames % 3 == 1)
        {
            _runtime.Snapshot = VisualFixture.Create(HudCases[(_renderFrames - 1) / 3].State);
            _runtime.Snapshot = _runtime.Snapshot with { Players = _runtime.Snapshot.Players.Select(p => p with { Charge = 0.5f, Casting = false }).ToArray() };
        }
    }
    internal void AfterRender(WorldSnapshot latest)
    {
        if (_mapsSmoke && _renderFrames % 30 == 0) CheckMaps(latest);
        if (_previewMap != null && !_mapOverview && _renderFrames == 1) _runtime.Renderer.SetPreviewZoom(72);
        if (_hudSmoke && _renderFrames <= HudCases.Length * 3 && _renderFrames % 3 == 2) CheckHudCase();
        if (_capturePath != null && _previewShield != null && _renderFrames == 75)
        {
            // Prueba visual real del HUD de Windows antes de capturar el framebuffer.
            var hud = _runtime.Hud ?? throw new InvalidOperationException("HUD no inicializado.");
            Rect2 screen = GetViewport().GetVisibleRect();
            foreach (var name in new[] { "PlayerStatusPanel", "SpellControlsPanel", "MiniMapPanel", "MatchObjective" })
            {
                if (hud.FindChild(name, true, false) is not Control panel)
                    throw new InvalidOperationException("Falta el panel de escritorio: " + name);
                if (panel.IsVisibleInTree() && !screen.Encloses(panel.GetGlobalRect()))
                    throw new InvalidOperationException($"Desktop HUD: {name} desborda {screen}: {panel.GetGlobalRect()}");
            }
            var health = (Control)hud.FindChild("PlayerStatusPanel", true, false);
            var map = (Control)hud.FindChild("MiniMapPanel", true, false);
            if (map.IsVisibleInTree() && health.GetGlobalRect().Intersects(map.GetGlobalRect()))
                throw new InvalidOperationException("Desktop HUD: minimapa y vida superpuestos.");
            GD.Print("DESKTOP_HUD_LAYOUT_OK:" + screen);
        }
        if (_effectsSmoke) CheckEffects();
        if (_spritesSmoke) CheckSprites(latest);
        if (_practiceSmoke) CheckPractice(latest);
    }
    internal void AfterFrame()
    {
        if (_diagnosticsSmoke && _renderFrames == 30) _runtime.ExportDiagnostics();
        if (_capturePath != null && _renderFrames == 90) _ = Capture(_capturePath);
    }
    internal void AfterPhysics()
    {
        if (_practiceSmoke) StepPracticeSmoke();
        if (_smoke)
        {
            _smokeFrames++;
            if (_smokeFrames == 30)
            {
                _runtime.Aim = new(5, 0);
                _runtime.Submit(new() { Type = "input", AimX = 5, AimY = 0 });
                foreach (var e in new[] { Element.Arcane, Element.Arcane, Element.Fire }) _runtime.Submit(new() { Type = "element", Element = e });
                _runtime.Submit(new() { Type = "cast" });
            }
            if (_smokeFrames == 140) { _runtime.Submit(new() { Type = "release" }); _runtime.Submit(new() { Type = "element", Element = Element.Shield }); _runtime.Submit(new() { Type = "self" }); }
            if (_smokeFrames >= 240 && _capturePath == null)
            CompleteSmoke($"SMOKE_OK: {_runtime.Snapshot?.Players.Length} jugadores, tick {_runtime.Snapshot?.Tick}");
        }
        if (_hostSmoke && _runtime.Connection?.Connected == true && ++_smokeFrames >= 180)
        CompleteSmoke($"HOST_SMOKE_OK: {_runtime.Connection.Room?.Name}, {_runtime.Snapshot?.Players.Length} jugadores");
        if (_mobileSmoke && _renderFrames >= 2) CheckMobile();
    }
    internal void BeforeDiagnosticExport()
    { if (_diagnosticsSmoke) GD.PushWarning("DIAGNOSTICS_SMOKE_MARKER: aviso de prueba controlado."); }
    internal async Task CheckDiagnosticReport(string path)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        bool marker = false;
        foreach (var entry in zip.Entries.Where(e => e.Name.StartsWith("client-") && e.Name.EndsWith(".jsonl")))
        {
            using var reader = new StreamReader(entry.Open());
            if ((await reader.ReadToEndAsync().ConfigureAwait(false)).Contains("DIAGNOSTICS_SMOKE_MARKER")) marker = true;
        }
        if (!marker || zip.GetEntry("report.json") == null) throw new IOException("El informe no incluye el aviso del motor o los metadatos.");
        _runtime.Post(() => CompleteSmoke("DIAGNOSTICS_SMOKE_OK: " + path));
    }
    private async Task Capture(string path)
    {
        if (DisplayServer.GetName() == "headless") { GD.Print("CAPTURE_SKIPPED: headless"); GetTree().Quit(); return; }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var captured = GetViewport().GetTexture().GetImage(); var error = captured.SavePng(path);
        if (_captureReview)
        {
            captured.Resize(_previewStrength > 0 ? 960 : 640, _previewStrength > 0 ? 600 : 400, Image.Interpolation.Lanczos);
            string marker = _previewStrength > 0 ? "EFFECT_REVIEW_BASE64:" + _previewStrength : "MAP_REVIEW_BASE64:" + _previewMap;
            GD.Print(marker + ":" + Convert.ToBase64String(captured.SaveJpgToBuffer(0.8f)));
        }
        GD.Print($"CAPTURE: {error} {path}"); GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
}
