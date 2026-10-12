# Directorate Main Battle Tank production brief

Base revision: `b67d142b1587d6dca8b5ef9b3da793a57c179825` on master.
The existing identity is `unit.directorate.main_battle_tank`; its visual scale
is 4 x 3 x 7.2m, ground movement centre offset 1.5m, +Y up and +Z forward.
Collision, navigation, combat hitboxes, strategic symbol and gameplay remain
unchanged. The four weapon/turret sockets retain their exact coordinates:
turret_pivot (0,.10,.02), gun_pivot (0,.16,.10), weapon_muzzle (0,.16,.64),
recoil_anchor (0,.16,.16). Turret yaw and cannon elevation/recoil continue to
use the existing rigid-range read-model contract, not a second animation path.

The previous chassis has 744 triangles, turret 216; LOD1/2 share cheap
36-triangle silhouettes. Chassis bounds are X +/-.49, Y [-.22,.10],
Z [-.43,.41]; turret bounds X +/-.27, Y [0,.29], Z [-.17,.64].
The old track bottom floats .84m above the terrain at the gameplay offset.
Deliberately extend the new track bottom to Y -.5 to meet sampled ground,
within the retained conservative normalized collision/selection box.
X/Z/top extents and all gameplay bounds remain unchanged. Cosmetic wheel
anchors move down to the new axle line; their stable names and motion kind
remain. They are not targeting or simulation anchors.

Author a low, wide tracked chassis with sloped wedge glacis, reinforced
shoulders, segmented guards, raised rear powerpack/grille, towing fixtures,
four road wheels per side, end idlers/sprockets and a closed tread belt.
The turret has an angular frontal wedge, rear bustle, hatches, observation
optics, smoke-launcher hardware and a long hollow cannon with thermal jacket,
trunnion/recoil hardware and a clear muzzle opening at the retained anchor.
The tank must read as a broad frontal turret/weapon platform, distinct from
the rear-mounted artillery and elevated scout sensor.

The shared four-by-four PBR atlas remains the material source: painted and
worn armor, structural steel, rubber, heat-treated steel, glass, grille and
service panels. No new texture, shader or per-tank material is required.
Authored face charts retain existing cell insets; normals are explicit and
tangents come from the compiler. Fine wear is shared material detail, not
painted lighting or a unique texture approval.

Editable component recipes and deterministic original geometry export to
standalone glTF. LOD0 separates hull and articulated turret; reduced LODs
combine neutral hull/turret/cannon and omit fittings. Road-wheel shells retain
their authored profile; hub detail uses existing bounded wheel motion.
Tread links are static authored geometry: circulating links and physical
suspension are outside the existing rigid motion contract.

Damaged/critical attachments add displaced armor and exposed service details
through existing extracted damage state. A dedicated collapsed wreck replaces
the operational strategic silhouette. Damage tint/VFX and wreck authority
remain existing behavior; no gameplay lifecycle or weapon event changes.

Qualification must include source/export determinism, compiled normals/UVs/
tangents, socket and range coverage, aim/recoil alignment, pause/session reset,
LOD/state rendering, repeated tanks, native reference displays and canonical
1,000-instance smoke. Static camera evidence is not artistic approval or
continuous lifecycle/physical-display acceptance. Results follow qualification.
