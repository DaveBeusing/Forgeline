# Directorate Command Core production brief

The existing identity is building.directorate.command_core; gameplay footprint
is 20 x 20 metres with 12-metre visual scale, centre pivot and +Y up/+Z access.
LOD0 currently has 288 triangles and LOD1/2 share the same 36-triangle source.
Its six box masses and three fittings lack recessed access, a readable
communications array, independent strategic reduction and authored construction.
Primary metadata has no sockets. The shared collision box remains unchanged.

Produce a low, wide reinforced headquarters, split command blocks surrounding
an access court, a broad communications spine, roof heat exchangers, tapered
blast walls, stepped foundation, recessed loading portal and serviced roof.
Use the current sixteen-cell PBR atlas with concrete, painted steel, structural
steel, glass, warning and equipment cells; no new texture or material family.
Keep one material section per mesh. Large roof/access landmarks survive LOD2.

Retain X [-.505,.505], Z [-.34,.48] and top Y .56 (20.2 x 16.4 x 12.72m
relative to the current transform). Deliberately extend only the foundation
bottom from Y -.34 to -.5: existing placement centres at half the 12m height,
so the old base floats 1.92m above flat ground. The new bottom seats at ground
without changing centre pivot, gameplay footprint, placement or navigation.
This is a cosmetic foundation change; collision/spatial authority is unchanged.
LOD1/2 will retain these extents to avoid silhouette/bounds jumps. No alternate
building definition, simulation changes or cosmetic collision detail.

Editable component recipes and a deterministic mesh exporter are the source.
Export standalone modular glTF pieces and dedicated assembled LOD0/1/2.
Construction and operational attachments are Command-Core-specific variants
of the existing state vocabulary; authoritative priority remains unchanged.
Destroyed geometry is a collapsed command/array wreck, not a physics object.
Socket additions are named authoring anchors only, with no new gameplay logic.

Visual acceptance requires production D3D12 renders at 120/420/1000m, normal
role read and state distinction. Compilation is not final art approval.
