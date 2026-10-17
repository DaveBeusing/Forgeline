# Blender rigid motion authoring

The authoring adapter produces static component geometry plus the existing typed
`mechanicalParts` metadata. It reuses the pinned Blender launcher, source importer,
strict rest-pose exporter, compiler and rigid presentation renderer. It adds no
animation asset type, skeletal importer, player dependency or gameplay controller.

## Existing bindings and ownership

| Source family | Current rigid components | Presentation input |
| --- | --- | --- |
| Main Battle Tank | Static hull/tracks; eight wheel hubs; separate turret with cannon range | Signed completed-tick travel, visible aim and shot history |
| Scout vehicle | Six wheels, sensor radar, separate weapon/turret | Travel, cosmetic active clock, aim/elevation/recoil |
| Self-propelled artillery | Ten road wheels, separate turret/cannon | Travel, owned mission-derived aim/elevation and visible shots |
| Vehicle Factory | Body, loading door, overhead crane | Running production, power state, bounded door progress and active clock |
| Power Plant / Radar | Cooling fan / radar array | Operational power state and copied active clock |

`PresentationExtractor` already resolves production status to Operational/Idle and
power loss to Unpowered. `MechanicalPoseHistory` owns signed travel, active duration,
last-shot time and door progress at completed ticks, retaining full entity identity
and pruning invisible entities. Same-tick extraction does not advance time or lose
recoil. `RenderWorld` interpolates copied poses. `MechanicalTransforms` composes
pivots in metric space under nonuniform world scale; `MechanicalGeometryBatching`
shares equivalent translated ranges. At most 32 ranges use the existing stack
storage and GPU buffers. Reduced LODs fade to their established static silhouettes;
missing metadata keeps the ordinary static path.

Turret world yaw is also applied outside a turret asset's own part table, around
the unit's `turret_pivot`; the adapter's inspection and runtime comparison include
that outer transform. Tracks retain existing static geometry. Tread-link circulation,
independent steering and physical suspension are not supported.

## Author, edit and export

```powershell
dotnet build ForgeLine.sln --configuration Release
pwsh tools/blender/Invoke-BlenderMotion.ps1 -OutputRoot artifacts/rigid-review -RenderPreviews
# Open artifacts/rigid-review/authoring.blend in the pinned Blender.
# After editing supported part properties and saving to a separate rest project:
pwsh tools/blender/Invoke-BlenderMotion.ps1 -Action Export -BlendFile artifacts/rigid-edited.blend -OutputRoot artifacts/rigid-export
pwsh tools/blender/Test-BlenderMotion.ps1 -OutputRoot artifacts/rigid-qualified -RuntimeSeedRoot assets/runtime -RenderPreviews
python -m unittest discover -s tools/blender/tests -p 'test_*contract.py' -v
```

The fixture contract covers tank and factory with all current states, LODs and
material/texture/collision dependencies: 29 assets, 19 meshes, 11,296 triangles and
14 mechanical ranges. Other inventoried families remain supported by their existing
runtime bindings; they are not claimed as newly qualified authoring fixtures.

The rest project has separate named source collections and part frames. The tank
is visible initially; change collection/object visibility to inspect the factory
or state/LOD geometry. Per-part custom properties are:

- `fl_motion`: one existing typed kind;
- `fl_rate`: finite rate within the existing -20..20 contract;
- `fl_travel`: finite amplitude within -2..2;
- `fl_pivot_game`: a three-component canonical **game-space** position, +Y up;
- `fl_axis_description`: inspection label, not an editable runtime axis.

Edit these properties on the existing part frames. The exporter first validates
the entire original rest scene. It then rebases only reviewed mechanical pivots,
keeping static vertex positions, normals, UVs, triangle ownership and part hierarchy
intact. Direct object transforms, mesh edits, constraints, modifiers, drivers,
keyframes or arbitrary added objects fail the rest contract. Author topology or new
component ranges through the existing source kit/metadata production process first,
then reimport. This adapter edits the validated existing partitions; it does not
infer partitions from arbitrary new objects.

