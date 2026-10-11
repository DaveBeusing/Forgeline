using System.Numerics;
using System.Runtime.InteropServices;

namespace ForgeLine.Graphics;

// Immutable initial conditions. Position and velocity evolution are evaluated by
// the vertex shader; no particle positions are read back or written by gameplay.
[StructLayout(LayoutKind.Sequential)]
public readonly record struct GraphicsParticle(Vector4 OriginAge, Vector4 VelocityLife, Vector4 Color, Vector4 Shape)
{
    internal void Validate()
    {
        if (!Finite(OriginAge) || OriginAge.W < 0 || !Finite(VelocityLife) || VelocityLife.W is < .05f or > 4 ||
            !Finite(Color) || Color.W is < 0 or > 1 || Color.X < 0 || Color.Y < 0 || Color.Z < 0 ||
            !Finite(Shape) || Shape.X is <= 0 or > 8 || MathF.Abs(Shape.Y) > 20 || Shape.Z is < 0 or > 5)
            throw new ArgumentException("Particle initial conditions must be finite and bounded.");
    }
    private static bool Finite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
}

public sealed class GpuParticleRenderer : IDisposable
{
    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsBuffer _indices;
    private readonly IGraphicsBuffer[] _frames;
    private readonly int _capacity;
    private bool _disposed;

    public GpuParticleRenderer(IGraphicsDevice graphics, int capacity)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (capacity is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (!graphics.SupportsParticleRendering) throw new NotSupportedException("GPU particles require readable scene depth.");
        _capacity = capacity;
        graphics.ConfigureParticleDepth(true);
        _frames = new IGraphicsBuffer[graphics.Diagnostics.Surface.BufferCount];
        var compiler = new DxcShaderCompiler();
        const string shared = """
            struct Output {
                float4 Position : SV_Position; float2 Uv : TEXCOORD0;
                nointerpolation float4 Color : COLOR0;
                nointerpolation float4 Shape : TEXCOORD1;
                nointerpolation float4 DepthSettings : TEXCOORD2;
                nointerpolation float4 OutputSettings : TEXCOORD3;
            };
            """;
        var vertex = compiler.Compile(shared + """
            cbuffer Frame : register(b0) { row_major float4x4 VP; float4 Right; float4 Up; float4 DepthSettings; float4 OutputSettings; };
            struct Particle { float4 OriginAge : TEXCOORD0; float4 VelocityLife : TEXCOORD1; float4 Color : COLOR0; float4 Shape : TEXCOORD2; };
            Output VSMain(Particle p, uint vertex : SV_VertexID) {
                float2 corners[4] = { float2(-1,-1), float2(-1,1), float2(1,-1), float2(1,1) };
                float age = p.OriginAge.w; float fraction = saturate(age / p.VelocityLife.w);
                float3 position = p.OriginAge.xyz + p.VelocityLife.xyz * age;
                position.y += .5 * p.Shape.y * age * age;
                if (p.Shape.z < 1.5) position.xz += sin(age * 2 + p.Shape.w) * age * .3;
                float size = p.Shape.x * (p.Shape.z < 1.5 ? 1 + fraction : p.Shape.z == 3 ? .5 + 1.5 * fraction : 1 - fraction * .3);
                float2 corner = corners[vertex];
                Output o; o.Position = mul(float4(position + (Right.xyz * corner.x + Up.xyz * corner.y) * size,1), VP);
                o.Uv = corner; o.Color = p.Color;
                float fadeIn = p.Shape.z == 2 || p.Shape.z == 5 ? 1 : smoothstep(0,.08,fraction);
                o.Color.a *= fadeIn * (1-fraction) * (p.Shape.z == 2 ? 1-fraction : 1);
                if (p.Shape.z == 3 || p.Shape.z == 5)
                    o.Color.rgb = lerp(p.Color.rgb, float3(.13,.12,.11), smoothstep(.08,.65,fraction));
                if (p.Shape.z == 4)
                    o.Color.rgb = lerp(float3(1.1,.48,.12), float3(.22,.09,.035), fraction);
                if (p.Shape.z == 0) o.Color.rgb *= .7 + .3 * fraction;
                o.Shape = p.Shape; o.DepthSettings = DepthSettings; o.OutputSettings = OutputSettings; return o;
            }
            """, GraphicsShaderStage.Vertex, "VSMain", "ParticleVertex.hlsl");
        var pixel = compiler.Compile(shared + """
            Texture2D<float> SceneDepth : register(t15);
            float4 PSMain(Output input) : SV_Target0 {
                float radius = dot(input.Uv,input.Uv); clip(1-radius);
                float depth = SceneDepth.Load(int3(int2(input.Position.xy),0));
                clip(depth-input.Position.z);
                float sceneZ = input.DepthSettings.y / (depth-input.DepthSettings.x);
                float particleZ = input.DepthSettings.y / (input.Position.z-input.DepthSettings.x);
                float fade = depth >= .999999 ? 1 : saturate((sceneZ-particleZ) / .8);
                float coverage = pow(saturate(1-radius), input.Shape.z == 2 ? 1 : 2);
                if (input.Shape.z < 1.5) coverage *= .8 + .2 * sin(input.Uv.x * 9 + input.Uv.y * 7 + input.Shape.w);
                float alpha = input.Color.a * coverage * fade; clip(alpha-.001);
                float3 value = input.Color.rgb;
                if (input.OutputSettings.z < .5) {
                    value *= input.OutputSettings.x;
                    if (input.OutputSettings.y > .5) value = saturate((value*(2.51*value+.03))/(value*(2.43*value+.59)+.14));
                    else value = saturate(value);
                    value = lerp(1.055*pow(max(value,0),1.0/2.4)-.055,value*12.92,step(value,.0031308));
                }
                return float4(value,alpha);
            }
            """, GraphicsShaderStage.Pixel, "PSMain", "ParticlePixel.hlsl");
        IGraphicsPipeline? pipeline = null; IGraphicsBuffer? indices = null;
        try
        {
            pipeline = graphics.CreateGraphicsPipeline(new(vertex, pixel)
            {
                VertexRootConstantCount = 32, CullMode = GraphicsCullMode.None, AlphaBlendEnabled = true, SceneDepthReceiver = true,
                TargetFormat = graphics.SceneOutput.Enabled ? GraphicsFrameTargetFormat.Rgba16Float : GraphicsFrameTargetFormat.Rgba8Unorm,
                VertexElements = [new("TEXCOORD",0,GraphicsVertexElementFormat.Float4,0,0,GraphicsVertexInputRate.PerInstance,1),
                    new("TEXCOORD",1,GraphicsVertexElementFormat.Float4,16,0,GraphicsVertexInputRate.PerInstance,1),
                    new("COLOR",0,GraphicsVertexElementFormat.Float4,32,0,GraphicsVertexInputRate.PerInstance,1),
                    new("TEXCOORD",2,GraphicsVertexElementFormat.Float4,48,0,GraphicsVertexInputRate.PerInstance,1)]
            });
            indices = graphics.CreateBuffer(new(12, GraphicsBufferMemory.Upload));
            indices.SetData<ushort>([0,1,2,2,1,3]);
            for (int i = 0; i < _frames.Length; i++) _frames[i] = graphics.CreateBuffer(new(checked((ulong)capacity * 64), GraphicsBufferMemory.Upload));
            _pipeline = pipeline; _indices = indices;
        }
        catch { foreach (var frame in _frames) frame?.Dispose(); indices?.Dispose(); pipeline?.Dispose(); throw; }
    }

