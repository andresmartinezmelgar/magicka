namespace Arcana.Core;

public sealed partial class GameWorld
{
    private const float MovementSkin = 0.006f;
    private readonly record struct MovementContact(float Time, V2 Normal, int Surface, V2 A, V2 B);

    private static void ResetSlide(Wizard player)
    {
        player.SlideSurface = 0;
        player.SlideSide = 0;
        player.SlideIntent = V2.Zero;
        player.SlideTangent = V2.Zero;
    }

    private void MoveWithCollisions(Wizard player, V2 delta, V2 walking)
    {
        var intent = walking.Normalized;
        bool assist = walking.LengthSquared > 0.0000001f;
        if (!assist || V2.Dot(intent, player.SlideIntent) < 0.85f) ResetSlide(player);
        player.SlideIntent = intent;
        bool touched = false;

        // Cada tramo se barre completo. El presupuesto restante solo disminuye,
        // incluso con empujes rápidos o varios contactos en la misma esquina.
        for (int iteration = 0; iteration < 5 && delta.LengthSquared > 0.0000001f; iteration++)
        {
            var from = player.Position;
            if (MovementHit(from, delta) is not MovementContact contact)
            {
                var to = from + delta;
                if (Walkable(to)) player.Position = to;
                break;
            }

            float travel = Math.Clamp(contact.Time - MovementSkin / delta.Length, 0, 1);
            var arrival = from + delta * travel;
            if (Walkable(arrival)) player.Position = arrival;
            else break;

            var remaining = delta * (1 - travel);
            walking *= 1 - travel;
            delta = remaining - contact.Normal * Math.Min(0, V2.Dot(remaining, contact.Normal));

            // La proyección física queda a cero en un choque frontal. Solo la
            // intención de caminar aporta un mínimo tangencial para iniciar el
            // rodeo; el empuje y un mago congelado no reciben esta asistencia.
            if (assist && V2.Dot(walking, contact.Normal) < -0.000001f)
            {
                touched = true;
                var tangent = contact.Normal.Perpendicular;
                int side = SlideSide(player, contact, tangent, intent);
                player.SlideSurface = contact.Surface;
                player.SlideSide = side;
                player.SlideTangent = tangent * side;
                float minimum = Math.Min(remaining.Length, walking.Length) * 0.65f;
                if (delta.Length < minimum) delta = tangent * (side * minimum);
            }
        }

        if (!touched) ResetSlide(player);
    }

    private int SlideSide(Wizard player, MovementContact contact, V2 tangent, V2 intent)
    {
        float along = V2.Dot(intent, tangent);
        float previousAlong = V2.Dot(player.SlideTangent, tangent);
        // Los muros pueden estar formados por varios segmentos contiguos. El
        // cambio de identificador no debe invertir el rodeo en cada unión.
        if (player.SlideSide != 0 && (player.SlideSurface == contact.Surface || Math.Abs(previousAlong) > 0.85f))
            return Math.Abs(along) > 0.3f ? Math.Sign(along) :
                player.SlideSurface == contact.Surface ? player.SlideSide : Math.Sign(previousAlong);
        if (Math.Abs(along) > 0.08f) return Math.Sign(along);

        // En una pared plana, toma el extremo próximo que tenga espacio. En el
        // centro exacto de un pilar, el desempate es estable, sin azar por tick.
        bool positive = ClearMovement(player.Position, player.Position + tangent * 0.4f);
        bool negative = ClearMovement(player.Position, player.Position - tangent * 0.4f);
        if (positive != negative) return positive ? 1 : -1;
        if (contact.A != contact.B)
        {
            var positiveEnd = V2.Dot(contact.B - contact.A, tangent) > 0 ? contact.B : contact.A;
            var negativeEnd = positiveEnd == contact.A ? contact.B : contact.A;
            float clearance = Geometry.DistanceToSegment(player.Position, contact.A, contact.B);
            bool positiveExit = ClearMovement(player.Position,
                positiveEnd + contact.Normal * clearance + tangent * 0.8f);
            bool negativeExit = ClearMovement(player.Position,
                negativeEnd + contact.Normal * clearance - tangent * 0.8f);
            if (positiveExit != negativeExit) return positiveExit ? 1 : -1;
            return V2.Distance(player.Position, positiveEnd) <= V2.Distance(player.Position, negativeEnd) ? 1 : -1;
        }
        return 1;
    }

    private MovementContact? MovementHit(V2 from, V2 delta)
    {
        var to = from + delta;
        MovementContact? closest = null;
        void Consider(float? time, int surface, V2 a, V2 b)
        {
            if (time is not float t || closest is MovementContact previous && t >= previous.Time) return;
            var at = V2.Lerp(from, to, t);
            var normal = (at - ClosestPoint(at, a, b)).Normalized;
            // Un contacto exacto permite salir o continuar en paralelo.
            if (V2.Dot(delta, normal) >= -0.000001f) return;
            closest = new(t, normal, surface, a, b);
        }

        for (int i = 0; i < Map.Boundary.Count; i++)
        {
            var edge = Map.Boundary[i];
            Consider(Geometry.SegmentCapsule(from, to, edge.A, edge.B, ArenaMap.WizardRadius),
                -i - 1, edge.A, edge.B);
        }
        for (int i = 0; i < Map.Obstacles.Count; i++)
        {
            var obstacle = Map.Obstacles[i];
            Consider(Geometry.SegmentCircle(from, to, obstacle.Position, obstacle.Radius + ArenaMap.WizardRadius),
                -Map.Boundary.Count - i - 1, obstacle.Position, obstacle.Position);
        }
        foreach (var wall in _walls)
            if (wall.Health > 0)
                Consider(Geometry.SegmentCapsule(from, to, wall.A, wall.B, ArenaMap.WizardRadius + 0.18f),
                    wall.Id, wall.A, wall.B);
        return closest;
    }

    private static V2 ClosestPoint(V2 point, V2 a, V2 b)
    {
        var segment = b - a;
        float along = segment.LengthSquared > 0 ? Math.Clamp(V2.Dot(point - a, segment) / segment.LengthSquared, 0, 1) : 0;
        return a + segment * along;
    }
}