The explicit profile `fixtures/motion.json` limits pivot changes to 0.025 canonical
metres for these fixtures. Only mechanical `motion_*` sockets can move; gameplay
sockets and cannon/turret pivots coupled to muzzle/aim extraction remain locked.
Shared sockets require consistent edits on every referencing frame. Names,
parents, index ranges and socket bindings remain fixed. Source float values are
retained exactly when property storage introduces only float32 noise.

The exported `source/` clone contains `_rigid` GLBs for the three mechanical assets
and ordinary compiler-readable JSON. Collision, state/LOD geometry, material and
texture files are copied unchanged. Original source hashes are checked before and
after. No content is automatically migrated into `assets/source` or the runtime
cache. Review intentional metadata edits before applying source changes through
the normal content process.

## Supported motion semantics

| Kind | Existing runtime semantics |
| --- | --- |
| static | Neutral range |
| turretYaw | Game +Y rotation from copied aim yaw |
| cannon | Bounded -X elevation and -Z recoil, existing 0.35-second pulse |
| wheel | Game +X rotation from signed travel times rate |
| fan / radar | Game +Y rotation from active seconds times rate |
| door | Game +Y translation from copied normalized door progress times travel |
| crane | Game +Y sinusoidal sweep from active seconds, rate and travel |
| infantryStride | Game +X bounded oscillation from travel, rate and amplitude |

Rate is consumed by wheel, fan, radar, crane and stride. Cannon timing and door
progress speed retain their existing runtime policy; changing their rate field
does not introduce a new timing curve. Custom axes, free animation clips, skeletal
weights and arbitrary Blender shader/animation graph baking are unsupported.
The rigid contract limits remain those validated by the actual compiler and loader.

The presentation change freezes an already open door during power loss, damage,
destruction, construction or production pause. Powered Idle/NoInput/OutputFull
allows its existing bounded return toward closed. Resuming production continues
from the retained pose. Existing unpowered/damage/wreck mesh variants retain their
static geometry; retained progress controls the resumed live mesh, not an articulated
replacement of those variants. The active clock already follows production/power state;
wheel travel, aim and recoil keep their existing completed-tick inputs. No game
logic, collision, hit, production, economy, save or authoritative state is modified.

## Inspection and qualification

`authoring.blend` remains a validated rest project. `inspection.blend` is a separate
generated preview with pose keys; it cannot be exported as an authoring input.
It samples idle, forward, reverse, stopped, paused, firing, recoil recovery,
producing, unpowered, destroyed and LOD-neutral states. The 33 asset/state rows
cover 154 part matrices per export, including representative nonuniform instance
scales and outer tank turret yaw. Between-key Blender playback is an inspection
convenience, not a runtime clip or an authoritative clock.

Packed PBR previews and optional CPU Cycles idle/firing/producing PNGs are generated
only after source validation. They do not claim shader parity. The .NET qualifier
independently composes the same poses through the actual runtime, comparing all
616 matrices across two independent exports, a reopened rest project and an edited
project. It also verifies default compiled geometry, sockets, materials and batch
sharing; intentional reversed wheel rate, a mechanical pivot shift and changed door
travel compile and participate in pose parity checks.

Normal CI runs standard-library contracts and focused presentation lifecycle,
allocation and batching tests. The manual provisioned Blender workflow executes
the actual authoring/compile/runtime comparison and retains its evidence. Local
execution does not imply that workflow has been dispatched.

Every invocation requires a fresh contained output folder. Failure leaves the
launcher logs and `motion-report.json`; fix the source/profile cause and choose a
new output folder. Do not overwrite a qualified project to retry. Reimport after
source or profile hashes change, and keep generated inspection actions out of rest
projects. Existing compiler/source-less integrity and package checks remain required.
