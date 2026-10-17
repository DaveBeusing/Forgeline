using System.Numerics;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class BlenderMechanicalQualificationTests
{
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_BLENDER_MOTION_ROOT"));

    [Fact(Skip="Set FORGELINE_BLENDER_MOTION_ROOT after native rigid authoring.",SkipUnless=nameof(Enabled))]
    public void BlenderPoseMatricesMatchRuntimeRigidCompositionIncludingEditedBindings()
    {
        string root=Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_BLENDER_MOTION_ROOT")!);
        int compared=0;
        foreach(string variant in new[]{"first","second","reopened","edited"})
        {
            var catalog=RuntimeAssetCatalog.Load(Path.Combine(root,variant,"runtime"));
            using var poses=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,variant,"poses.json")));
            foreach(var row in poses.RootElement.GetProperty("samples").EnumerateArray())
            {
                string id=row.GetProperty("id").GetString()!;
                string familyId=row.GetProperty("root").GetString()!;
                var family=poses.RootElement.GetProperty("families").EnumerateArray().Single(x=>x.GetProperty("root").GetString()==familyId);
                Vector3 scale=Vector(family.GetProperty("instanceScale"));
                Matrix4x4 world=Matrix4x4.CreateScale(scale)*Matrix4x4.CreateRotationY(family.GetProperty("instanceYawDegrees").GetSingle()*MathF.PI/180)*Matrix4x4.CreateTranslation(Vector(family.GetProperty("instanceTranslation")));
                var sample=row.GetProperty("sample");float aim=Number(sample,"aim");
                if(id==familyId+".turret")
                {
                    var pivot=catalog.Get(AssetId.Parse(familyId)).Sockets.Single(s=>s.Name=="turret_pivot");
                    Vector3 p=new(pivot.X,pivot.Y,pivot.Z);
                    world=Matrix4x4.CreateTranslation(-p)*MechanicalMotionSpace.Rotation(scale,Quaternion.CreateFromAxisAngle(Vector3.UnitY,aim))*Matrix4x4.CreateTranslation(p)*world;
                }
                var record=catalog.Get(AssetId.Parse(id));
                var parts=record.MechanicalParts.Select(part=>
                {
                    var socket=record.Sockets.FirstOrDefault(x=>x.Name==part.Socket);
                    return new MechanicalPartBinding(part,new(socket.X,socket.Y,socket.Z));
                }).ToArray();
                bool disabled=sample.TryGetProperty("disabled",out var flag)&&flag.GetBoolean();
                bool unit=id.StartsWith("unit.",StringComparison.Ordinal);
                var instance=new RenderInstance(new EntityId(1,1),new(Vector3.Zero,Quaternion.Identity,scale),new(1),RenderMaterialHandle.Default,RenderVisibilityMask.World)
                {
                    UnitFeature=unit?new(UnitIds.MainBattleTank,disabled?UnitPresentationDamageState.Wreck:UnitPresentationDamageState.Intact,aim):default,
                    BuildingFeature=unit?default:new(BuildingIds.VehicleFactory,disabled?BuildingPresentationState.Destroyed:BuildingPresentationState.Operational),
                    MechanicalPose=new(true,Number(sample,"travel"),Number(sample,"active"),Number(sample,"seconds",2),Number(sample,"shot",-1),Number(sample,"elevation"),Number(sample,"door"))
                };
                var matrices=new Matrix4x4[parts.Length];MechanicalTransforms.Compose(parts,instance,world,matrices,Number(sample,"weight",1));
                Assert.Equal(parts.Length,row.GetProperty("matrices").GetArrayLength());
                for(int i=0;i<parts.Length;i++)
                {
                    float[] expected=row.GetProperty("matrices")[i].EnumerateArray().Select(x=>x.GetSingle()).ToArray();
                    float[] actual=Values(matrices[i]);
                    for(int j=0;j<16;j++)Assert.True(MathF.Abs(actual[j]-expected[j])<=.00002f,$"{variant}/{id}/{sample.GetProperty("name").GetString()}/{parts[i].Part.Name}/{j}: {actual[j]} != {expected[j]}");
                    compared++;
                }
            }
        }
        Assert.Equal(616,compared);
        var edited=RuntimeAssetCatalog.Load(Path.Combine(root,"edited/runtime"));
        var tank=edited.Get(AssetId.Parse("unit.directorate.main_battle_tank"));
        Assert.Equal(-1.75f,tank.MechanicalParts.First(x=>x.Motion==MechanicalMotionKind.Wheel).Rate);
        Assert.Equal(.3f,edited.Get(AssetId.Parse("building.directorate.vehicle_factory")).MechanicalParts.Single(x=>x.Motion==MechanicalMotionKind.Door).Travel);
    }
    private static float Number(JsonElement element,string key,float fallback=0)=>element.TryGetProperty(key,out var value)?value.GetSingle():fallback;
    private static Vector3 Vector(JsonElement value)=>new(value[0].GetSingle(),value[1].GetSingle(),value[2].GetSingle());
    private static float[] Values(Matrix4x4 m)=>[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
}
