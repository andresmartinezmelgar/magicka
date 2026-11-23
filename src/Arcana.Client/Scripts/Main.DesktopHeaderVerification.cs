using Godot;
using Arcana.Core;
namespace Arcana.Client;

public partial class Main
{
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
                _desktopHeaderSmoke = false;
                CompleteMenuSmoke("DESKTOP_HEADER_LAYOUT_OK");
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
}
