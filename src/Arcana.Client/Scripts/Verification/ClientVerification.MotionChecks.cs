using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private bool CheckWizardFacing()
    {
        void Require(bool condition, string detail)
        { if (!condition) throw new InvalidDataException("Orientación: " + detail); }
        foreach (int fps in new[] { 30, 60, 120 })
        foreach (double delay in new[] { 1 / 60.0, 0.075 })
        {
            var player = VisualFixture.Create("none").Players[0] with
            { Position = V2.Zero, Facing = new(-1, 0), Casting = false, Frozen = 0, Alive = true };
            var wizard = new WizardVisual(); _runtime.Renderer.AddChild(wizard); wizard.Build(player, false);
            float dt = 1f / fps; double time = 0;
            void Render(V2 movement, bool casting = false)
            {
                time += dt;
                player = player with { Position = player.Position + movement * (6.2f * dt), Casting = casting };
                wizard.Update(player, dt, (float)time, _runtime.Renderer.Camera, time, delay);
            }
            try
            {
                wizard.Update(player, 0, 0, _runtime.Renderer.Camera, 0, delay);
                foreach (var (direction, row) in new (V2, int)[]
                    { (new(1, 0), 3), (new(-1, 0), 1), (new(0, -1), 2), (new(0, 1), 0) })
                {
                    player = player with { Facing = direction * -1 };
                    var poses = new HashSet<int>();
                    for (int frame = 0; frame < fps; frame++)
                    {
                        Render(direction);
                        if (frame < fps / 4) continue;
                        Require(wizard.Sprite.Frame / 4 == row, $"caminar y apuntar en sentidos opuestos a {fps} FPS.");
                        poses.Add(wizard.Sprite.Frame % 4);
                    }
                    Require(poses.SetEquals(new[] { 0, 1 }), "la marcha no alterna sus dos pasos.");
                }
                player = player with { Facing = new(-1, 0) };
                for (int frame = 0; frame < fps / 2; frame++)
                {
                    Render(new(1, 0), casting: true);
                    Require(wizard.Sprite.Frame / 4 == 1 && wizard.Sprite.Frame % 4 == 3,
                        "el lanzamiento deja de mirar al objetivo al desplazarse.");
                }
                Render(new(1, 0));
                Require(wizard.Sprite.Frame / 4 == 3 && wizard.Sprite.Frame % 4 < 2,
                    "soltar el hechizo no devuelve la orientación y los pasos de marcha.");
                wizard.PlayCastPose([Element.Earth]);
                for (int frame = 0; frame < fps / 8; frame++)
                {
                    Render(new(1, 0));
                    Require(wizard.Sprite.Frame / 4 == 1 && wizard.Sprite.Frame % 4 == 3,
                        "el lanzamiento instantáneo pierde su orientación o su pose.");
                }
                for (int frame = 0; frame < fps / 2; frame++) Render(new(1, 0));
                Require(wizard.Sprite.Frame / 4 == 3 && wizard.Sprite.Frame % 4 < 2,
                    "la pose instantánea deja al mago mirando en la dirección anterior.");
                for (int frame = 0; frame < fps / 2; frame++) Render(V2.Zero);
                Require(wizard.Sprite.Frame % 4 == 0, "la animación sigue caminando después de parar.");
            }
            finally { wizard.QueueFree(); }
        }
        GD.Print("FACING_OK: cuatro direcciones, 30/60/120 FPS, práctica y red, apuntado opuesto, marcha, lanzamiento, soltar, pose instantánea y parada");
        return true;
    }

    private bool CheckWizardMotion()
    {
        void Require(bool condition, string detail)
        { if (!condition) throw new InvalidDataException("Movimiento: " + detail); }
        foreach (int fps in new[] { 30, 60, 120 })
        foreach (float speed in new[] { 3.1f, 6.2f, 10.23f })
        foreach (bool jitter in new[] { false, true })
        {
            var motion = new WizardMotion(); motion.Advance(V2.Zero, 0, 0, 0.075);
            int packet = 0;
            for (int frame = 1; frame <= fps * 3; frame++)
            {
                double now = frame / (double)fps;
                double Arrival(int next) => next * 0.05 + (jitter ? Math.Sin(next * 2.17) * 0.015 : 0);
                while (Arrival(packet + 1) <= now + 0.000001) packet++;
                double sample = packet * 0.05;
                var target = new V2(speed * (float)Math.Min(sample, 2), 0);
                var before = motion.Position;
                motion.Advance(target, sample, 1f / fps, 0.075);
                Require(!motion.Moving || motion.Direction == new V2(1, 0), "dirección de marcha distinta del recorrido confirmado.");
                Require(motion.Position.X >= before.X - 0.00001f && motion.Position.X <= target.X + 0.00001f,
                    $"retroceso o extrapolación a {fps} FPS.");
                if (now is > 0.2 and < 1.8)
                    Require(Math.Abs(motion.DistanceMoved * fps - speed) < 0.003f, $"velocidad irregular a {fps} FPS.");
                if (frame == fps * 3 / 2)
                {
                    float expected = speed * (1.5f - 0.075f);
                    Require(Math.Abs(motion.Position.X - expected) < 0.002f, "dependencia de la tasa de fotogramas.");
                    Require(Math.Abs(motion.WalkCycle - expected / 1.55f % 1) < 0.002f, "pasos desligados de la distancia.");
                }
                if (now > 2.12) Require(!motion.Moving && motion.WalkCycle == 0 && motion.Direction == V2.Zero,
                    "pasos o deslizamiento después de parar.");
            }
            // Falta prolongada de paquetes: permanecer en la última posición confirmada.
            for (int i = 0; i < fps; i++) motion.Advance(motion.Position, packet * 0.05, 1f / fps, 0.075);
            Require(!motion.Moving, "movimiento inventado sin paquetes.");
            motion.Advance(new(-10, 3), 4, 1f / fps, 0.075);
            Require(motion.Position == new V2(-10, 3) && !motion.Moving && motion.Direction == V2.Zero, "teletransporte interpolado.");
            motion.Advance(new(-9, 3), 4.05, 1f / fps, 0.075, snap: true);
            Require(motion.Position == new V2(-9, 3) && motion.WalkCycle == 0, "reaparición cercana o congelación interpolada.");
            motion.Advance(new(1, 1), 0, 1f / fps, 0.075);
            Require(motion.Position == new V2(1, 1), "reinicio del reloj arrastra la posición anterior.");
            for (int i = 0; i < fps; i++) motion.Advance(new(1, 1), 0, 1f / fps, 0.075);
            motion.Advance(new(1.1f, 1), 0.05, 1f / fps, 0.075);
            Require(motion.Position == new V2(1.1f, 1) && !motion.Moving, "reanudar una práctica conserva el reloj adelantado.");
        }
        // Giro junto a una esquina: la representación permanece dentro del recorrido confirmado.
        var corner = new WizardMotion(); corner.Advance(V2.Zero, 0, 0, 0.075);
        for (int frame = 1; frame <= 120; frame++)
        {
            double sample = Math.Floor(frame / 3.0) * 0.05;
            var target = sample <= 1 ? new V2((float)sample, 0) : new V2(1, (float)sample - 1);
            corner.Advance(target, sample, 1 / 60f, 0.075);
            Require(corner.Position.X <= 1.00001f && (corner.Position.X >= 0.99999f || corner.Position.Y == 0), "atajo a través de una esquina.");
        }
        GD.Print("MOTION_OK: 30/60/120 FPS, muestras a 20 Hz con fluctuación, lentitud y celeridad, pasos por distancia, parada, pérdida de paquetes, giro, congelación y reaparición");
        return true;
    }

    private bool CheckMobilePalette()
    {
        float Luminance(Color color)
        {
            float Linear(float value) => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
            return Linear(color.R) * 0.2126f + Linear(color.G) * 0.7152f + Linear(color.B) * 0.0722f;
        }
        float background = Luminance(new("0e1724"));
        foreach (var element in Enum.GetValues<Element>())
        {
            var color = Ui.ElementColor(element, true);
            if ((Luminance(color) + 0.05f) / (background + 0.05f) < 4.5f || color == Ui.ElementColor(element, false)) return false;
        }
        Color water = Ui.ElementColor(Element.Water), cold = Ui.ElementColor(Element.Cold);
        Color lightning = Ui.ElementColor(Element.Lightning), arcane = Ui.ElementColor(Element.Arcane);
        if (Math.Abs(water.H - cold.H) < 0.075f || Math.Abs(lightning.H - arcane.H) < 0.1f) return false;
        GD.Print("MOBILE_COLORS_OK: ocho tonos, contraste de texto superior a 4,5:1 sobre el fondo de los controles, agua/frío y rayo/arcano distintos");
        return true;
    }
}
