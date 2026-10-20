using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests.SceneComposition;

[Collection("Native postprocessing")]
public sealed class CompositionNumericsNativeTests
{
    [Fact]
    public void ProductionWeightNormalizationRejectsInvalidInputsAndPreservesFiniteMass()
    {
        const string probe = """
            float4 ProbeMain(float4 position:SV_Position):SV_Target0 {
                uint index=uint(position.x)%14;
                uint nearest=index%4;
                float4 weights=0;
                float4 expected=0;
                expected[nearest]=1;
                if(index==4) weights=asfloat(uint4(1,2,3,4));
                if(index==5) weights=float4(asfloat(0x7fc00000),1,0,0);
                if(index==6) weights=float4(0,asfloat(0x7f800000),0,1);
                if(index==7) weights=float4(-.1,.4,.4,.3);
                if(index==8) weights=float4(0,0,asfloat(0xff800000),0);
                if(index==9) { weights=float4(.2,.3,.4,.1); expected=weights; }
                if(index==10) { weights=float4(1,1,2,0); expected=float4(.25,.25,.5,0); }
                if(index==11) { weights=float4(0,0,0,.25); expected=float4(0,0,0,1); }
                if(index==12) { weights=float4(.5,0,.5,0); expected=weights; }
                if(index==13) { weights=float4(0,.75,0,.25); expected=weights; }
                float4 actual=NormalizeWeights(weights,nearest);
                bool valid=all(isfinite(actual)) && all(actual>=0) &&
                    abs(dot(actual,1)-1)<.000001 && all(abs(actual-expected)<.000001);
                return valid ? float4(0,1,0,1) : float4(1,0,0,1);
            }
            """;

        AssertGreenProbes(probe, 14);
    }

    [Fact]
    public void SharedNeighborhoodAdmissionPreservesClampedBordersAndSmallDimensions()
    {
        const string probe = """
            float4 ProbeMain(float4 position:SV_Position):SV_Target0 {
                static const uint2 sizes[20]={
                    uint2(1,1),uint2(1,1),uint2(2,1),uint2(1,7),
                    uint2(2,2),uint2(2,2),uint2(2,2),uint2(2,2),
                    uint2(2,7),uint2(2,7),uint2(2,7),uint2(2,7),
                    uint2(7,5),uint2(7,5),uint2(7,5),uint2(7,5),
                    uint2(3840,2160),uint2(3840,2160),uint2(3840,2160),uint2(3840,2160)};
                static const int2 origins[20]={
                    int2(-1,-1),int2(0,0),int2(0,0),int2(0,3),
                    int2(0,0),int2(-1,0),int2(1,0),int2(0,1),
                    int2(0,0),int2(0,5),int2(1,3),int2(0,6),
                    int2(0,0),int2(5,3),int2(6,3),int2(5,4),
                    int2(0,0),int2(3838,2158),int2(3839,2158),int2(3838,2159)};
                static const bool expected[20]={
                    false,false,false,false,true,false,false,false,
                    true,true,false,false,true,true,false,false,true,true,false,false};
                uint index=uint(position.x)%20;
                bool valid=CanShareNeighborhood(origins[index],sizes[index])==expected[index];
                return valid ? float4(0,1,0,1) : float4(1,0,0,1);
            }
            """;

        AssertGreenProbes(probe, 20);
    }

    [Fact]
    public void ProductionDepthContinuityKeepsSameSurfaceAndRejectsSharpForegroundBackgroundSteps()
    {
        const string probe = """
            float4 ProbeMain(float4 position:SV_Position):SV_Target0 {
                static const float depths[8]={.2,.8,1,.2,.8,.202,.200001,1};
                static const float references[8]={.2,.8,1,.8,.2,.2,.2,.999};
                static const float expected[8]={1,1,1,0,0,.95,.999975,0};
                uint index=uint(position.x)%8;
                float actual=DepthContinuity(depths[index],references[index]);
                bool valid=isfinite(actual) && abs(actual-expected[index])<.0001;
                return valid ? float4(0,1,0,1) : float4(1,0,0,1);
            }
            """;

        AssertGreenProbes(probe, 8);
    }

    [Fact]
    public void ProductionTransferFunctionsRetainSrgbReferencePointsAndLinearInterpolation()
    {
        const string probe = """
            float4 ProbeMain(float4 position:SV_Position):SV_Target0 {
                uint index=uint(position.x)%6;
                float3 actual=0;
                float3 expected=0;
                if(index==0) { actual=Decode(float3(0,.04045,1)); expected=float3(0,.003130805,1); }
                if(index==1) { actual=Decode(float3(.125,.5,.75)); expected=float3(.014349875,.21404114,.52252155); }
                if(index==2) { actual=Encode(float3(0,.0031308,1)); expected=float3(0,.040449936,1); }
                if(index==3) { actual=Encode(float3(.25,.5,.75)); expected=float3(.5370987,.73535698,.88082502); }
                if(index==4) { actual=Encode((Decode(float3(0,0,0))+Decode(float3(1,1,1)))*.5); expected=.73535698; }
                if(index==5) { actual=Encode(Decode(float3(.125,.25,.625))); expected=float3(.125,.25,.625); }
                bool valid=all(isfinite(actual)) && all(abs(actual-expected)<.00001);
                return valid ? float4(0,1,0,1) : float4(1,0,0,1);
            }
            """;

        AssertGreenProbes(probe, 6);
    }

    private static void AssertGreenProbes(string probe, int cases)
    {
        using var platform = new WindowsPlatform();
        using var window = platform.CreateWindow(new("FORGELINE Composition Numeric Contracts", 320, 240));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
        var compiler = new DxcShaderCompiler();
        var vertex = compiler.Compile(CompositionShaderNativeTests.ProbeVertex,
            GraphicsShaderStage.Vertex, "VSMain", "CompositionNumericVertex.hlsl");
        var pixel = compiler.Compile("#define UPSAMPLE 1\n#define ATMOSPHERE 0\n#define COMPOSITION_SHARED_LINEAR 0\n" +
            SceneCompositeShader.Pixel + "\n" + probe,
            GraphicsShaderStage.Pixel, "ProbeMain", "CompositionNumericPixel.hlsl");
        using var pipeline = graphics.CreateGraphicsPipeline(new(vertex, pixel) { CullMode = GraphicsCullMode.None });
        byte[] image = graphics.CaptureFrame(new(0, 0, 0, 1), context =>
        {
            context.SetPipeline(pipeline);
            context.Draw(3);
        });
        int width = graphics.Diagnostics.Surface.Width;
        for (int index = 0; index < cases; index++)
        {
            int offset = (width + index) * 4;
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, image.AsSpan(offset, 4).ToArray());
        }
        Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
        Assert.Equal(0, graphics.Diagnostics.Resources.TextureBindingFailureCount);
    }
}
