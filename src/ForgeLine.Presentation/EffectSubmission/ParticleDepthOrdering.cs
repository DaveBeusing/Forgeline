using System.Numerics;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation.EffectSubmission;

internal struct ParticleDepthKey
{
    internal float Depth;
    internal int SourceIndex;
}

internal static class ParticleDepthOrdering
{
    internal static void Sort(Span<GraphicsParticle> particles, Span<ParticleDepthKey> scratch, in Matrix4x4 view)
    {
        if (scratch.Length < particles.Length) throw new ArgumentException("Particle ordering scratch is too small.", nameof(scratch));
        scratch = scratch[..particles.Length];
        for (int i = 0; i < particles.Length; i++)
            scratch[i] = new() { Depth = ViewDepth(particles[i], view), SourceIndex = i };

        // Sort small keys once, then apply the permutation in place. Equal-depth
        // particles retain the deterministic emitter/particle submission order.
        for (int root = scratch.Length / 2 - 1; root >= 0; root--) Sift(scratch, root);
        for (int end = scratch.Length - 1; end > 0; end--)
        {
            (scratch[0], scratch[end]) = (scratch[end], scratch[0]);
            Sift(scratch[..end], 0);
        }
        for (int start = 0; start < scratch.Length; start++)
        {
            if (scratch[start].SourceIndex < 0) continue;
            GraphicsParticle saved = particles[start];
            int destination = start;
            while (true)
            {
                int source = scratch[destination].SourceIndex;
                scratch[destination].SourceIndex = ~source;
                if (source == start)
                {
                    particles[destination] = saved;
                    break;
                }
                particles[destination] = particles[source];
                destination = source;
            }
        }
    }

    internal static float ViewDepth(in GraphicsParticle particle, in Matrix4x4 view)
    {
        // Evaluate only a scalar ordering key from the same analytic conditions
        // as the vertex shader. Initial conditions and simulation stay immutable.
        float age = particle.OriginAge.W;
        float depth = -(particle.OriginAge.X * view.M13 + particle.OriginAge.Y * view.M23 +
            particle.OriginAge.Z * view.M33 + view.M43);
        depth -= age * (particle.VelocityLife.X * view.M13 + particle.VelocityLife.Y * view.M23 + particle.VelocityLife.Z * view.M33);
        depth -= .5f * particle.Shape.Y * age * age * view.M23;
        if (particle.Shape.Z < 1.5f)
        {
            float curl = MathF.Sin(age * 2 + particle.Shape.W) * age * .3f;
            depth -= curl * (view.M13 + view.M33);
        }
        return float.IsFinite(depth) ? depth : float.NegativeInfinity;
    }

    private static int Compare(in ParticleDepthKey a, in ParticleDepthKey b)
    {
        int order = b.Depth.CompareTo(a.Depth);
        return order != 0 ? order : a.SourceIndex.CompareTo(b.SourceIndex);
    }

    private static void Sift(Span<ParticleDepthKey> values, int root)
    {
        while (root * 2 + 1 < values.Length)
        {
            int child = root * 2 + 1;
            if (child + 1 < values.Length && Compare(values[child], values[child + 1]) < 0) child++;
            if (Compare(values[root], values[child]) >= 0) return;
            (values[root], values[child]) = (values[child], values[root]);
            root = child;
        }
    }
}
