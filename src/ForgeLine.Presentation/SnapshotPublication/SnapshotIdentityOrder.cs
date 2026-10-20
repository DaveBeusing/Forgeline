namespace ForgeLine.Presentation;

// Render-owner permutations only; published instance order and GC ownership never change.
internal static class SnapshotIdentityOrder
{
    internal const int MaximumRetainedInstances = 65_536;

    internal static void Trim(ref int[] values, int count)
    {
        if (values.Length > MaximumRetainedInstances && count <= MaximumRetainedInstances) values = [];
    }

    internal static void Ensure(ref int[] values, int count)
    {
        Trim(ref values, count);
        if (values.Length >= count) return;
        int capacity = count > MaximumRetainedInstances ? count :
            Math.Min(MaximumRetainedInstances, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(16, count)));
        values = new int[capacity];
    }

    // Production has either one ascending run or ECS/dressing/VFX followed by decals.
    // Arbitrary caller order uses eight stable radix passes over the full uint/uint identity.
    internal static bool Prepare(PresentationSnapshot snapshot, ref int[] order, ref int[] scratch, out bool duplicates)
    {
        int split = -1;
        bool manyRuns = false;
        duplicates = false;
        for (int i = 1; i < snapshot.InstanceCount; i++)
        {
            int comparison = snapshot.Instances[i - 1].Entity.CompareTo(snapshot.Instances[i].Entity);
            if (comparison == 0) duplicates = true;
            if (comparison <= 0) continue;
            if (split >= 0) manyRuns = true;
            else split = i;
        }
        if (split < 0) return true;
        int count = snapshot.InstanceCount;
        Ensure(ref order, count);
        if (!manyRuns)
        {
            int a = 0, b = split;
            for (int target = 0; target < count; target++)
                order[target] = b == count || a < split &&
                    snapshot.Instances[a].Entity.CompareTo(snapshot.Instances[b].Entity) <= 0 ? a++ : b++;
        }
        else
        {
            Ensure(ref scratch, count);
            for (int i = 0; i < count; i++) order[i] = i;
            int[] source = order, destination = scratch;
            Span<int> buckets = stackalloc int[256];
            for (int pass = 0; pass < 8; pass++)
            {
                buckets.Clear();
                for (int i = 0; i < count; i++) buckets[Digit(source[i], pass)]++;
                int prefix = 0;
                for (int i = 0; i < buckets.Length; i++)
                { int size = buckets[i]; buckets[i] = prefix; prefix += size; }
                for (int i = 0; i < count; i++) destination[buckets[Digit(source[i], pass)]++] = source[i];
                (source, destination) = (destination, source);
            }
        }
        for (int i = 1; i < count; i++)
            if (snapshot.Instances[order[i - 1]].Entity == snapshot.Instances[order[i]].Entity) duplicates = true;
        return false;

        int Digit(int index, int pass)
        {
            var entity = snapshot.Instances[index].Entity;
            uint value = pass < 4 ? entity.Generation : entity.Index;
            return (int)((value >> ((pass & 3) * 8)) & 255);
        }
    }
}
