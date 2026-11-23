using System.Net.Http;
using Godot;
using Arcana.Core;

namespace Arcana.Client;

public partial class ReleaseUpdateNotice
{
    private static string Size(long bytes)
    {
        if (bytes < 0) return "Tamaño por determinar";
        double value = bytes;
        string[] units = ["B", "KB", "MB", "GB"];
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{bytes} B" : $"{value:0.0} {units[unit]}";
    }

    private static PanelContainer Card()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Ui.ModalPanel());
        return panel;
    }

    private static void AddText(VBoxContainer parent, ElementTextLabel label)
    {
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        parent.AddChild(label);
    }

    private void ShowNotice(string version, string download, long size,
        string checksumUrl = "", string assetName = "")
    {
        if (!IsInsideTree() || _stop.IsCancellationRequested || _overlay != null) return;
        _version = version;
        _downloadUrl = download;
        _checksumUrl = checksumUrl;
        _assetName = assetName;
        _expectedBytes = size;
        _downloading = _completed = _installing = _waitingToInstall = _windowsDelta = false;
        _downloadPath = _expectedHash = "";
        _canvas = new CanvasLayer { Layer = 90 };
        AddChild(_canvas);
        // Ocultar mientras se construye: un fallo intermedio nunca deja un fondo
        // oscuro bloqueando el menú sin botones utilizables.
        _overlay = new Control
        {
            Name = "ReleaseUpdateOverlay",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Theme = Ui.Theme()
        };
        _canvas.AddChild(_overlay);
        _overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var dim = new ColorRect { Color = Ui.ModalDim, MouseFilter = Control.MouseFilterEnum.Stop };
        _overlay.AddChild(dim);
        Ui.Full(dim);

        var center = new CenterContainer { Name = "CenterContainer", MouseFilter = Control.MouseFilterEnum.Stop };
        _overlay.AddChild(center);
        Ui.Full(center);

        // El antiguo ScrollContainer era hijo directo del CenterContainer y su
        // altura mínima de 0 podía contraerse a cero: solo quedaba el fondo negro.
        // Ahora la tarjeta tiene medidas acotadas por la pantalla y las acciones
        // están FUERA del scroll, siempre visibles y pulsables.
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float width = Math.Max(240, Math.Min(Ui.IsMobile ? 710 : 680, viewport.X - 24));
        float height = Math.Max(210, Math.Min(Ui.IsMobile ? 570 : 550, viewport.Y - 24));
        var card = Card();
        card.Name = "ReleaseUpdateCard";
        card.CustomMinimumSize = new Vector2(width, height);
        _responsiveCard = card;
        center.AddChild(card);

        var layout = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        layout.AddThemeConstantOverride("separation", 10);
        card.AddChild(layout);
        var scroll = new ContentScrollContainer
        {
            Name = "ReleaseUpdateDetails",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto
        };
        layout.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", Ui.IsMobile ? 15 : 12);
        scroll.AddChild(body);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 12);
        body.AddChild(top);
        top.AddChild(Ui.Label("✦", Ui.IsMobile ? 34 : 30, Ui.Gold));
        var topCopy = new VBoxContainer();
        topCopy.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.AddChild(topCopy);
        AddText(topCopy, Ui.Label("ARCANA ARENA", 15, Ui.Gold));
        _headline = Ui.Label(_activePlan?.Preview == true ? "Versión de prueba disponible" : "Actualización disponible", Ui.IsMobile ? 24 : 25, Ui.Text);
        AddText(topCopy, _headline);

        _subtitle = Ui.Label(_activePlan?.Differential == true ? "Se descargarán solo los cambios compatibles con tu versión cuando lo elijas." :
            _activePlan?.Preview == true ? "Rama: " + _activePlan.SourceBranch + ". Elige cuándo descargar esta prueba."
                : "Elige si quieres descargar la nueva versión ahora o seguir jugando.", 16, Ui.Muted);
        AddText(body, _subtitle);

        var versions = new HBoxContainer();
        versions.AddThemeConstantOverride("separation", 12);
        body.AddChild(versions);
        var installed = Ui.Label("INSTALADA\n" + BuildInfo.Version, 16, Ui.Muted);
        installed.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        versions.AddChild(installed);
        versions.AddChild(Ui.Label("➜", 24, Ui.Gold));
        var available = Ui.Label("DISPONIBLE\n" + version + (_activePlan?.Preview == true ? $" · prueba #{_activePlan.BuildNumber}" : ""), 16, Ui.Gold);
        available.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        versions.AddChild(available);

        AddText(body, Ui.Label("NOVEDADES DE ESTA VERSIÓN", 17, Ui.Gold));
        _changes = Ui.Label(_activePlan?.Changes is { Length: > 0 } changes
            ? string.Join("\n", changes.Select(c => "• " + c))
            : "Esta publicación no incluye un resumen de cambios.", 16, Ui.Text);
        _changes.Name = "ReleaseChangeSummary";
        AddText(body, _changes);

        var rule = new HSeparator();
        body.AddChild(rule);
        _sizeLabel = Ui.Label(size >= 0 ? "Tamaño de descarga  ·  " + Size(size) : "Tamaño de descarga  ·  Por determinar", 17, Ui.Text);
        AddText(body, _sizeLabel);

        var progressHeader = new HBoxContainer();
        body.AddChild(progressHeader);
        _progressLabel = Ui.Label(size >= 0 ? "0 B de " + Size(size) : "Preparado para descargar", 16, Ui.Muted);
        _progressLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        progressHeader.AddChild(_progressLabel);
        _percentLabel = Ui.Label("0 %", 17, Ui.Gold);
        progressHeader.AddChild(_percentLabel);

        _progress = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 0, ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, Ui.IsMobile ? 20 : 16) };
        body.AddChild(_progress);
        _speedLabel = Ui.Label("La descarga se realizará desde GitHub Releases.", 15, Ui.Muted);
        AddText(body, _speedLabel);

        _note = Ui.Label(_android
            ? "Se instalará automáticamente cuando Android lo permita. El sistema puede solicitar autorización."
            : "La actualización se instalará y el juego se reiniciará automáticamente.", 15, Ui.Muted);
        AddText(body, _note);

        layout.AddChild(new HSeparator());
        var actions = new GridContainer { Columns = width < 600 ? 1 : 2 };
        _responsiveButtons = actions;
        actions.Name = "ReleaseUpdateActions";
        actions.AddThemeConstantOverride("separation", 12);
        actions.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        layout.AddChild(actions);
        _secondary = Ui.Button("Ahora no", Close);
        _secondary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        actions.AddChild(_secondary);
        _primary = Ui.Button("Descargar actualización", PrimaryAction, true);
        _primary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        actions.AddChild(_primary);
        ShowSavedDownload();
        ResizeUpdateCard();
        _overlay.Visible = true;
        string? previousFailure = OS.HasFeature("windows") ? WindowsUpdateInstaller.PreviousFailure(
            _updatesFolder, _version) : null;
        if (previousFailure != null) ShowFailure(_progressGeneration, previousFailure);
    }

    private void ResizeUpdateCard()
    {
        if (_responsiveCard == null || !GodotObject.IsInstanceValid(_responsiveCard)) return;
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        float width = Math.Max(210, Math.Min(Ui.IsMobile ? 710 : 680, screen.X - 24));
        float height = Math.Max(180, Math.Min(Ui.IsMobile ? 570 : 550, screen.Y - 24));
        _responsiveCard.CustomMinimumSize = new Vector2(width, height);
        if (_responsiveButtons != null) _responsiveButtons.Columns = width < 600 ? 1 : 2;
    }
}
