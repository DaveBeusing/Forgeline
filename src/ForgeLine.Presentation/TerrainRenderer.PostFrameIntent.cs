using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public sealed partial class TerrainRenderer
{
    // Late observation only. No Prepare, Select, Harvest, visibility test or lazy asset resolution.
    public GraphicsPostFrameTerrainChunk[] CapturePostFrameIntent(GraphicsPostFrameRoster roster)
    {
        ArgumentNullException.ThrowIfNull(roster); ThrowIfDisposed();
        if (_graphics.IsRecording) throw new InvalidOperationException("Terrain intent is available only after completed capture.");
        if (_resources.Count > GraphicsPostFrameLimits.TerrainChunks || _source.Patterns.Ranges.Length > GraphicsPostFrameLimits.IndexRangesPerChunk)
            throw new InvalidDataException("Late terrain intent exceeds structural capacity.");
        var result = new GraphicsPostFrameTerrainChunk[_resources.Count];
        for (int order = 0; order < _resources.Count; order++)
        {
            var chunk = _resources[order];
            var references = new List<GraphicsPostFrameResourceReference>(19)
            {
                new(roster.AddBuffer("terrain-resident-vertex", chunk.VertexBuffer), "vertex"),
                new(roster.AddBuffer("terrain-shared-indices", chunk.IndexBuffer), "shared-indices"),
                new(roster.AddTexture("terrain-control", chunk.ControlTexture), "control")
            };
            if (chunk.Layers.Length > 32) throw new InvalidDataException("Late terrain palette exceeds structural capacity.");
            var keys = new string[chunk.Layers.Length];
            for (int layer = 0; layer < chunk.Layers.Length; layer++)
            {
                var material = chunk.Layers[layer].Material;
                keys[layer] = GraphicsPostFrameLimits.ValidateOptionalString(material.MaterialId.Value) ?? string.Empty;
                references.Add(new(roster.AddTexture("terrain-palette-base-color", material.BaseColorTexture), "layer-" + layer + "-base-color"));
                references.Add(new(roster.AddTexture("terrain-palette-normal", material.NormalTexture), "layer-" + layer + "-normal"));
                references.Add(new(roster.AddTexture("terrain-palette-orm", material.OrmTexture), "layer-" + layer + "-orm"));
                references.Add(new(roster.AddTexture("terrain-palette-retained-emissive", material.EmissiveTexture), "layer-" + layer + "-retained-emissive"));
            }
            var ranges = _source.Patterns.Ranges.Select(r => new GraphicsPostFrameIndexRange(r.FirstIndex, r.IndexCount, r.Step)).ToArray();
            result[order] = new(order, chunk.ChunkIndex, chunk.Coordinate.X, chunk.Coordinate.Z, chunk.Bounds.Minimum, chunk.Bounds.Maximum,
                _mainLods[chunk.ChunkIndex], "Stored main LOD history; culled entries may be stale. Visibility is not recomputed or asserted.",
                chunk.PaletteKey, keys, ranges, references.ToArray());
        }
        return result;
    }
}
