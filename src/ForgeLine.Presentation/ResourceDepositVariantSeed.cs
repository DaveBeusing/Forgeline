using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using ForgeLine.Core;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public static class ResourceDepositVariantSeed
{
    // Authored presentation salt, independent of the authoritative simulation RNG.
    public const ulong DefaultVisualSeed = 2026;

    public static bool TryCreate(ResourceId resource, string mapKey, string depositKey, ulong visualSeed,
        uint anchorOrdinal, out ResourceDepositVariantSelection selection, out Vector3 tint)
    {
        selection = default; tint = default;
        int family = resource.Value switch { 1 => 0, 3 => 1, 2 => 2, 8 => 3, _ => -1 };
        if (family < 0 || !ValidKey(mapKey) || !ValidKey(depositKey)) return false;
        WorldVisualId visual = WorldPresentationIdentity.ForResource(resource).Visual;
        uint form = Bucket(Channel("form"));
        int[] weights = family switch
        {
            0 => [40, 25, 20, 15], 1 => [35, 30, 20, 15],
            2 => [40, 25, 20, 15], _ => [35, 30, 20, 15]
        };
        int layout = 0, cumulative = weights[0];
        while (layout < 3 && form >= cumulative) cumulative += weights[++layout];
        uint sizeBucket = Bucket(Channel("size"));
        int size = sizeBucket < 25 ? 0 : sizeBucket < 75 ? 1 : 2;
        // Additive channel: existing form/size/tint hashes do not shift.
        int surface = (int)((ulong)Channel("surface") * 4 >> 32);
        selection = ResourceDepositVariantCatalog.SelectAuthored(visual, layout * 3 + size, surface);
        var range = ResourceDepositVariantCatalog.TintRange(visual);
        tint = Vector3.Lerp(range.Min, range.Max, (float)(Channel("tint") / 4294967296.0));
        return true;

        uint Channel(string channel)
        {
            using var bytes = new MemoryStream();
            using (var writer = new BinaryWriter(bytes, Encoding.ASCII, leaveOpen: true))
            {
                writer.Write(Encoding.ASCII.GetBytes("FORGELINE.resource-visual/1")); writer.Write((byte)0);
                writer.Write(visualSeed); writer.Write(resource.Value);
                WriteKey(writer, mapKey); WriteKey(writer, depositKey);
                writer.Write(anchorOrdinal); WriteKey(writer, channel);
            }
            return BinaryPrimitives.ReadUInt32LittleEndian(SHA256.HashData(bytes.ToArray()));
        }
    }

    internal static Dictionary<EntityId, (WorldVisualId Visual, ResourceDepositVariantSelection Selection, Vector3 Tint)> CreateMap(MatchRuntime scenario)
    {
        var result = new Dictionary<EntityId, (WorldVisualId, ResourceDepositVariantSelection, Vector3)>();
        var entities = scenario.BattlefieldRuntime.ResourceEntities;
        var deposits = scenario.Battlefield.Resources;
        if (entities.Count != deposits.Count) throw new InvalidDataException("Authored deposit keys do not match runtime resource entities.");
        for (int i = 0; i < deposits.Count; i++)
            if (TryCreate(deposits[i].ResourceId, scenario.Battlefield.Metadata.Key, deposits[i].Key,
                DefaultVisualSeed, 0, out var selection, out var tint))
                result.Add(entities[i], (WorldPresentationIdentity.ForResource(deposits[i].ResourceId).Visual, selection, tint));
        return result;
    }

    private static uint Bucket(uint hash) => (uint)((ulong)hash * 100 >> 32);
    private static void WriteKey(BinaryWriter writer, string key)
    {
        byte[] ascii = Encoding.ASCII.GetBytes(key);
        writer.Write((uint)ascii.Length); writer.Write(ascii);
    }
    private static bool ValidKey(string? key) => key is { Length: > 0 and <= 128 } &&
        key.All(static c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-');
}
