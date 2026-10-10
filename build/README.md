# Build Support

`Author-ProductionSurfaces.py` rebuilds native 4096-pixel physical texture
masters (numpy/Pillow). Run `Refine-ProductionMeshes.py` followed by
`Author-IndustrialProps.py` to reproduce the corresponding manufactured
bevels and functional prop silhouettes. Run `Author-UnitRunningGear.py` after
the bevel pass for wheels, track belts and hollow barrels. See
[Production surfaces](../docs/ProductionSurfaceMasters.md).

`Export-VisualAssetInventory.ps1` exports source/runtime bindings, LODs, collision,
sockets and source texture dimensions from the compiled catalog.
`Invoke-RtsReferenceQualification.ps1` runs twelve resolution/zoom smoke cases
with isolated settings and flags display clamps. See [RTS visual reference
qualification](../docs/RtsVisualReferenceQualification.md) for acceptance limits.

Repository-wide build configuration is centralized in the root `Directory.Build.props`, `Directory.Packages.props`, and `global.json`.

Additional build scripts belong in this directory when they become necessary. The foundation intentionally avoids wrapper scripts until they add value beyond the canonical .NET CLI commands.

`Invoke-ScalabilityQualification.ps1` captures the bounded CPU matrix, optional extended samples and opt-in native GPU reports. `Compare-ScalabilityReports.ps1` checks comparison metadata and produces advisory percentile/allocation deltas. See [Scalability qualification](../docs/ScalabilityQualification.md) for prerequisites and interpretation.
