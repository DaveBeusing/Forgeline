using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Presentation.EffectSubmission;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests.EffectSubmission;

public sealed class ParticleEffectSubmissionTests
{
    [Fact]
    public void OffscreenSourcesCannotExcludeALaterVisibleEventAtTheOldEmitterBoundary()
    {
        RenderInstance[] effects = Enumerable.Range(1, 2048)
            .Select(i => Effect((uint)i, VfxEffectKind.PersistentHeavySmoke, true, new(1000000, 0, 1000000)))
            .Append(Effect(4095, VfxEffectKind.MuzzleTankCannon)).ToArray();
        var submission = Create();
        submission.Prepare(World(effects), Camera(), 1280, 720, .1, false);
        Assert.Equal(8, submission.Diagnostics.ByType.Muzzle);
        Assert.Equal(1, submission.Diagnostics.ActiveEmitters);
        Assert.Equal(1, submission.RetainedEmitterCount);
        Assert.Equal(0, submission.Diagnostics.SuppressedEffects);
    }

    [Fact]
    public void VisibleImpactCompetesAheadOfDecorativeDensityAcrossTheCompleteSnapshot()
    {
        RenderInstance[] effects = Enumerable.Range(1, 4095)
            .Select(i => Effect((uint)i, VfxEffectKind.PersistentHeavySmoke, true))
            .Append(Effect(4096, VfxEffectKind.ImpactMetal)).ToArray();
        var forward = Create(ParticleQuality.Low);
        var reversed = Create(ParticleQuality.Low);
        forward.Prepare(World(effects), Camera(120), 1280, 720, .1, false);
        reversed.Prepare(World(effects.Reverse().ToArray()), Camera(120), 1280, 720, .1, false);
        Assert.Equal(4, forward.Diagnostics.ByType.Sparks);
        Assert.Equal(forward.Particles.ToArray(), reversed.Particles.ToArray());
        Assert.Equal(forward.Diagnostics.SuppressedEffects, reversed.Diagnostics.SuppressedEffects);
        Assert.True(forward.Diagnostics.SuppressedEffects > 0);
        Assert.InRange(forward.Diagnostics.ActiveParticles, 1, 512);
        Assert.InRange(forward.Diagnostics.ScreenCoverage, 0, .5f);
    }

    [Fact]
    public void FallbackAdmissionUsesEventPriorityAndStableIdentityWithoutSnapshotOrderBias()
    {
        RenderInstance impact = Effect(900, VfxEffectKind.ImpactArmor);
        RenderInstance[] effects = Enumerable.Range(1, 600)
            .Select(i => Effect((uint)i, VfxEffectKind.DestructionSmokePlume, true)).Append(impact).ToArray();
        var forward = Create(ParticleQuality.Disabled);
        var reverse = Create(ParticleQuality.Disabled);
        forward.Prepare(World(effects), Camera(), 1280, 720, 0, false);
        reverse.Prepare(World(effects.Reverse().ToArray()), Camera(), 1280, 720, 0, false);
        Assert.False(forward.Replaces(impact));
        Assert.Equal(128, forward.Diagnostics.FallbackEffects);
        Assert.Equal(473, forward.Diagnostics.SuppressedEffects);
        foreach (RenderInstance effect in effects) Assert.Equal(forward.Replaces(effect), reverse.Replaces(effect));
    }

    [Fact]
    public void NearbyOffscreenFallbacksCannotConsumeTheVisibleFallbackBudget()
    {
        var camera = Camera();
        RenderInstance visible = Effect(900, VfxEffectKind.PersistentHeavySmoke, true);
        RenderInstance[] effects = Enumerable.Range(1, 512)
            .Select(i => Effect((uint)i, VfxEffectKind.PersistentHeavySmoke, true, camera.Position + new Vector3(100, 0, 0)))
            .Append(visible).ToArray();
        var submission = Create(ParticleQuality.Disabled);
        submission.Prepare(World(effects), camera, 1280, 720, 0, false);
        Assert.Equal(1, submission.Diagnostics.FallbackEffects);
        Assert.False(submission.Replaces(visible));
        Assert.All(effects[..^1], effect => Assert.True(submission.Replaces(effect)));
    }

