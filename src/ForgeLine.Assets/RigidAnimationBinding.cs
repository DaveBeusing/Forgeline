using System.Numerics;

namespace ForgeLine.Assets;

public static class RigidAnimationBinding
{
    /// <summary>Validates identity, hierarchy and conservative swept bounds once at compile/load, never per frame.</summary>
    public static int[] Validate(RuntimeAnimationData clip, IReadOnlyList<MechanicalMeshPart> parts,
        IReadOnlyList<AssetSocket> sockets, RuntimeMeshData mesh)
    {
        MechanicalMeshValidation.Validate(parts, sockets, mesh.Indices.Count);
        if (parts.Count == 0 || parts.Any(p => p.Motion != MechanicalMotionKind.Static))
            throw new InvalidDataException("Clip meshes require static named mechanical ranges; code-driven parts cannot share a clip mesh.");
        var map = Enumerable.Repeat(-1, parts.Count).ToArray();
        for (int n = 0; n < clip.Nodes.Count; n++)
        {
            int p = -1;
            for (int i = 0; i < parts.Count; i++) if (parts[i].Name == clip.Nodes[n].Name) { p = i; break; }
            if (p < 0) throw new InvalidDataException($"Clip node '{clip.Nodes[n].Name}' has no mesh range.");
            int parent = parts[p].Parent;
            if (clip.Nodes[n].Parent < 0 ? parent != -1 : parent < 0 || parts[parent].Name != clip.Nodes[clip.Nodes[n].Parent].Name)
                throw new InvalidDataException("Clip and mesh parent identities differ; include the complete ancestor chain.");
            if (!sockets.Any(s => s.IsValid && s.Name == parts[p].Socket))
                throw new InvalidDataException("Clip nodes require explicit valid mesh pivot sockets.");
            map[p] = n;
        }
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        foreach (var v in mesh.Vertices) { min = Vector3.Min(min, new(v.X, v.Y, v.Z)); max = Vector3.Max(max, new(v.X, v.Y, v.Z)); }
        for (int i = 0; i < parts.Count; i++)
        {
            Vector3 lo = new(float.PositiveInfinity), hi = new(float.NegativeInfinity);
            var part = parts[i]; bool affected = false;
            for (int k = part.FirstIndex; k < part.FirstIndex + part.IndexCount; k++)
            { var v = mesh.Vertices[(int)mesh.Indices[k]]; lo = Vector3.Min(lo, new(v.X, v.Y, v.Z)); hi = Vector3.Max(hi, new(v.X, v.Y, v.Z)); }
            for (int p = i; p >= 0; p = parts[p].Parent)
            {
                if (map[p] < 0) continue;
                foreach (var channel in clip.Channels.Where(c => c.Node == map[p]).OrderByDescending(c => c.Path))
                {
                    affected = true;
                    if (channel.Path == AnimationPath.Rotation)
                    {
                        var socket = sockets.First(s => s.Name == parts[p].Socket);
                        Vector3 pivot = new(socket.X, socket.Y, socket.Z);
                        Vector3 extent = Vector3.Max(Vector3.Abs(lo - pivot), Vector3.Abs(hi - pivot));
                        float radius = extent.Length(); lo = pivot - new Vector3(radius); hi = pivot + new Vector3(radius);
                    }
                    else
                    {
                        Vector3 low = Vector3.Zero, high = Vector3.Zero;
                        foreach (var v in channel.Values) { low = Vector3.Min(low, new(v.X, v.Y, v.Z)); high = Vector3.Max(high, new(v.X, v.Y, v.Z)); }
                        lo += low; hi += high;
                    }
                }
            }
            const float tolerance = .00001f;
            if (affected && (lo.X < min.X - tolerance || lo.Y < min.Y - tolerance || lo.Z < min.Z - tolerance ||
                hi.X > max.X + tolerance || hi.Y > max.Y + tolerance || hi.Z > max.Z + tolerance))
                throw new InvalidDataException($"Clip swept bounds for '{part.Name}' exceed static mesh bounds; retain a containing static frame or reduce motion.");
        }
        return map;
    }
}
