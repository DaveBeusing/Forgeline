using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation;
using ForgeLine.Presentation.RenderContracts;
using ForgeLine.World;

namespace ForgeLine.Rendering.Benchmarks;

internal static class ProceduralMapMeasurements
{
    private static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields=true, WriteIndented=true, PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,
        Converters={new JsonStringEnumConverter()}
    };
    private sealed record Request
    {
        public required string RuntimeRoot { get; init; }
        public required string OutputRoot { get; init; }
        public required string SourceRevision { get; init; }
        public required string Driver { get; init; }
        public ulong Seed { get; init; }=17;
        public int Side { get; init; }=12;
        public int Width { get; init; }=1920;
        public int Height { get; init; }=1080;
        public int Warmups { get; init; }=32;
        public int Samples { get; init; }=128;
        public int MotionFrames { get; init; }=16;
        public bool Software { get; init; }
        public bool RequireSourceStamp { get; init; }=true;
        public bool AuthoredControl { get; init; }
        public bool EagerTerrain { get; init; }
        public string[] Scenes { get; init; }=["yard","corridor","forest"];
        public float[] Distances { get; init; }=[120,420,1000];
        public RenderQuality Quality { get; init; }=RenderQuality.Medium;
    }

    internal static void Run(string requestPath)
    {
        using var input=File.OpenRead(requestPath);
        if(input.Length is <1 or >65536)throw new InvalidDataException("Native request exceeds its input budget.");
        var request=JsonSerializer.Deserialize<Request>(input,Json) ?? throw new InvalidDataException("Missing qualification request.");
        if(request.Side is <8 or >16 || request.Width is <320 or >3840 || request.Height is <200 or >2160 ||
            request.Warmups is <3 or >512 || request.Samples is <8 or >1024 || request.MotionFrames is <2 or >64 ||
            request.Scenes.Length is <1 or >3 || request.Scenes.Any(s=>s is not ("yard" or "corridor" or "crossing" or "forest")) ||
            request.Distances.Length is <1 or >3 || request.Distances.Any(d=>d is not (120 or 420 or 1000)) ||
            request.SourceRevision.Length!=40 || request.SourceRevision.Any(c=>!Uri.IsHexDigit(c)) ||
            string.IsNullOrWhiteSpace(request.Driver))
            throw new InvalidDataException("Invalid bounded procedural reference request.");
        string build=Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        if(request.RequireSourceStamp && !build.Contains(request.SourceRevision,StringComparison.Ordinal))
            throw new InvalidDataException("Rebuild with SourceRevisionId matching the capture request.");
        if(Directory.Exists(request.OutputRoot)||File.Exists(request.OutputRoot))throw new IOException("Use a new evidence directory.");
        Directory.CreateDirectory(request.OutputRoot);
        var captures=new List<object>();
        try
        {
            var catalog=RuntimeAssetCatalog.LoadCancellable(request.RuntimeRoot,default,requireIntegrity:true);
            var original=new GeneratedMapRecipe();
            var recipe=original with {Terrain=original.Terrain with {Generation=original.Terrain.Generation with
                {Seed=request.Seed,WidthInChunks=request.Side,HeightInChunks=request.Side}}};
            long generationStarted=Stopwatch.GetTimestamp();
            var map=request.AuthoredControl ? null : GeneratedMapCompiler.Compile(recipe,catalog.Manifest.ContentSignature);
            var composition=request.AuthoredControl ? ScenarioRegistry.Default.Resolve(SplitBasinBattlefield.CompositionKey) : map!.CreateComposition();
            double generationMs=Stopwatch.GetElapsedTime(generationStarted).TotalMilliseconds;
            var definition=request.AuthoredControl ? composition.Battlefield : map!.Definition;
            var terrain=request.AuthoredControl ? BattlefieldTerrainFactory.Create(definition,SplitBasinBattlefield.CreateTerrainRecipe()) : map!.CreateTerrain();
            string mapFingerprint=request.AuthoredControl ? ScenarioRegistry.SplitBasinMapSha256 : map!.ManifestSha256;
            var chunks=request.AuthoredControl ? [] : terrain.Chunks.Select(c=>map!.GenerateDecoration(c.Coordinate)).ToArray();
            var decorations=new ChunkDecorationSnapshot(chunks);
            var nodes=definition.RoadNodes.ToDictionary(n=>n.Key,n=>new Vector2(n.Position.X,n.Position.Z),StringComparer.Ordinal);
            var masks=request.AuthoredControl ? null : TerrainInfrastructureMask.FromRoadSegments(
                definition.RoadEdges.Select(e=>(nodes[e.SourceNodeKey],nodes[e.DestinationNodeKey])),8,4);
            var environment=request.AuthoredControl ? null : TerrainVisualProfileResolver.CreateEnvironment(terrain,recipe.Terrain.Generation);
            using var match=MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation,2026,composition:composition));
            var gateway=new PlayerCommandGateway(match.Simulation,match.Services.BuildingCommands,match.BattlefieldRuntime.MatchStateEntity);
            var extraction=new PresentationExtractionContext(match,new(1),new PresentationInteractionState(),gateway);
            var buffer=new PresentationSnapshotBuffer();
            match.Simulation.RegisterTickObserver(gateway);
            match.Simulation.RegisterTickObserver(new PresentationExtractor(buffer,extraction,catalog,request.AuthoredControl ? null : new ChunkDecorationProvider(decorations)));
            match.Simulation.RunTicks(20);
            var world=new RenderWorld();
            if(!world.Update(buffer))throw new InvalidDataException("No authorized client snapshot.");
            var forest=chunks.OrderByDescending(c=>c.Instances.ToArray().Count(i=>i.Classification==ScatterClassification.Tree))
                .ThenBy(c=>c.Coordinate.Z).ThenBy(c=>c.Coordinate.X).FirstOrDefault();
            Vector3 forestTarget=forest is null ? definition.Starts[0].Position : new(forest.Coordinate.X*256+128,0,forest.Coordinate.Z*256+128);
            if(forest is not null)
                for(int index=0;index<forest.Instances.Length;index++)
                    if(forest.Instances[index].Classification==ScatterClassification.Tree){forestTarget=forest.GetTransform(index).PositionMeters;break;}
            bool renderProbeAdded=false;
            if(!request.AuthoredControl)
            {
                var prior=world.CurrentSnapshot!;
                var known=prior.Instances.ToArray().FirstOrDefault(i=>i.UnitFeature.IsSpecified && i.Selectable.Owner.Value==1);
                if(known.UnitFeature.IsSpecified && terrain.TrySampleHeight(forestTarget.X,forestTarget.Z,out float forestHeight))
                {
                    terrain.TrySampleHeight(known.Transform.Position.X,known.Transform.Position.Z,out float originalGround);
                    float heightOffset=known.Transform.Position.Y-originalGround;
                    var probe=known with {Entity=default,DebugIdentity=0xf0000001,Selectable=default,
                        Transform=known.Transform with {Position=new(forestTarget.X+.8f,forestHeight+heightOffset,forestTarget.Z)}};
                    buffer.Publish(new(prior.Tick,prior.TickDuration,prior.SimulationEntityCount,
                        prior.Instances.ToArray().Append(probe).ToArray(),prior.Intelligence,prior.SessionId){ChunkDecorations=decorations});
                    world.Update(buffer);renderProbeAdded=true;
                }
            }
            string sourceSnapshot=Hash(JsonSerializer.SerializeToUtf8Bytes(world.CurrentSnapshot!.Instances.ToArray(),Json));
            var annotations=new DebugDraw {Enabled=true};
            foreach(var area in definition.Starts.Select(p=>p.BuildArea).Concat(definition.Sites.Select(p=>p.BuildArea)))
            {
                Vector3 Point(float x,float z){terrain.TrySampleHeight(x,z,out float h);return new(x,h+.2f,z);}
                Vector3 a=Point(area.Minimum.X,area.Minimum.Z),b=Point(area.Maximum.X,area.Minimum.Z),
                    c=Point(area.Maximum.X,area.Maximum.Z),d=Point(area.Minimum.X,area.Maximum.Z);
                annotations.Line(a,b,new(.2f,1,.35f,1));annotations.Line(b,c,new(.2f,1,.35f,1));
                annotations.Line(c,d,new(.2f,1,.35f,1));annotations.Line(d,a,new(.2f,1,.35f,1));
            }
            var buildability=new BattlefieldBuildableAreaQuery(definition);
            var yardPermission=definition.Starts.Select(start=>new
            {
                player=start.Player,buildArea=start.BuildArea,
                samplePermission=buildability.IsBuildable(start.Player,new(
                    (start.BuildArea.Minimum+start.BuildArea.Maximum)*.5f-new Vector3(1,0,1),
                    (start.BuildArea.Minimum+start.BuildArea.Maximum)*.5f+new Vector3(1,0,1)))
            }).ToArray();
            var targets=new Dictionary<string,Vector3>(StringComparer.Ordinal)
            {
                ["yard"]=definition.Starts[0].Position,
                ["crossing"]=definition.Crossings.Count>0 ? definition.Crossings[0].Position : definition.RoadNodes[definition.RoadNodes.Count/2].Position,
                ["corridor"]=definition.RoadNodes[definition.RoadNodes.Count/2].Position,
                ["forest"]=forestTarget
            };
            using var platform=new WindowsPlatform();
            using var window=platform.CreateWindow(new("FORGELINE Procedural Map Qualification",request.Width,request.Height));
            using var graphics=GraphicsDeviceFactory.CreateForWindow(window,new()
            {EnableVSync=false,EnableDebugLayer=false,EnableMemoryDiagnostics=true,ForceSoftwareAdapter=request.Software,
                AllowSoftwareAdapterFallback=request.Software});
            graphics.Resize(request.Width,request.Height);
            var settings=new RenderQualitySettings {Quality=request.Quality};
            var profile=settings.Resolve(graphics.SupportsPostProcessing,graphics.Diagnostics.Device.IsSoftwareAdapter,
                request.Width,request.Height,graphics.Diagnostics.Surface.BufferCount,graphics.AvailableMemoryBudgetBytes,
                graphics.SupportsParticleRendering,graphics.SupportsDirectionalShadows,graphics.SupportsAtmosphere);
            var lighting=profile.Environment.ResolveLighting(SceneLightingSettings.Default);
            graphics.ConfigureSceneOutput(new(profile.LinearScene,lighting.Exposure,lighting.ToneMapping==SceneToneMappingMode.AcesFitted,profile.ResolutionScale));
            graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default with {Quality=profile.Atmosphere});
            graphics.ConfigureDirectionalShadows(profile.Shadows);
            graphics.ConfigurePostProcessing(profile.PostProcessing);
            using var ground=new TerrainRenderer(definition.TerrainVisual,graphics,terrain,environment,masks,
                runtimeAssets:catalog,lighting:lighting,surfaceDetail:profile.TerrainDetail,textureDetail:profile.Textures,
                residency:request.EagerTerrain ? TerrainResidencySettings.Eager : new(true,128),environment:profile.Environment)
                {DebugChunksEnabled=false};
            using var objects=new SimpleInstanceRenderer(graphics,catalog,lighting,profile.LodDiameterScale,profile.Textures,
                profile.VegetationMotion,profile.Environment,profile.Vegetation)
                {DecorationSettings=new(){MaximumResidentChunks=128,MaximumResidentInstances=65536,MaximumAdmissionsPerPrepare=128}};
            using var debugRenderer=new DebugDrawRenderer(graphics);
            foreach(string scene in request.Scenes)
            foreach(float distance in request.Distances)
            {
                Vector3 target=targets[scene];
                if(!terrain.TrySampleHeight(target.X,target.Z,out float y))throw new InvalidDataException("Reference target outside generated terrain.");
                target.Y=y;
                var camera=new RtsCamera(new(){InitialTarget=target,InitialDistance=distance,MaximumDistance=2000,
                    InitialYawRadians=MathF.PI/4,InitialPitchRadians=-55*MathF.PI/180,VerticalFieldOfViewRadians=45*MathF.PI/180});
                var start=camera.CaptureState();
                GraphicsDirectionalShadowFrame? shadow=null;
                double seconds=2;
                void Prepare(int frame,int total,bool moving)
                {
                    float phase=moving ? frame/(float)Math.Max(1,total-1)*MathF.Tau : 0;
                    Vector3 point=target+new Vector3(384*MathF.Sin(phase),0,256*(1-MathF.Cos(phase)));
                    point.X=Math.Clamp(point.X,64,definition.Metadata.WidthMeters-64);
                    point.Z=Math.Clamp(point.Z,64,definition.Metadata.HeightMeters-64);
                    if(!terrain.TrySampleHeight(point.X,point.Z,out point.Y))throw new InvalidDataException("Camera path left terrain.");
                    camera.ApplyState(start with {Target=point,YawRadians=start.YawRadians+.3f*MathF.Sin(phase),
                        Distance=distance*(1+.2f*MathF.Sin(2*phase))});
                    seconds=2+(moving ? frame/(double)Math.Max(1,total-1)*4 : 0);
                    shadow=profile.Shadows.Enabled ? DirectionalShadowProjection.Create(camera.CaptureState(),lighting,profile.Shadows) : null;
                    ground.Prepare(camera,request.Width,request.Height,shadow);
                    objects.Prepare(camera,world,new(request.Width,request.Height,1,shadow,profile.Shadows.MaximumCasters,seconds,true));
                }
                void Draw(IGraphicsCommandContext context)
                {
                    context.BeginPass(GraphicsFramePass.World);
                    if(profile.Atmosphere!=AtmosphereQuality.Disabled)
                        context.SetAtmosphereView(SceneAtmosphereSettings.CreateView(camera,context.Width,context.Height));
                    if(shadow is { } frame)
                    {
                        context.BeginDirectionalShadowPass(frame);
                        int draws=ground.RenderShadow(context,frame,128);
                        objects.RenderShadow(context,camera,world,1,frame,Math.Max(0,profile.Shadows.MaximumCasters-draws),
                            Math.Max(0,profile.Shadows.MaximumDraws-draws),seconds);
                        context.EndDirectionalShadowPass();
                    }
                    ground.Render(context,camera);
                    objects.Render(context,camera,world,1,seconds);
                    context.BeginPass(GraphicsFramePass.Overlay);
                    debugRenderer.Render(context,camera,annotations);
                }
                for(int warmup=0;warmup<request.Warmups;warmup++)
                {
                    platform.PumpEvents();Prepare(0,request.Samples,false);
                    graphics.RenderFrame(GraphicsColor.ForgeLineClear,Draw);objects.Submit();objects.Retire();
                }
                ulong minimumFence=graphics.Diagnostics.Frame?.GpuSubmissionFence ?? 0;
                var cpu=new List<double>();var gpu=new List<double>();var fences=new HashSet<ulong>();
                var frames=new List<object>();
                for(int sample=0;sample<request.Samples;sample++)
                {
                    platform.PumpEvents();
                    long allocated=GC.GetAllocatedBytesForCurrentThread(),clock=Stopwatch.GetTimestamp();
                    Prepare(sample,request.Samples,true);
                    graphics.RenderFrame(GraphicsColor.ForgeLineClear,Draw);objects.Submit();
                    double elapsed=Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
                    var diagnostic=graphics.Diagnostics;
                    objects.ObserveFrameDiagnostics(diagnostic);objects.Retire();
                    cpu.Add(elapsed);
                    if(diagnostic.Frame is { } completed && completed.GpuSubmissionFence>minimumFence &&
                        completed.GpuSubmissionFence!=ulong.MaxValue && diagnostic.GpuFrameMilliseconds is { } duration &&
                        double.IsFinite(duration) && duration>=0 && fences.Add(completed.GpuSubmissionFence))gpu.Add(duration);
                    frames.Add(new {sample,pose=camera.CaptureState(),cpuMilliseconds=elapsed,
                        allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated,terrain=ground.LastDiagnostics,
                        instances=objects.LastDiagnostics,decoration=objects.DecorationDiagnostics,graphics=diagnostic});
                }
                string key=$"{(request.AuthoredControl ? "split-basin-control" : $"seed-{request.Seed}-{request.Side}")}-{scene}-{distance:0}";
                string directory=Path.Combine(request.OutputRoot,key);Directory.CreateDirectory(directory);
                var motion=new List<object>();
                for(int frame=0;frame<request.MotionFrames;frame++)
                {
                    platform.PumpEvents();
                    Prepare(frame,request.MotionFrames,true);
                    byte[] first=graphics.CaptureFrame(GraphicsColor.ForgeLineClear,Draw);objects.Submit();objects.Retire();
                    Prepare(frame,request.MotionFrames,true);
                    byte[] second=graphics.CaptureFrame(GraphicsColor.ForgeLineClear,Draw);objects.Submit();objects.Retire();
                    if(!first.AsSpan().SequenceEqual(second))throw new InvalidDataException("Procedural reference pixels are not repeatable.");
                    string name=$"frame-{frame:D3}.png";
                    ProceduralCaptureImage.Write(Path.Combine(directory,name),request.Width,request.Height,second);
                    motion.Add(new{frame,name,pose=camera.CaptureState(),pixelSha256=Hash(second)});
                }
                var last=graphics.Diagnostics;
                if(last.Surface.Width!=request.Width || last.Surface.Height!=request.Height ||
                    last.Resources.TextureBindingFailureCount!=0 || last.Health.SubmissionFaultCount!=0 ||
                    objects.LastDiagnostics.FallbackMeshInstances!=0 || objects.DecorationDiagnostics.Render.FallbackMeshInstances!=0 ||
                    ground.LastDiagnostics.TextureBindingsPerDraw!=13)
                    throw new InvalidDataException("Procedural reference draw, surface or resource contract failed.");
                File.WriteAllText(Path.Combine(directory,"review.html"),ReviewPage(request.MotionFrames));
                using(var stream=new FileStream(Path.Combine(directory,"frames.json"),FileMode.CreateNew))
                    JsonSerializer.Serialize(stream,new{timedFrames=frames,motionFrames=motion},Json);
                string motionFingerprint=Hash(JsonSerializer.SerializeToUtf8Bytes(motion,Json));
                string fingerprint=Hash(Encoding.UTF8.GetBytes($"{mapFingerprint}|{sourceSnapshot}|{scene}|{distance:R}|fixture=2|path=1|motion={motionFingerprint}"));
                captures.Add(new{key,fingerprint,motionFingerprint,camera=start,profile,lighting,rasterDpi=96,uiScale=1,
                    requestedDisplay=new{width=request.Width,height=request.Height},actualSurface=last.Surface,
                    run=new{runProfile="Warm",cameraPath="Traverse",cameraPathVersion=1,warmupFrames=request.Warmups},
                    cpu=Distribution(cpu),gpu=Distribution(gpu),instances=objects.LastDiagnostics,
                    decoration=objects.DecorationDiagnostics,terrain=ground.LastDiagnostics,materials=objects.MaterialDiagnostics,
                    graphics=last,repeatPixelsEqual=true,motionFrames=request.MotionFrames,
                    gpuFenceCount=fences.Count,frameEvidence=key+"/frames.json",review=key+"/review.html"});
                Console.WriteLine($"Captured {key}: CPU mean={cpu.Average():F3}ms, GPU samples={gpu.Count}, decoration={objects.DecorationDiagnostics.Render.VisibleInstances}.");
            }
            var evidence=new
            {
                schemaVersion=1,fixtureVersion=2,status=request.RequireSourceStamp ? "structural-pass" : "development-only",
                commit=request.SourceRevision,build,runtimeFingerprint=catalog.Manifest.ContentSignature,
                environment=new{os=RuntimeInformation.OSDescription,framework=RuntimeInformation.FrameworkDescription,
                    architecture=RuntimeInformation.ProcessArchitecture.ToString()},driver=request.Driver,
                software=graphics.Diagnostics.Device.IsSoftwareAdapter,samples=request.Samples,
                generationMilliseconds=generationMs,map=map?.Manifest,manifestSha256=mapFingerprint,sourceSnapshot,
                mapKind=request.AuthoredControl ? "authored-control" : "generated",crossingCount=definition.Crossings.Count,
                roadEdgeCount=definition.RoadEdges.Count,roadMaskRegions=masks?.Regions.Length ?? 0,yardPermission,renderProbeAdded,buildAreaAnnotationLines=annotations.Lines.Length,
                simulationTick=20,simulationFrozenDuringRender=true,sourceDecorationInstances=decorations.InstanceCount,
                captures,artAcceptance="pending",performanceTargetAcceptance="pending",
                interpretation="Native generated-map scene using production assets and existing client render paths; frozen viewer-authorized simulation. Timed traversals exclude readback/event pumping; CPU includes Prepare/submission/Present, GPU uses distinct completed fences. No live-match, physical-monitor, win-rate or 60 FPS acceptance."
            };
            using var output=new FileStream(Path.Combine(request.OutputRoot,"manifest.json"),FileMode.CreateNew);
            JsonSerializer.Serialize(output,evidence,Json);
        }
        catch(Exception exception)
        {
            using var failure=new FileStream(Path.Combine(request.OutputRoot,"failure.json"),FileMode.CreateNew);
            JsonSerializer.Serialize(failure,new{status="failed",error=exception.GetType().Name,message=exception.Message,captures},Json);
            throw;
        }
    }

    private static object Distribution(IEnumerable<double> source)
    {
        double[] values=source.Order().ToArray();
        if(values.Any(x=>!double.IsFinite(x)||x<0))throw new InvalidDataException("Invalid timing sample.");
        double? Percentile(double fraction)=>values.Length==0 ? null : values[(int)Math.Ceiling(fraction*(values.Length-1))];
        return new{samples=values.Length,mean=values.Length==0 ? (double?)null : values.Average(),
            p50=Percentile(.5),p95=Percentile(.95),p99=Percentile(.99)};
    }
    private static string Hash(ReadOnlySpan<byte> bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string ReviewPage(int count)=>"<!doctype html><meta charset=utf-8><title>Procedural traversal</title><style>body{background:#171a1f;color:white;font:16px sans-serif}img{max-width:98vw}input{width:70vw}</style><h1>Recorded native traversal</h1><p>Space: pause/play. Review anchors, occlusion, LOD transitions and shadows; this sequence is not automatic art acceptance.</p><img id=v><p><input id=s type=range min=0 max="+(count-1)+"><span id=n></span></p><script>let i=0,play=true;function show(){v.src='frame-'+String(i).padStart(3,'0')+'.png';s.value=i;n.textContent=' '+i;}s.oninput=()=>{play=false;i=+s.value;show()};onkeydown=e=>{if(e.code==='Space'){play=!play;e.preventDefault()}};setInterval(()=>{if(play){i=(i+1)%"+count+";show()}},125);show()</script>";
}
