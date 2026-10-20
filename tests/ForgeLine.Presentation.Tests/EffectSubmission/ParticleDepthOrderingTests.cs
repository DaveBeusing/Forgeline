using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Presentation.EffectSubmission;
using Xunit;

namespace ForgeLine.Presentation.Tests.EffectSubmission;

public sealed class ParticleDepthOrderingTests
{
    [Fact]
    public void ViewDepthOrdersOffAxisParticlesInsteadOfRadialDistance()
    {
        // The off-axis near particle is radially farther away, but must blend last.
        GraphicsParticle near = Particle(new(100, 0, -2), Vector3.Zero, 0, 0, ParticleVisualKind.Spark, 1);
        GraphicsParticle far = Particle(new(0, 0, -10), Vector3.Zero, 0, 0, ParticleVisualKind.Spark, 2);
        GraphicsParticle[] particles = [near, far];
        ParticleDepthOrdering.Sort(particles, new ParticleDepthKey[2], Matrix4x4.Identity);
        Assert.Equal(new[] { far, near }, particles);
    }

    [Fact]
    public void AnalyticMotionCanReverseAnchorOrderWithoutChangingInitialConditions()
    {
        GraphicsParticle retreating = Particle(new(0, 0, -10), new(0, 0, 8), 1, 0, ParticleVisualKind.Spark, 1);
        GraphicsParticle advancing = Particle(new(0, 0, -4), new(0, 0, -12), 1, 0, ParticleVisualKind.Muzzle, 2);
        GraphicsParticle[] particles = [retreating, advancing];
        ParticleDepthOrdering.Sort(particles, new ParticleDepthKey[2], Matrix4x4.Identity);
        Assert.Equal(new[] { advancing, retreating }, particles);
        Assert.Equal(16, ParticleDepthOrdering.ViewDepth(advancing, Matrix4x4.Identity));
        Assert.Equal(2, ParticleDepthOrdering.ViewDepth(retreating, Matrix4x4.Identity));
    }

    [Fact]
    public void GravityAndCurlAffectOnlyTheOrderingKey()
    {
        var view = Matrix4x4.CreateLookAt(new(0, 8, 12), Vector3.Zero, Vector3.UnitY);
        GraphicsParticle particle = Particle(new(2, 3, -4), new(1, 2, 3), .75f, -9.8f, ParticleVisualKind.Smoke, .35f);
        const float age = .75f;
        float curl = MathF.Sin(age * 2 + .35f) * age * .3f;
        Vector3 center = new(2 + age + curl, 3 + 2 * age + .5f * -9.8f * age * age, -4 + 3 * age + curl);
        float expected = -Vector3.Transform(center, view).Z;
        Assert.InRange(MathF.Abs(expected - ParticleDepthOrdering.ViewDepth(particle, view)), 0, .00001f);
        Assert.Equal(new Vector4(2, 3, -4, age), particle.OriginAge);
        Assert.Equal(new Vector4(1, 2, 3, 2), particle.VelocityLife);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(4096)]
    public void BoundedPermutationPreservesPayloadAndEqualDepthInputOrder(int count)
    {
        var random = new Random(51913);
        GraphicsParticle[] original = Enumerable.Range(0, count)
            .Select(i => Particle(new(i, 0, -random.Next(1, 9)), Vector3.Zero, 0, 0, ParticleVisualKind.Spark, i)).ToArray();
        GraphicsParticle[] expected = original.OrderByDescending(x => -x.OriginAge.Z).ToArray();
        GraphicsParticle[] actual = original.ToArray();
        var scratch = new ParticleDepthKey[count];
        ParticleDepthOrdering.Sort(actual, scratch, Matrix4x4.Identity);
        Assert.Equal(expected, actual);
        ParticleDepthOrdering.Sort(actual, scratch, Matrix4x4.Identity);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void InsufficientScratchFailsBeforeMutatingParticles()
    {
        GraphicsParticle[] particles = [Particle(new(0, 0, -1), Vector3.Zero, 0, 0, ParticleVisualKind.Spark, 1)];
        GraphicsParticle original = particles[0];
        Assert.Throws<ArgumentException>(() => ParticleDepthOrdering.Sort(particles, [], Matrix4x4.Identity));
        Assert.Equal(original, particles[0]);
    }

    private static GraphicsParticle Particle(Vector3 origin, Vector3 velocity, float age, float gravity, ParticleVisualKind visual, float phase) =>
        new(new(origin, age), new(velocity, 2), new(1, .5f, .25f, .5f), new(.5f, gravity, (float)visual, phase));
}
