using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ForgeLine.AssetCompiler;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class InfantrySkinPipelineTests
{
    [Theory]
    [InlineData("rifle_squad")][InlineData("combat_engineer")]
    public void SavedBlenderRigMatchesRuntimeAndDeploysWithoutSources(string family)
    {
        using var fixture = new Workspace(family);
        var result = fixture.Compile(); Assert.True(result.Success, string.Join(";", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(11, result.CompiledCount); Assert.Equal(11, fixture.Compile().SkippedCount);
        var catalog = RuntimeAssetCatalog.LoadCancellable(fixture.Runtime, CancellationToken.None, true);
        var skin = RuntimeSkinData.FromPayload(catalog.Read(AssetId.Parse("skin.test.infantry")).Payload);
        Assert.Equal(44, skin.Joints.Count); Assert.Equal(skin.ToPayload(), RuntimeSkinData.FromPayload(skin.ToPayload()).ToPayload());
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Repo, "tools/fixtures/infantry-skin/samples.json")));
        var samples = report.RootElement.GetProperty("families").EnumerateArray().Single(f => f.GetProperty("family").GetString() == family).GetProperty("samples");
        var palette = new Matrix4x4[RuntimeSkinData.MaximumJoints]; var output = new RuntimeMeshVertex[skin.Mesh.Vertices.Count];
        foreach (string name in Workspace.Actions)
        {
            var clip = RuntimeAnimationData.FromPayload(catalog.Read(AssetId.Parse("animation.test." + name)).Payload);
            Assert.Equal(2, BitConverter.ToInt32(clip.ToPayload()));
            int[] nodes = skin.Bind(clip);
            foreach (var sample in samples.EnumerateArray().Where(s => s.GetProperty("action").GetString() == name))
            {
                skin.Evaluate(clip, nodes, sample.GetProperty("seconds").GetSingle(), palette);
                int joint = skin.Joints.ToList().FindIndex(j => j.Name == sample.GetProperty("joint").GetString());
                Vector3 Read(string property) { var a = sample.GetProperty(property); return new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle()); }
                Assert.InRange(Vector3.Distance(Read("position"), Vector3.Transform(Read("bind"), palette[joint])), 0, .00002f);
            }
            skin.Evaluate(clip, nodes, .375f, palette); skin.Deform(palette, output);
            Assert.All(output, v => { Assert.True(float.IsFinite(v.X + v.Y + v.Z)); Assert.InRange(new Vector3(v.NormalX,v.NormalY,v.NormalZ).Length(), .9999f, 1.0001f); });
            Assert.True(float.IsFinite(skin.SocketPosition("weapon_muzzle", palette).Length()));
            for (int i = 0; i < 3; i++) { skin.Evaluate(clip,nodes,.375f,palette); skin.Deform(palette,output); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++) { skin.Evaluate(clip,nodes,.375f,palette); skin.Deform(palette,output); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Directory.Delete(fixture.Source, true);
        var qualification = RuntimeAssetQualification.Run(fixture.Runtime, true);
        Assert.True(qualification.Success); Assert.Equal(1, qualification.SkinCount); Assert.Equal(9, qualification.AnimationCount);
    }

    [Theory]
    [InlineData("joint-index")][InlineData("duplicate-joint")][InlineData("missing-bind")]
    [InlineData("singular-bind")][InlineData("weight-negative")][InlineData("weight-sum")]
    [InlineData("nonfinite")][InlineData("scale")][InlineData("cubic")][InlineData("sparse")]
    [InlineData("count-bomb")][InlineData("missing-clip")][InlineData("wrong-type")]
    [InlineData("missing-socket")][InlineData("second-weights")][InlineData("cycle")]
    public void MalformedSourcesFailWithControlledDiagnostics(string scenario)
    {
        using var f = new Workspace("rifle_squad"); var root = f.Gltf;
        var attrs = root["meshes"]![0]!["primitives"]![0]!["attributes"]!;
        int joints = attrs["JOINTS_0"]!.GetValue<int>(), weights = attrs["WEIGHTS_0"]!.GetValue<int>();
        void Float(int accessor, float value)
        {
            int view = root["accessors"]![accessor]!["bufferView"]!.GetValue<int>();
            int offset = root["bufferViews"]![view]!["byteOffset"]!.GetValue<int>() + (root["accessors"]![accessor]!["byteOffset"]?.GetValue<int>() ?? 0);
            BinaryPrimitives.WriteSingleLittleEndian(f.Binary.AsSpan(offset,4),value);
        }
        switch(scenario)
        {
            case "joint-index":
                int view = root["accessors"]![joints]!["bufferView"]!.GetValue<int>();
                f.Binary[root["bufferViews"]![view]!["byteOffset"]!.GetValue<int>()] = 255; break;
            case "duplicate-joint": root["skins"]![0]!["joints"]![1] = root["skins"]![0]!["joints"]![0]!.DeepClone(); break;
            case "missing-bind": root["skins"]![0]!.AsObject().Remove("inverseBindMatrices"); break;
            case "singular-bind": Float(root["skins"]![0]!["inverseBindMatrices"]!.GetValue<int>(),0); break;
            case "weight-negative": Float(weights,-1); break;
            case "weight-sum": Float(weights,.2f); break;
            case "nonfinite": Float(weights,float.NaN); break;
            case "scale": root["nodes"]![0]!["scale"] = new JsonArray(2,1,1); break;
            case "cubic": root["animations"]![0]!["samplers"]![0]!["interpolation"] = "CUBICSPLINE"; break;
            case "sparse": root["accessors"]![joints]!["sparse"] = new JsonObject(); break;
            case "count-bomb": root["accessors"]![joints]!["count"] = int.MaxValue; break;
            case "missing-clip": f.Skin["animationReferences"] = new JsonArray("animation.absent"); break;
            case "wrong-type": f.Skin["animationReferences"] = new JsonArray("material.test.infantry"); break;
            case "missing-socket": f.Skin["skinSockets"]![0]!["joint"] = "absent"; break;
            case "second-weights": attrs["WEIGHTS_1"] = weights; break;
            case "cycle": root["nodes"]![0]!["children"] = new JsonArray(0); break;
        }
        f.Write(); var result = f.Compile(); Assert.False(result.Success); Assert.Contains(result.Diagnostics,d => d.Severity == AssetCompilerDiagnosticSeverity.Error);
    }

    [Fact]
    public void PayloadAndBindingRejectInvalidWeightsHierarchySignaturesAndBounds()
    {
        RuntimeMeshVertex[] vertices = [new(.1f,0,0,0,1,0,0,0,1,0,0,1),new(0,.1f,0,0,1,0,0,0,1,0,0,1),new(0,0,.1f,0,1,0,0,0,1,0,0,1)];
        var mesh = new RuntimeMeshData(RuntimeMeshAttributes.Normal|RuntimeMeshAttributes.Uv0|RuntimeMeshAttributes.Tangent,vertices,[0,1,2],[],[new(0,3,-1)]);
        SkinJoint[] joints = [new("root",-1,Vector3.Zero,Quaternion.Identity,Matrix4x4.Identity)];
        SkinInfluence[] weights = Enumerable.Repeat(new SkinInfluence(0,0,0,0,new(1,0,0,0)),3).ToArray();
        var skin = new RuntimeSkinData(mesh,joints,weights,[new("weapon_muzzle",0,Vector3.Zero)]);
        foreach(var bad in new[]{new SkinInfluence(1,0,0,0,new(1,0,0,0)),new(0,0,0,0,new(.5f,0,0,0)),new(0,0,0,0,new(float.NaN,0,0,0))})
            Assert.Throws<InvalidDataException>(()=>new RuntimeSkinData(mesh,joints,[bad,..weights.Skip(1)],[]));
        Assert.Throws<InvalidDataException>(()=>new RuntimeSkinData(mesh,[joints[0] with {Parent=0}],weights,[]));
        Assert.Throws<InvalidDataException>(()=>new RuntimeSkinData(mesh,[joints[0] with {InverseBind=default}],weights,[]));
        RuntimeAnimationData Clip(float x,string? signature)=>new("walk",[new("root",-1)],[new(0,AnimationPath.Translation,AnimationInterpolation.Linear,[0,1],[Vector4.Zero,new(x,0,0,0)])],signature);
        Assert.Throws<InvalidDataException>(()=>skin.Bind(Clip(.1f,null)));
        Assert.Throws<InvalidDataException>(()=>skin.Bind(Clip(.1f,new string('a',64))));
        Assert.Throws<InvalidDataException>(()=>skin.Bind(Clip(2,RuntimeSkinData.RigSignature(joints))));
        var payload = skin.ToPayload();
        foreach(int length in new[]{0,1,20,payload.Length-1}) Assert.Throws<InvalidDataException>(()=>RuntimeSkinData.FromPayload(payload[..length]));
        Assert.Throws<InvalidDataException>(()=>RuntimeSkinData.FromPayload(new byte[RuntimeSkinData.MaximumPayloadBytes+1]));
        var json=JsonNode.Parse(payload)!;json["Version"]=99;Assert.Throws<InvalidDataException>(()=>RuntimeSkinData.FromPayload(JsonSerializer.SerializeToUtf8Bytes(json)));
        json=JsonNode.Parse(payload)!;json["Joints"]=new JsonArray(Enumerable.Range(0,49).Select(_=>json["Joints"]![0]!.DeepClone()).ToArray());
        Assert.Throws<InvalidDataException>(()=>RuntimeSkinData.FromPayload(JsonSerializer.SerializeToUtf8Bytes(json)));
    }

    private sealed class Workspace : IDisposable
    {
        internal static readonly string[] Materials=["material.test.infantry"];
        internal static readonly string[] Actions=["idle","walk","run","aim","fire","reload","hit","suppression","death"];
        readonly string _root=Path.Combine(Path.GetTempPath(),"infantry-skin-"+Guid.NewGuid().ToString("N"));
        public string Repo {get;} public string Source=>Path.Combine(_root,"source");public string Runtime=>Path.Combine(_root,"runtime");
        public JsonObject Gltf {get;} public byte[] Binary {get;} public JsonNode Skin {get;}
        public Workspace(string family)
        {
            var repo=new DirectoryInfo(AppContext.BaseDirectory);while(repo is not null&&!File.Exists(Path.Combine(repo.FullName,"ForgeLine.sln")))repo=repo.Parent;
            Repo=repo!.FullName;Directory.CreateDirectory(Source);
            byte[] bytes=File.ReadAllBytes(Path.Combine(Repo,$"assets/source/units/directorate/authoring/infantry/{family}.glb"));
            int length=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12,4));Gltf=JsonNode.Parse(bytes.AsSpan(20,length))!.AsObject();Binary=bytes[(28+length)..];
            File.WriteAllText(Path.Combine(Source,"surface.json"),"{}");
            WriteDefinition("surface",new{id="material.test.infantry",type="material",source="surface.json"});
            foreach(string action in Actions)WriteDefinition(action,new{id="animation.test."+action,type="animation",source="skin.gltf",skeletalAnimation=true,animationClip=action});
            Skin=JsonSerializer.SerializeToNode(new{id="skin.test.infantry",type="skin",source="skin.gltf",materialReferences=Materials,animationReferences=Actions.Select(a=>"animation.test."+a),sockets=new[]{new AssetSocket("weapon_muzzle",.05f,.05f,.42f)},skinSockets=new[]{new SkinSocketSource("weapon_muzzle","member_0_forearm_right")}})!;
            Write();
        }
        void WriteDefinition(string name,object value)=>File.WriteAllText(Path.Combine(Source,name+".asset.json"),JsonSerializer.Serialize(value));
        public void Write(){Gltf["buffers"]![0]!["uri"]="data:application/octet-stream;base64,"+Convert.ToBase64String(Binary);File.WriteAllText(Path.Combine(Source,"skin.gltf"),Gltf.ToJsonString());File.WriteAllText(Path.Combine(Source,"skin.asset.json"),Skin.ToJsonString());}
        public AssetCompilationResult Compile()=>AssetPipelineCompiler.Compile(Source,Runtime);
        public void Dispose(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
    }
}
