using System.Numerics;

namespace ForgeLine.Game;

// Bounded public scenery only; these definitions never allocate simulation entities.
internal static class CentralDivideKitDressing
{
    internal static BattlefieldWorldObjectDefinition[] Create()
    {
        var result = new List<BattlefieldWorldObjectDefinition>();
        Place("gantry", WorldVisualId.PropLoadingGantry, 840, 300, new(8, 6, 4));
        for (int i = 0; i < 3; i++)
        {
            Place($"slab.{i}", WorldVisualId.PropLoadingApron, 832 + i * 8, 300, new(8, .45f, 8));
            Place($"barrier.{i}", WorldVisualId.PropBarrierModule, 830 + i * 4, 312, new(4, 1.2f, .9f));
        }
        for (int i = 0; i < 4; i++)
            Place($"fence.{i}", WorldVisualId.PropServiceFence, 828 + i * 8, 288, new(8, 2.4f, .35f));
        Place("pipe.0", WorldVisualId.PropPipeElbow, 854, 303, new(3, 1, 3));
        Place("pipe.1", WorldVisualId.PropPipeElbow, 858, 303, new(3, 1, 3), 90);
        Place("yard-drain", WorldVisualId.PropRoadEdge, 840, 307.3f, new(8, .4f, 1.4f));
        for (int i = 0; i < 4; i++)
        {
            Place($"broadleaf.{i}", WorldVisualId.VegetationBroadleaf, 878 + i * 9, 278 + i % 2 * 13, new(7, 12 + i, 7), i * 37);
            Place($"slab-rock.{i}", WorldVisualId.PropRockSlab, 883 + i * 10, 304 + i % 2 * 10, new(9, 3, 6), i * 43);
            Place($"spire-rock.{i}", WorldVisualId.PropRockSpire, 892 + i * 9, 326 + i % 2 * 8, new(4, 7, 4), i * 29);
        }
        // Physical mesh edges join the unchanged 12m road and existing crossing bounds.
        // Aprons are thin nonblocking decoration, never crossing health or restoration state.
        Place("bridge-apron.west", WorldVisualId.PropBridgeApron, 1484, 920, new(12, .3f, 8), -90);
        Place("bridge-apron.east", WorldVisualId.PropBridgeApron, 1588, 920, new(12, .3f, 8), 90);
        for (int i = 0; i < 3; i++)
        {
            Place($"road-drain.west.{i}", WorldVisualId.PropRoadEdge, 1044 + i * 8, 1528.5f, new(8, .4f, 1.4f));
            Place($"road-drain.east.{i}", WorldVisualId.PropRoadEdge, 2012 + i * 8, 1543.5f, new(8, .4f, 1.4f), 180);
        }
        return result.ToArray();

        void Place(string key, WorldVisualId visual, float x, float z, Vector3 scale, float yaw = 0) =>
            result.Add(new($"kit.{key}", visual,
                visual == WorldVisualId.VegetationBroadleaf ? WorldPresentationKind.Vegetation : WorldPresentationKind.Prop,
                new(x, 0, z), scale, yaw, PresentationOnly: true));
    }
}
