# Cinematic frontend presentation

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

The title layer is an original static composition of the existing Directorate industrial
buildings, three Main Battle Tanks and Central Divide materials. It replaces the abstract
schematic when optional artwork is ready. It is illustrative title art, not a live match,
new mode or runtime status display. Source/export/hash/provenance live in
`assets/source/branding/forgeline/menu/README.md`. No external imagery or new license grant
was introduced. The current meshes remain visible in the still; it does not claim new
high-detail models or final film-quality content.

## Ownership and availability

`FrontendMenuBackground` owns one cancellable CPU task, bounded precompiled texture data,
one image pipeline, one GPU texture and at most one small upload buffer per back buffer.
The host schedules the task only after an interactive MainMenu frame has presented. It
never gates bootstrap, studio playback/skip or menu readiness. Texture upload is prepared
outside command recording on the existing render owner; resource retirement uses the
backend's established fence-aware ownership. No world/simulation or source compiler runs
on this path. The explicit offline export command is never called by player startup.

Missing optional content, invalid texture/integrity data or unsupported GPU upload records
one diagnostic and retains the existing schematic/command interface. It does not retry I/O
or allocate resources every frame. A cancelled read may finish on its CPU worker but owns no
window/GPU resources and cannot upload after disposal. Mandatory manifest/gameplay content
errors remain actionable startup/session failures under the existing deployment contract.
Device loss uses the existing host failure path; this change does not invent device recovery.

## Composition and quality

The shared `FrontendGeometry.MenuArtwork` region is entirely visual and stays to the right
of `Navigation`. Secondary pages use `Decorative` where space remains. Artwork cannot cover
controls, header, footer, warnings or buttons. Controls pages retain the readable full-width
list. Pause preserves the battlefield and draws only its existing left-side commands and
header. Font metrics and action geometry stay shared with the adaptive frontend.

The source is 2048×1152, display-referred BC7 with mipmaps. The complete compiled container
is below 4 MiB and resident image payload is 3,145,872 bytes. High/Medium/Ultra use the full
static image; Low/software use a 1024×576 top mip. A reported device memory budget below
16 MiB selects 512×288. Quality changes replace the texture once and retire its predecessor;
resize merely changes six quad vertices. Static art introduces no movement, animation/audio
or new reduced-motion setting. Existing intro skip/preferences remain independently owned.
Top/bottom shading and opaque semantic control surfaces retain text contrast on busy art.

## Reproduction and evidence

```text
ForgeLine.Client.exe --author-menu-background <new-output.png> assets/runtime
ForgeLine.Client.exe --cinematic-menu-capture <empty-output-directory> <runtime-root>
```

Authoring uses the already shipped models through the development renderer and writes a
source image, which must then go through the normal asset compiler. Native export is not
promised pixel-identical across GPU/driver versions; the accepted source hash pins the
shipped still. Player qualification uses only precompiled assets and checks repeated output,
layout/DPI combinations, memory, window modes and zero-size restore. The capture includes
same-device fallback/cinematic frame measurements. See [qualification](CinematicFrontendQualification.md).
