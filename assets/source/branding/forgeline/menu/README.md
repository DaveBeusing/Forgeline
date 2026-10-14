# Directorate title composition

Original repository-authored still composition exported from existing Directorate and
Central Divide runtime models/materials with the native PBR renderer. No external imagery,
fonts, photographs or third-party artwork were incorporated; this adds no separate license
grant. Existing repository/content notices continue to govern the underlying assets.

Subject: three Main Battle Tanks ahead of the Directorate industrial line. The source scene
uses the IndustrialBase and MainBattleTankProduction authored presentation snapshots,
repositions three visible owned tanks, and uses a low, 48-metre camera with warm directional
light, cool ambient fill, baked shadows and distance haze. It is illustrative title art, not a
live match preview, runtime status display or evidence of additional available game modes.

Re-export explicitly from a built development tree:

```
ForgeLine.Client.exe --author-menu-background <new-output.png> assets/runtime
```

The authoring path is never invoked by player startup. Keep the checked-in 2048×1152 source
when accepting a release; native export can vary across GPUs/drivers. Stable identity:
`branding.forgeline.menu.directorate`. The normal compiler emits display-referred, linear
BC7 with a mip chain. Sampling must not sRGB-decode the already tone-mapped capture.
The complete payload stays below 4 MiB and the existing 56 MiB catalog budget is unchanged.

Source PNG SHA-256: `1b70997ffa4d7a7e7cedacfa27474c3747a9c51c7e22dc9ac1ba0a7e3949246a`.

The source faithfully uses the current production meshes. It does not claim new high-detail
Directorate models or final film-quality asset fidelity. High-detail art replacement remains a
separate content review; geometry, interaction and fallback do not depend on such replacement.
