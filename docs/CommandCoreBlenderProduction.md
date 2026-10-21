# Command Core Blender production source

The editable production master is
`assets/source/buildings/directorate/authoring/command_core.blend`.
It contains the existing ten mesh identities: three intact LODs, three
construction stages, unpowered shutters, damage plates, critical ribs and
the collapsed wreck. The original kit and historical glTF exports remain
provenance and bootstrap inputs. They must not replace reviewed Blender edits.

The master separates ground foundation, heavy command bunker, loading access,
protected signal spine, roof mechanical array, signal array and service piping.
Concrete bunker masses, steel roof/service parts and the protected central
spine carry the role at tactical distance. Small fittings are omitted or
simplified in the reduced LODs. All geometry and the emissive mask are original
work derived from the repository's original component recipe; shared production
surface masters retain their existing provenance.

## Stable contracts and cost

The pivot, engine +Y up/+Z forward basis, 20 x 12 x 20 metre instance scale,
three named sockets, collision reference, gameplay footprint and state priority
are unchanged. Blender uses +Z up/-Y forward and metre scale. No new gameplay
or typed mechanical identities are introduced. Modular DCC objects do not become
runtime articulated parts. Each mesh retains one material section.

| Mesh | Previous triangles | Blender triangles |
| --- | ---: | ---: |
| Intact LOD0 | 4244 | 1960 |
| Intact LOD1 | 952 | 812 |
| Intact LOD2 | 348 | 324 |
| Foundation / frame / shell | 108 / 240 / 288 | 108 / 240 / 288 |
| Unpowered / damaged | 36 / 48 | 36 / 48 |
| Critical / destroyed | 180 / 204 | 132 / 188 |

Intact bounds remain X [-.505,.505], Y [-.5,.56], Z [-.34,.48]. Existing
LOD thresholds and hysteresis remain in the renderer. The family-local checker
retains the previous triangle and geometry-byte ceilings and rejects changed
topology, UVs, normals, sections, sockets and unsupported scene data. Bounded
vertex edits are allowed when face/normal agreement and retained bounds hold.

Base Color, tangent Normal and ORM reuse the shared surface atlas and existing
BC7 runtime tiers (2048/1024/512). A family emissive mask has a 1024-pixel
editable PNG and an explicitly capped 32-pixel BC7 runtime output (1392 bytes,
six mips). Only access glass and the critical exposed-rib atlas cell emit;
unpowered shutters use non-emissive steel. This replaces the unrelated shared
status mask in this family's dependency closure. The existing selected
Core/tank texture cap remains 7,325,040 bytes; no sampler, memory or residency
limit is increased. Five family material identities and one texture identity
bring the full source catalog to 1363 assets. Other building materials are
unchanged. Construction continues using the shared structural material.

## Reopen and export

Use pinned Blender 5.2.2 x64 through the repository launcher:

```powershell
./tools/blender/Invoke-BlenderCommandCore.ps1 -OutputRoot artifacts/core-export
./tools/blender/Invoke-Blender.ps1 -Action Script `
  -Script tools/blender/tests/native_command_core_contract.py `
  -InputRoot assets/source -OutputRoot artifacts/core-contracts `
  -ScriptArguments @((Resolve-Path assets/source/buildings/directorate/authoring/command_core.blend).Path,
    (Resolve-Path tools/blender/fixtures/command_core.json).Path) -TimeoutSeconds 1800
```

Use a fresh, short output path. Export reopens the saved master; it never
regenerates or saves over it. Exported source definitions and GLBs go into
`<output>/source`. Review their diff before deliberately promoting them into
`assets/source`. Compile through the existing AssetCompiler. Do not edit
generated runtime buffers. Packed preview textures are checked by content,
color space and graph binding, independent of Blender's relative path remapping.
Material JSON and source texture hashes must match the reviewed preview.
Material text hashes accept Git's LF/CRLF checkout conversion; value changes
and changed source material references remain rejected. Binary texture checks
remain byte-exact. The legacy kit generator writes separate historical glTF
filenames; current runtime definitions reference only `blender/*.glb` outputs.
Free-form shader graphs are preview data, not a new runtime shader interface.

`-Action Author` is a one-time bootstrap into a fresh artifact directory, using
the original kit with the family-local refinement recipe. It does not open,
edit or overwrite the production master. The saved master is authoritative
after its creation. The native contract suite reopens it, rejects unsupported
edits, and checks that an intentional saved piping edit changes only LOD0.

## Runtime review and promotion

`CommandCoreProduction` retains the existing real-state gallery.
`CommandCoreSingle` and `CommandCoreRepeated` copy the extracted operational
presentation instance into exact 1/100-instance diagnostic workloads; the latter
uses all four cardinal rotations. These immutable review snapshots add no
simulation authority or gameplay scenario. Camera yaw can also inspect each
side of the state gallery. Capture the established close/normal/strategic views,
Low/Medium/High, 1080p/4K, plus dark/wet cases using
`build/Invoke-RtsReferenceQualification.ps1` after committing and rebuilding.

Technical export, compiler, image-repeatability and packaging checks do not
constitute human art approval or sustained performance acceptance. Keep the PR
draft while renderer qualification, performance/resource acceptance or human
review is open. Do not merge, release or approve new goldens on authoring checks
alone. Detailed measured evidence and limitations belong in the accompanying
qualification record.
