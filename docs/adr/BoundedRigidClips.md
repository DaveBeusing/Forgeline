# Bounded rigid clip assets

## Decision

Implement optional independent rigid-node clips after the initial vehicle roll-out. Existing turret, recoil, wheel and factory motion remains supported. Clips are cosmetic and never feed simulation.

A mesh may reference one looping clip through `animationReferences`. A clip contains at most 32 named nodes, 64 translation/rotation channels, 1024 keys per channel and 8192 keys in total. Duration is bounded to 120 seconds, translations to 2 meters and compiled payload to 1 MiB. STEP and LINEAR are supported; rotations use normalized shortest-path quaternion interpolation. Skinning, morphs, animated scale, cubic interpolation and nonidentity rest transforms are rejected explicitly.

Node names and parent relationships bind to static mechanical mesh ranges. Deltas are in canonical meters about the range socket, composed in glTF model space before the instance transform. Legacy metric mechanical rotations retain their existing scale compensation. Clip meshes cannot mix code-driven articulation. Binding validates the conservative swept volume against the original mesh bounds inside the canonical unit box; gameplay and culling footprints are never expanded by animation.

Playback uses copied/interpolated active seconds from existing mechanical pose history. Pause and power loss freeze the clock, session/entity lifecycle resets remain in the existing history, wrecks/destroyed buildings use neutral pose. Existing screen-space detail weight fades motion toward neutral; unreferenced reduced LOD assets remain static. No per-instance controller or GPU animation allocation is introduced. Immutable clips are cached with mesh resources and released with that renderer session.

## Compatibility

Append asset type Animation=5 without changing existing enum values, manifest/header or mesh payload versions. The new clip binary has its own version. Old source-less assets remain readable; older players cannot load new clip assets. Compiler version changes invalidate incremental outputs once. Clip and external buffer hashes participate in the existing dependency graph, sealing and packaging.
