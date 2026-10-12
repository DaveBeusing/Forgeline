# Central Divide modular environment kit

Starting master: `fad0f7a69842c5a4b96eda1524e1841255288e69`.
Existing world content already supplies conifer/scrub/grass, mineral clusters,
eleven industrial props, two world LODs, shared PBR surfaces and presentation-only
authored dressing. Roads, crossings and resource state are gameplay authority.

The representative batch adds two rock silhouettes, a branched broadleaf tree,
Jersey-style barrier, open service fence, hollow pipe elbow, loading gantry,
road-edge drain, bridge approach apron and loading slab. Upgrade the existing
ferrous deposit geometry without changing its identity, resource state or bounds.
This is a bounded batch; additional families require review of these results.

Use existing normalized world-mesh conventions: +Y up, +Z forward, prop centre
at the origin inside [-.5,.5], vegetation root at Y=0 with height 1. A reference
mesh unit is one metre; map instance scale supplies documented physical dimensions.
Import scale remains 1. No implicit scale conversion or terrain-height mutation.
Fence/road modules snap at 8m lengths; barriers at 4m. Road/bridge approach
dressing is low profile and non-blocking. Gantries preserve a clear loading bay.

Each family has an editable recipe and self-contained glTF High/Reduced exports.
One shared industrial or vegetation material per mesh/LOD; atlas cells retain
their .018 inset. Minerals use cell 11, timber 10, foliage 13, concrete 6,
steel 1 and warning strips 5. Normals are explicit; compiler generates tangents.
Broadleaf crowns use solid, closed geometry, with no alpha cards or new overdraw
policy. Simplified collision proxies are tooling metadata only, never navigation,
cover, sensor, selection or buildability authority.

New scenery must remain PresentationOnly and preserve every original key and
transform. Service-yard/natural groups respect existing clearance exclusions.
Low road-edge/bridge modules may sit at corridor boundaries as explicit authored
transitions; they do not add road edges, blockers, concealment or gameplay state.
No ruined bridge/damage state is fabricated by decoration.

Qualification will record source determinism, compiled bounds/material/LOD
references, map reproducibility, headless authority parity, native reference
views and repeated-instance cost. Static pixels are review evidence, not final
art, continuous LOD-motion, live route or sustained performance acceptance.
