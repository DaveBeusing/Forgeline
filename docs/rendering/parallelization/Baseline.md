# Render boundary baseline

Integration base: `3c61f47274f03a0ab6e3b2022f40ac8205a72eb2`, version 0.1.400.
The related Blender, material, rigid-animation and infantry changes are integrated.
No overlapping unfinished render branch is being continued. This foundation is
serialized; later ownership remains blocked until integration and required gates.

The historical CI 38928699407 failed its window qualification at 99,608,050 compiled
gameplay texture bytes against the unchanged 56 MiB cap. Current master includes
the content correction from merged production budget work. The exact current
`Invoke-WindowModeQualification.ps1 -Configuration Release -RenderStressInstances 1000`
passed before refactoring, including borderless 5120x2160 and matching surface size.
No quality profile or content change is needed here.

The last completed older master CI 39086554073 instead failed
`WorldAudioMixerTests.OwnerHeartbeatStopsNativeQueueAfterRenderPublicationStalls`
(expected 0 queued blocks, actual 1). That is an audio test outside the reserved
render files. The current master CI 39094375378 is tracked separately. It is not
reported as fixed by this foundation.

Baseline Release: zero warnings/errors; project references pass. The 26 prescribed
renderer/extraction/snapshot tests pass. Native High 1920x1080 tank and industrial
fixtures at actual 120/420/1000m all pass structural and repeated-readback checks.
Raw baseline evidence is retained under `artifacts/render-foundation-before`.

The serial owner additionally has explicit authorization to wire
`src/ForgeLine.Client/VisualReferenceCapture.cs`; it is frozen for subsequent
parallel implementations. Source/asset identities and final raw evidence follow
in the qualification report. No parallel work is released by this note.