    [Fact]
    public void PausedPreparationRetainsParticleBytesAndHiddenSourcesRetireImmediately()
    {
        RenderInstance source = Effect(1, VfxEffectKind.PersistentHeavySmoke, true);
        var submission = Create();
        var world = World([source]);
        var camera = Camera();
        submission.Prepare(world, camera, 1280, 720, .1, false);
        submission.Prepare(world, camera, 1280, 720, .1, false);
        GraphicsParticle[] before = submission.Particles.ToArray();
        ulong emitted = submission.Diagnostics.EmittedParticles;
        submission.Prepare(world, camera, 1280, 720, 10, true);
        Assert.Equal(before, submission.Particles.ToArray());
        Assert.Equal(emitted, submission.Diagnostics.EmittedParticles);
        submission.Prepare(World([source with { Visibility = RenderVisibilityMask.Debug }]), camera, 1280, 720, .1, false);
        Assert.Empty(submission.Particles.ToArray());
        Assert.False(submission.Replaces(source));
        Assert.Equal(0, submission.RetainedEmitterCount);
    }

    [Fact]
    public void SessionReplacementAndTickRewindRestartOnlyCosmeticState()
    {
        RenderInstance source = Effect(1, VfxEffectKind.PersistentFire, true);
        var submission = Create();
        var camera = Camera();
        submission.Prepare(World([source], tick: 10), camera, 1280, 720, .1, false);
        GraphicsParticle[] initial = submission.Particles.ToArray();
        submission.Prepare(World([source], tick: 11), camera, 1280, 720, .1, false);
        Assert.NotEqual(initial, submission.Particles.ToArray());
        submission.Prepare(World([source], tick: 1), camera, 1280, 720, .1, false);
        Assert.Equal(initial, submission.Particles.ToArray());
        submission.Prepare(World([source], session: 2, tick: 12), camera, 1280, 720, .1, false);
        Assert.Equal(initial, submission.Particles.ToArray());
        Assert.Equal(8UL, submission.Diagnostics.EmittedParticles);
    }

    [Fact]
    public void ExpiredBurstsDoNotReserveParticleOrCoverageBudget()
    {
        RenderInstance expired = Effect(1, VfxEffectKind.ExplosionBuilding) with
        { VfxFeature = new(VfxEffectKind.ExplosionBuilding, 1, 1, false, 1) };
        RenderInstance fresh = Effect(2, VfxEffectKind.ExplosionBuilding) with
        { VfxFeature = new(VfxEffectKind.ExplosionBuilding, 2, 1, false, 100) };
        var submission = Create(ParticleQuality.Low);
        submission.Prepare(World([expired, fresh], tick: 100), Camera(), 1280, 720, .1, false);
        Assert.Equal(8, submission.Diagnostics.ActiveParticles);
        Assert.Equal(1, submission.Diagnostics.ActiveEmitters);
        Assert.Equal(1, submission.RetainedEmitterCount);
        Assert.Equal(8UL, submission.Diagnostics.EmittedParticles);
        Assert.True(submission.Replaces(expired));
    }

    [Fact]
    public void FullDormantEmitterPoolAdmitsTheCurrentVisibleGroupWithoutGrowing()
    {
        const int groupSize = 32;
        RenderInstance[] effects = Enumerable.Range(0, 65 * groupSize)
            .Select(i => Effect((uint)i + 1, VfxEffectKind.PersistentHeavySmoke, true,
                new(i / groupSize * 4000, 0, i % groupSize))).ToArray();
        var world = World(effects);
        var submission = Create();
        for (int group = 0; group < 65; group++)
        {
            var camera = new RtsCamera(new() { InitialTarget = new(group * 4000, 0, 16), InitialDistance = 420, MaximumDistance = 2000 });
            submission.Prepare(world, camera, 1280, 720, 0, false);
            Assert.Equal(groupSize, submission.Diagnostics.ActiveEmitters);
            Assert.Equal(Math.Min(2048, (group + 1) * groupSize), submission.RetainedEmitterCount);
            Assert.All(submission.Particles.ToArray(), p => Assert.Equal(group * 4000, p.OriginAge.X));
        }
        Assert.Equal(2048, submission.RetainedEmitterCount);
        Assert.Equal(0, submission.Diagnostics.SuppressedEffects);
    }

