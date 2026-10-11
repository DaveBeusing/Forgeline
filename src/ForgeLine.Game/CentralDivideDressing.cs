using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Game;

/// <summary>Authored art groups; these records never contribute navigation or buildability.</summary>
internal static class CentralDivideDressing
{
    public static BattlefieldWorldObjectDefinition[] Create(
        BattlefieldStartPosition[] starts, BattlefieldResourceDepositDefinition[] resources,
        BattlefieldSiteDefinition[] sites, BattlefieldRoadNodeDefinition[] nodes,
        BattlefieldRoadEdgeDefinition[] edges, BattlefieldCrossingDefinition[] crossings)
    {
        var objects = new List<BattlefieldWorldObjectDefinition>();
        // Deliberate clearings separate the industrial fringe, approach corridors and horizon groups.
        (string Key, Vector2 Center, float Angle)[] groves =
        [
            ("west-yard", new(430, 720), .2f), ("west-ridge", new(1050, 440), 1.1f),
            ("north-bank", new(1300, 580), -.5f), ("east-bank", new(1780, 540), .8f),
            ("east-yard", new(2630, 2770), 2.3f), ("east-ridge", new(2040, 2680), -.9f),
            ("south-bank", new(1320, 2680), 1.4f), ("south-east", new(1820, 2630), .3f),
            ("west-base", new(300, 1130), .6f), ("east-base", new(2770, 1130), -.6f),
            ("north-horizon", new(700, 180), .4f), ("south-horizon", new(2360, 2910), -.4f)
        ];
        Vector2[] canopy = [new(-34,-18), new(-15,9), new(5,-25), new(26,-7), new(41,18), new(8,31), new(-26,37)];
        foreach (var grove in groves)
        {
            for (int i = 0; i < canopy.Length; i++)
            {
                Vector2 p = grove.Center + Rotate(canopy[i], grove.Angle);
                float height = 17 + (i * 7 % 11);
                Add($"grove.{grove.Key}.canopy.{i}", WorldVisualId.VegetationConifer, p, new(7 + i % 3, height, 7 + i % 3), i * 47);
            }
            // Lower, wider-spaced margins let unit silhouettes read against the clear ground.
            for (int i = 0; i < 12; i++)
            {
                float angle = i * MathF.Tau / 12 + grove.Angle;
                Vector2 p = grove.Center + new Vector2(MathF.Cos(angle) * (55 + i % 3 * 7), MathF.Sin(angle) * (46 + i % 4 * 6));
                Add($"grove.{grove.Key}.margin.{i}", i % 3 == 0 ? WorldVisualId.PropRock : WorldVisualId.VegetationScrub,
                    p, i % 3 == 0 ? new(6, 3, 5) : new(6, 2.5f, 5), i * 31);
                Add($"grove.{grove.Key}.grass.{i}", WorldVisualId.VegetationGrassClump, p + new Vector2(9, 7), new(7, 1, 6), i * 19);
            }
        }

        // Three-sided service yards sit outside playable construction rectangles; the inward edge stays open.
        Yard("west-outpost", new(650, 305), 0);
        Yard("east-outpost", new(2420, 2775), MathF.PI);
        Yard("west-base", new(460, 1220), 0);
        Yard("east-base", new(2610, 1220), 0);
        return objects.ToArray();

        void Yard(string key, Vector2 center, float rotation)
        {
            Place("crate-a", WorldVisualId.PropCrate, new(-20, 6), new(4, 3, 4), 4);
            Place("crate-b", WorldVisualId.PropCrate, new(-25, 1), new(3, 2.5f, 3), -8);
            Place("pallet", WorldVisualId.PropPallet, new(-12, 7), new(5, .6f, 4), 0);
            Place("drum-a", WorldVisualId.PropDrum, new(14, -3), new(1.6f, 2.4f, 1.6f), 0);
            Place("drum-b", WorldVisualId.PropDrum, new(17, -2), new(1.6f, 2.4f, 1.6f), 0);
            Place("utility", WorldVisualId.PropUtilityBox, new(25, -10), new(3, 4, 2), 0);
            Place("light", WorldVisualId.PropIndustrialLightSignage, new(-33, -13), new(1.5f, 9, 1.5f), 0);
            Place("pipe", WorldVisualId.PropPipeSection, new(14, 10), new(9, 2, 2), 90);
            Place("drain", WorldVisualId.PropConcreteBlock, new(29, 16), new(8, .5f, 2), 0);
            Place("rubble", WorldVisualId.PropRubble, new(34, 22), new(6, 1.2f, 5), 25);
            for (int i = 0; i < 4; i++)
                Place($"fence-back-{i}", WorldVisualId.PropFence, new(-27 + i * 18, -24), new(18, 3, 1), 0);
            Place("fence-left", WorldVisualId.PropFence, new(-40, -12), new(20, 3, 1), 90);
            Place("fence-right", WorldVisualId.PropFence, new(40, -12), new(20, 3, 1), 90);
            Place("oil", WorldVisualId.DecalOilStain, new(16, 0), new(9, .08f, 7), 12);
            Place("wear", WorldVisualId.DecalTireTracks, new(0, 12), new(22, .08f, 5), 90);

            void Place(string part, WorldVisualId visual, Vector2 offset, Vector3 scale, float yaw) =>
                Add($"yard.{key}.{part}", visual, center + Rotate(offset, rotation), scale, yaw + rotation * 180 / MathF.PI);
        }

        void Add(string key, WorldVisualId visual, Vector2 p, Vector3 scale, float yaw)
        {
            float radius = new Vector2(scale.X, scale.Z).Length() * .5f;
            if (p.X - radius < 0 || p.Y - radius < 0 || p.X + radius > CentralDivideBattlefield.WidthMeters || p.Y + radius > CentralDivideBattlefield.HeightMeters) return;
            if (starts.Any(x => Near(x.BuildArea, p, radius + 12)) || sites.Any(x => Near(x.BuildArea, p, radius + 12))) return;
            if (crossings.Any(x => Near(x.PassageBounds, p, radius + 32))) return;
            if (resources.Any(x => Vector2.Distance(p, new(x.Center.X, x.Center.Z)) < radius + new Vector2(x.HalfExtents.X, x.HalfExtents.Z).Length() + 24)) return;
            foreach (var edge in edges)
            {
                var a = nodes.First(x => x.Key == edge.SourceNodeKey).Position;
                var b = nodes.First(x => x.Key == edge.DestinationNodeKey).Position;
                Vector2 start = new(a.X, a.Z), delta = new(b.X - a.X, b.Z - a.Z);
                float t = Math.Clamp(Vector2.Dot(p - start, delta) / delta.LengthSquared(), 0, 1);
                if (Vector2.Distance(p, start + delta * t) < radius + 28) return;
            }
            WorldPresentationKind kind = visual >= WorldVisualId.DecalTireTracks ? WorldPresentationKind.Decal
                : visual >= WorldVisualId.VegetationConifer ? WorldPresentationKind.Vegetation : WorldPresentationKind.Prop;
            objects.Add(new($"dressing.{key}", visual, kind, new(p.X, 0, p.Y), scale, yaw));
        }
    }

    private static Vector2 Rotate(Vector2 p, float angle) => new(p.X * MathF.Cos(angle) - p.Y * MathF.Sin(angle), p.X * MathF.Sin(angle) + p.Y * MathF.Cos(angle));

    private static bool Near(AxisAlignedBounds bounds, Vector2 p, float padding) =>
        p.X >= bounds.Minimum.X - padding && p.X <= bounds.Maximum.X + padding &&
        p.Y >= bounds.Minimum.Z - padding && p.Y <= bounds.Maximum.Z + padding;
}