    public long BufferBytes => (long)_capacity * 64 * _frames.Length + 12;

    public void Render(IGraphicsCommandContext context, ReadOnlySpan<GraphicsParticle> particles, Matrix4x4 viewProjection,
        Vector3 right, Vector3 up, float projectionA, float projectionB, float exposure, bool aces, bool linearOutput)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        if (particles.Length > _capacity || (uint)context.FrameIndex >= (uint)_frames.Length) throw new ArgumentOutOfRangeException(nameof(particles));
        if (particles.IsEmpty) return;
        foreach (float value in MemoryMarshal.Cast<Matrix4x4,float>(MemoryMarshal.CreateReadOnlySpan(ref viewProjection,1)))
            if(!float.IsFinite(value)) throw new ArgumentException("Particle projection must be finite.",nameof(viewProjection));
        if(!float.IsFinite(projectionA) || !float.IsFinite(projectionB) || MathF.Abs(projectionB)<.000001f || !float.IsFinite(exposure) || exposure is < .25f or > 4)
            throw new ArgumentException("Particle depth and exposure must be finite and valid.");
        if(!FiniteBasis(right) || !FiniteBasis(up)) throw new ArgumentException("Particle camera basis must be finite and nonzero.");
        foreach(var particle in particles) particle.Validate();
        _frames[context.FrameIndex].SetData(particles);
        context.BeginParticlePass();
        try
        {
            context.SetPipeline(_pipeline);
            context.SetIndexBuffer(_indices, GraphicsIndexFormat.SixteenBit);
            context.SetVertexBuffer(_frames[context.FrameIndex], 64);
            Span<float> constants = stackalloc float[32];
            MemoryMarshal.Cast<Matrix4x4,float>(MemoryMarshal.CreateReadOnlySpan(ref viewProjection,1)).CopyTo(constants);
            constants[16]=right.X; constants[17]=right.Y; constants[18]=right.Z; constants[19]=0;
            constants[20]=up.X; constants[21]=up.Y; constants[22]=up.Z; constants[23]=0;
            constants[24]=projectionA; constants[25]=projectionB; constants[26]=0; constants[27]=0;
            constants[28]=exposure; constants[29]=aces ? 1 : 0; constants[30]=linearOutput ? 1 : 0; constants[31]=0;
            context.SetVertexConstants(constants);
            context.DrawIndexedInstanced(6, particles.Length);
        }
        finally { context.EndParticlePass(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var frame in _frames) frame.Dispose();
        _indices.Dispose(); _pipeline.Dispose(); _disposed = true;
    }
    private static bool FiniteBasis(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && value.LengthSquared() > .000001f;
}