    [Theory]
    [InlineData(ParticleQuality.Low)]
    [InlineData(ParticleQuality.Medium)]
    [InlineData(ParticleQuality.High)]
    public void RepeatedFullBurstReplacementRemainsBounded(ParticleQuality quality)
    {
        var submission = Create(quality);
        var settings = new ParticleSettings(quality);
        var camera = Camera();
        for (int frame = 0; frame < 24; frame++)
        {
            RenderInstance[] effects = Enumerable.Range(1, 4096).Select(i => Effect((uint)i, VfxEffectKind.ExplosionMedium) with
            { VfxFeature = new(VfxEffectKind.ExplosionMedium, (uint)i, (uint)frame + 1, false) }).ToArray();
            submission.Prepare(World(effects, tick: (ulong)frame + 1), camera, 1280, 720, .05, false);
            Assert.InRange(submission.Diagnostics.ActiveParticles, 1, settings.Capacity);
            Assert.InRange(submission.Diagnostics.ScreenCoverage, 0, settings.MaximumScreenCoverage);
            Assert.InRange(submission.RetainedEmitterCount, 1, 2048);
            Assert.Equal(0, submission.Diagnostics.FallbackEffects);
            Assert.True(submission.Replaces(effects[^1]));
        }
        submission.Prepare(World([]), camera, 1280, 720, 0, false);
        Assert.Equal(0, submission.RetainedEmitterCount);
        Assert.Equal(0, submission.Diagnostics.ActiveParticles);
    }

    [Fact]
    public void WarmProductionPreparationAllocatesNoManagedBytes()
    {
        var submission = Create(ParticleQuality.High);
        var camera = Camera();
        var world = World(Enumerable.Range(1, 512).Select(i => Effect((uint)i, VfxEffectKind.PersistentHeavySmoke, true,
            new((i % 16 - 8) * 2, 0, (i / 16 - 16) * 2))).ToArray());
        for (int i = 0; i < 64; i++) submission.Prepare(world, camera, 1280, 720, 0, false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++) submission.Prepare(world, camera, 1280, 720, 0, false);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        long controlBefore = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(new byte[4096]);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - controlBefore >= 4096);
    }

    [Fact]
    public void CachedDescriptorsPreserveSeededInitialConditionsAndPalette()
    {
        var source = Effect(123, VfxEffectKind.ExplosionBuilding);
        var submission = Create();
        var camera = Camera();
        submission.Prepare(World([source]), camera, 1280, 720, .1, false);
        Assert.True(ParticleEmitterDescriptor.TryResolve(VfxEffectKind.ExplosionBuilding, out var descriptor));
        Assert.Equal(descriptor.Count, submission.Particles.Length);
        Vector4 tint = VfxPresentationCatalog.Get(source.VfxFeature.Kind).FallbackTint;
        tint.W = .6f;
        var expected = new List<(Vector4 Origin, Vector4 Velocity, Vector4 Color, float Phase)>();
        for (int i = 0; i < descriptor.Count; i++)
        {
            uint seed = unchecked(123U ^ 747796405U ^ (uint)source.VfxFeature.Kind * 2891336453U ^ (uint)i * 277803737U);
            seed ^= seed >> 16;
            seed = unchecked(seed * 2246822519U);
            seed ^= seed >> 13;
            float angle = (seed & 65535) / 65535f * MathF.Tau;
            float spread = .3f + ((seed >> 16) & 255) / 255f * .7f;
            Vector3 velocity = descriptor.InitialVelocity(angle, spread, Quaternion.Identity);
            expected.Add((new(Vector3.Zero, 0), new(velocity, descriptor.LifetimeSeconds), tint, angle));
        }
        foreach (GraphicsParticle particle in submission.Particles)
            Assert.Contains((particle.OriginAge, particle.VelocityLife, particle.Color, particle.Shape.W), expected);
    }

    internal static ParticleEffectSubmission Create(ParticleQuality quality = ParticleQuality.Medium)
    {
        bool[] available = Enumerable.Repeat(true, Enum.GetValues<VfxEffectKind>().Max(x => (int)x) + 1).ToArray();
        return new(new(quality), available);
    }

    internal static RtsCamera Camera(float distance = 420) => new(new() { InitialTarget = Vector3.Zero, InitialDistance = distance, MaximumDistance = 2000 });

    internal static RenderInstance Effect(uint id, VfxEffectKind kind, bool continuous = false, Vector3 position = default) =>
        new(new(id, 1), new(position, Quaternion.Identity, Vector3.One), new(1), RenderMaterialHandle.Default,
            RenderVisibilityMask.World, VfxFeature: new(kind, id, 1, continuous));

    internal static RenderWorld World(RenderInstance[] effects, ulong session = 1, ulong tick = 1)
    {
        var snapshots = new PresentationSnapshotBuffer();
        snapshots.Publish(new(new SimulationTick(tick), TimeSpan.FromMilliseconds(50), 0, effects, sessionId: new(session)));
        var world = new RenderWorld();
        Assert.True(world.Update(snapshots));
        return world;
    }
}
