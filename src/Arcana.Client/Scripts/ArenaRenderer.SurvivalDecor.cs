using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private void BuildSurvivalDecor(Node3D sector, int sectorX, int sectorY)
    {
        int seed = unchecked((_map.SurvivalMap?.Seed ?? 0) ^ sectorX * 73856093 ^ sectorY * 19349663 ^ 0x2d6a7b15);
        var random = new Random(seed);
        bool city = _map.Theme == ArenaTheme.City;
        int pitch = city ? 12 : 6, limit = city ? 2 : Ui.IsMobile ? 5 : 7, placed = 0;
        for (int x = 0; x < SurvivalSectorSize && placed < limit; x += pitch)
        for (int y = 0; y < SurvivalSectorSize && placed < limit; y += pitch)
        {
            if (random.Next(3) == 0) continue;
            var at = new V2(sectorX * SurvivalSectorSize + x + pitch / 2f + random.Next(-1, 2),
                sectorY * SurvivalSectorSize + y + pitch / 2f + random.Next(-1, 2));
            float clearance = city ? 5.3f : _map.Theme == ArenaTheme.Forest ? 4.2f : 2.7f;
            if (Math.Abs(at.X) > _map.HalfWidth + 10 || Math.Abs(at.Y) > _map.HalfHeight + 10 ||
                !OutsideFloor(at, clearance, clearance)) continue;
            // Solo fuera de TODO el suelo; no añade obstáculos a la simulación.
            BuildSurvivalProp(sector, at, random); placed++; SurvivalDecorations++;
        }
    }

    private void BuildSurvivalProp(Node3D parent, V2 at, Random random)
    {
        if (_map.Theme == ArenaTheme.Forest)
        { BuildTree(parent, at, .9f + random.NextSingle() * .25f, 2.1f + random.NextSingle() * .3f, random.Next(Leaves.Length)); return; }
        if (_map.Theme == ArenaTheme.City)
        { BuildHouse(parent, at, random.Next(2) == 0 ? new(1, 0) : new(0, 1), random.Next(Facades.Length), scale: .65f); return; }
        var root = new Node3D { Position = World(at), Rotation = new(0, random.NextSingle() * Mathf.Tau, 0) };
        parent.AddChild(root);
        Color stone = new(_survivalPalette.Border), accent = new(_survivalPalette.Accent);
        float height = 1.1f + random.NextSingle() * .8f;
        switch (_map.Theme)
        {
            case ArenaTheme.Ruins:
                Mesh(root, new CylinderMesh { TopRadius = .5f, BottomRadius = .7f, Height = height, RadialSegments = 7 }, new(0, height / 2, 0), stone);
                Box(root, new(1.4f, .28f, 1.3f), new(0, height, 0), stone.Lightened(.1f));
                Box(root, new(.7f, .22f, .8f), new(.4f, .1f, .3f), accent.Darkened(.2f));
                break;
            case ArenaTheme.Sanctuary:
                Mesh(root, new CylinderMesh { TopRadius = .8f, BottomRadius = 1, Height = .3f, RadialSegments = 10 }, new(0, .15f, 0), stone);
                Mesh(root, new PrismMesh { Size = new(.55f, height, .55f) }, new(0, height / 2 + .4f, 0), accent, .15f);
                break;
            case ArenaTheme.Desert:
                if (random.Next(2) == 0)
                {
                    Mesh(root, new CylinderMesh { TopRadius = .18f, BottomRadius = .24f, Height = height, RadialSegments = 7 }, new(0, height / 2, 0), accent.Darkened(.25f));
                    Box(root, new(.9f, .2f, .22f), new(.25f, height * .55f, 0), accent.Darkened(.25f));
                    Box(root, new(.2f, .55f, .22f), new(.6f, height * .7f, 0), accent.Darkened(.25f));
                }
                else Mesh(root, new SphereMesh { Radius = .85f, Height = .9f, RadialSegments = 7, Rings = 3 }, new(0, .35f, 0), stone);
                break;
            case ArenaTheme.Frozen:
            case ArenaTheme.Crystal:
                for (int i = 0; i < 3; i++)
                {
                    float h = height * (i == 0 ? 1 : .6f);
                    var shard = Mesh(root, new PrismMesh { Size = new(.6f, h, .6f) }, new((i - 1) * .6f, h / 2, (i % 2) * .35f),
                        i == 0 ? accent : stone.Lightened(.15f), _map.Theme == ArenaTheme.Crystal ? .12f : 0);
                    shard.Rotation = new(0, i * 1.1f, (i - 1) * .15f);
                }
                break;
            case ArenaTheme.Volcanic:
                Mesh(root, new SphereMesh { Radius = .9f, Height = height, RadialSegments = 6, Rings = 3 }, new(0, height * .4f, 0), stone.Darkened(.2f));
                Mesh(root, new PrismMesh { Size = new(.35f, .5f, .35f) }, new(.5f, .2f, .4f), accent, .15f);
                break;
            case ArenaTheme.Marsh:
                Mesh(root, new CylinderMesh { TopRadius = .28f, BottomRadius = .6f, Height = .9f, RadialSegments = 6 }, new(0, .45f, 0), stone);
                for (int i = 0; i < 4; i++)
                    Box(root, new(.09f, height * .7f, .11f), new((i - 1.5f) * .25f, height * .35f, .5f), accent.Darkened(.15f));
                break;
            case ArenaTheme.Fungal:
                for (int i = 0; i < 3; i++)
                {
                    float h = height * (i == 0 ? .9f : .5f);
                    var offset = new Vector3((i - 1) * .65f, 0, (i % 2) * .45f);
                    Mesh(root, new CylinderMesh { TopRadius = .12f, BottomRadius = .22f, Height = h, RadialSegments = 6 }, offset + Vector3.Up * (h / 2), stone.Lightened(.15f));
                    Mesh(root, new SphereMesh { Radius = i == 0 ? .75f : .45f, Height = .45f, RadialSegments = 8, Rings = 4 },
                        offset + Vector3.Up * h, i == 0 ? accent : stone.Lightened(.3f), .08f);
                }
                break;
        }
    }
}
