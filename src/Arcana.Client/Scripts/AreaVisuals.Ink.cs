using Arcana.Core;
using Godot;

namespace Arcana.Client;

internal static partial class AreaVisuals
{
    private sealed class Ink : IDisposable
    {
        private readonly SurfaceTool _surface = new();
        internal float Speed = 3.2f, RadialMotion;
        internal Ink() => _surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        private void Vertex(Vector3 point, Color color, float lift = 0, float coverage = 2)
        {
            _surface.SetNormal(Vector3.Up); _surface.SetColor(color);
            // [-1,1] suaviza los trazos; 2 conserva el relleno de discos y símbolos.
            _surface.SetUV(new(coverage, Speed)); _surface.SetUV2(new(lift, RadialMotion));
            _surface.AddVertex(point);
        }
        internal void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color, float lift = 0)
        { Vertex(a, color, lift); Vertex(b, color, lift); Vertex(c, color, lift); }
        private void FeatheredTriangle(Vector3 a, Vector3 b, Vector3 c, Color color, float lift)
        {
            Vertex(a, color, lift, -1);
            Vertex(b, color, lift, 0);
            Vertex(c, color, lift, 1);
        }
        internal void Disc(Color center, Color edge)
        {
            for (int i = 0; i < 48; i++)
            { Vertex(Vector3.Zero, center); Vertex(At(0.99f, i * Mathf.Tau / 48), edge); Vertex(At(0.99f, (i+1) * Mathf.Tau / 48), edge); }
        }
        internal void Ring(float inner, float outer, Color color) => Arc(inner, outer, 0, Mathf.Tau, color, 64);
        internal void Arc(float inner, float outer, float start, float end, Color color, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.Lerp(start, end, i / (float)segments), b = Mathf.Lerp(start, end, (i+1) / (float)segments);
                Vertex(At(inner, a), color, coverage: -1);
                Vertex(At(outer, a), color, coverage: 1);
                Vertex(At(outer, b), color, coverage: 1);
                Vertex(At(inner, a), color, coverage: -1);
                Vertex(At(outer, b), color, coverage: 1);
                Vertex(At(inner, b), color, coverage: -1);
            }
        }
        internal void Line(Vector3 a, Vector3 b, float width, Color color)
        {
            var side = new Vector3(-(b-a).Z, 0, (b-a).X).Normalized() * width * 0.5f;
            Vertex(a-side, color, coverage: -1);
            Vertex(a+side, color, coverage: 1);
            Vertex(b+side, color, coverage: 1);
            Vertex(a-side, color, coverage: -1);
            Vertex(b+side, color, coverage: 1);
            Vertex(b-side, color, coverage: -1);
        }
        internal void Cross(Vector3 at, float radius, Color color)
        {
            Line(at - Vector3.Right * radius, at + Vector3.Right * radius, radius * 0.6f, color);
            Line(at - Vector3.Back * radius, at + Vector3.Back * radius, radius * 0.6f, color);
        }
        internal void Flame(float angle, float radius, Color color)
        {
            var at = At(radius, angle); var side = At(0.035f, angle + Mathf.Pi / 2);
            FeatheredTriangle(at-side, at + Vector3.Up * 0.28f + At(0.025f, angle + 0.7f), at+side, color, 0.035f);
            FeatheredTriangle(at-side*0.45f, at + Vector3.Up * 0.17f, at+side*0.45f, new(Gold, 0.82f), 0.02f);
        }
        internal void Drop(Vector3 at, float angle, Color color)
        {
            var side = At(0.035f, angle + Mathf.Pi / 2); var tip = At(0.085f, angle);
            Triangle(at-side, at+tip, at+side, color);
            Triangle(at-side, at+side, at-tip*0.5f, color);
        }
        internal void Leaf(Vector3 at, float angle, Color color)
        {
            var tip = At(0.1f, angle); var side = At(0.04f, angle + Mathf.Pi / 2);
            Triangle(at-tip, at+side, at+tip, color); Triangle(at-tip, at+tip, at-side, color);
        }
        internal ArrayMesh Finish() => _surface.Commit();
        public void Dispose() => _surface.Dispose();
    }
}
