using System.Numerics;
using System.Text.Json;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class InfrastructureTrafficParityTests
{
    private static readonly JsonSerializerOptions JsonOptions=new() { IncludeFields=true };
    public static bool Enabled=>!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_INFRASTRUCTURE_TRACE_OUTPUT"));
    [Fact(Skip="Set an explicit traffic trace output path.",SkipUnless=nameof(Enabled))]
    public void AuthoritativeTrafficAndWheelMotionMatchTheRetainedBaseline()
    {
        var terrain=CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        List<object> results=[];
        foreach(float z in new float[] { 920,2200 })
        {
            Vector3[] path=[new(1486,0,z),new(1586,0,z)];
            var sample=InfrastructureReferenceScene.SimulatePath(terrain,null,path,.5f);
            results.Add(new { z,sample.Trace,sample.AcceptedCommands,sample.Truck.MechanicalPose });
        }
        string json=JsonSerializer.Serialize(results,JsonOptions);
        string? baseline=Environment.GetEnvironmentVariable("FORGELINE_INFRASTRUCTURE_TRACE_BASELINE");
        if(!string.IsNullOrWhiteSpace(baseline))Assert.Equal(File.ReadAllText(baseline),json);
        File.WriteAllText(Environment.GetEnvironmentVariable("FORGELINE_INFRASTRUCTURE_TRACE_OUTPUT")!,json);
    }
}
