using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using ForgeLine.Navigation;
using ForgeLine.World;

if (args.Length != 1) throw new ArgumentException("Supply the JSON result path.");
var settings = new WorldGridSettings { ChunkSizeMeters = 512, HeightSamplesPerSide = 33 };
var terrain = new TerrainWorld(settings,
    [new TerrainChunk(new ChunkCoordinate(0, 0), new TerrainHeightfield(33, 512, new float[33 * 33]))]);
var gridSettings = new NavigationGridSettings { CellSizeMeters = 4, StaticObstacleClearanceMeters = 0.5f };
var obstacles = new AxisAlignedBounds[1024];
for (int index = 0; index < obstacles.Length; index++)
{
    float x = (index % 32) * 16 + 6;
    float z = (index / 32) * 16 + 6;
    obstacles[index] = new AxisAlignedBounds(new Vector3(x, -1, z), new Vector3(x + 3, 4, z + 3));
}
var world = NavigationWorld.Build(terrain, gridSettings: gridSettings);
var pathfinder = new HierarchicalPathfinder(world);
NavigationCapabilities capabilities = NavigationCapabilities.For(NavigationMovementClass.Wheeled);
Vector3 start = new(2, 0, 2);
for (int index = 0; index < 256; index++)
    _ = pathfinder.FindPath(start, Destination(index), capabilities);
for (int index = 0; index < 3; index++)
    _ = NavigationGridBuilder.Build(terrain, obstacles, gridSettings);

const int routeSamples = 512;
const int gridSamples = 12;
long allocated = GC.GetAllocatedBytesForCurrentThread();
long began = Stopwatch.GetTimestamp();
ulong routeSignature = 14695981039346656037;
long expanded = 0;
for (int index = 0; index < routeSamples; index++)
{
    NavigationSearchResult result = pathfinder.FindPath(start, Destination(index), capabilities);
    if (!result.Succeeded || result.Path is null) throw new InvalidOperationException("Route workload failed.");
    expanded += result.Path.Diagnostics.ExpandedLocalNodes;
    for (int cell = 0; cell < result.Path.RefinedCells.Count; cell++)
    {
        NavigationCellCoordinate coordinate = result.Path.RefinedCells[cell];
        routeSignature = Mix(Mix(routeSignature, (uint)coordinate.X), (uint)coordinate.Z);
    }
    for (int point = 0; point < result.Path.Waypoints.Count; point++)
    {
        Vector3 position = result.Path.Waypoints[point];
        routeSignature = Mix(Mix(routeSignature, BitConverter.SingleToUInt32Bits(position.X)),
            BitConverter.SingleToUInt32Bits(position.Z));
    }
}
double routeMs = Stopwatch.GetElapsedTime(began).TotalMilliseconds / routeSamples;
long routeBytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / routeSamples;
allocated = GC.GetAllocatedBytesForCurrentThread();
began = Stopwatch.GetTimestamp();
ulong gridSignature = 14695981039346656037;
for (int sample = 0; sample < gridSamples; sample++)
{
    NavigationGrid grid = NavigationGridBuilder.Build(terrain, obstacles, gridSettings);
    for (int cell = 0; cell < grid.CellCount; cell++)
        gridSignature = Mix(gridSignature, grid.GetCell(grid.GetCoordinate(cell)).StaticBlocked ? 1U : 0U);
}
double gridMs = Stopwatch.GetElapsedTime(began).TotalMilliseconds / gridSamples;
long gridBytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / gridSamples;
allocated = GC.GetAllocatedBytesForCurrentThread();
for (int sample = 0; sample < 10_000; sample++) _ = world.GetSectorGraph(capabilities);
long graphBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
var report = new
{
    Runtime = Environment.Version.ToString(),
    ProcessArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
    RouteSamples = routeSamples, GridSamples = gridSamples, Obstacles = obstacles.Length,
    GridCells = 128 * 128, RouteMilliseconds = routeMs, RouteAllocatedBytes = routeBytes,
    GridMilliseconds = gridMs, GridAllocatedBytes = gridBytes,
    CachedGraphAllocatedBytesFor10000Calls = graphBytes,
    ExpandedLocalNodes = expanded, RouteSignature = routeSignature, GridSignature = gridSignature
};
File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report));

static Vector3 Destination(int index) => index % 2 == 0 ? new Vector3(506, 0, 506) : new Vector3(506, 0, 254);
static ulong Mix(ulong hash, uint value) => unchecked((hash ^ value) * 1099511628211);
