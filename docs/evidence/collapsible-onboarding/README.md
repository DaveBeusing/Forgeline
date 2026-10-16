# Collapsible onboarding evidence

Qualified product source: `6aad37feebcce50adbb02dcd90c60cf85694fcd1`.
Version: 0.1.386-dev. Base master: `3b34a562470921bcf3efa2339b918ab9886a3636`.
[PR 220](https://github.com/DaveBeusing/Forgeline/pull/220) remains draft.

Original PNG bytes are copied without cropping, resizing or recoloring:

- [Expanded 1080p, bright terrain](expanded-1080p.png)
- [Restored full HINT label, 720p / DPI 192 / UI 2](restore-720p-high-scale.png)
- [Compact 720p / UI .75, dark terrain](compact-dark-720p.png)
- [Expanded 4K / DPI 192 / UI 2](expanded-4k.png)

[Image identity](images.json) pins source, original paths, byte counts and hashes.
The native window host DPI and injected raster DPI remain separate in manifests;
these images do not certify physical monitors or subjective motion/accessibility.

[Records archive](records.zip): 99 immutable records,
1,799,254 bytes, SHA-256
`b4756e808dfe417d392c859b0b2ad83d246537f59feee306444598f7df8ca32b`.
[Record index](records.json) pins every archived byte and original relative path.
The subsequently added early-CI geometry filter was checked separately:
[11 passing geometry cases](geometry-check.log). No product code changed for that filter.

The archive retains original failed builds and the invalid overlapping-build
package attempt alongside the successful final checks. Full native sequences
remain local at `artifacts/onboarding-qualified-sequence`: 120 exact quarter-time
samples, two equal pixel readbacks per sample, 40 original PNGs from 60-Hz sampling.
Canonical references remain in `artifacts/onboarding-reference-1080`,
`onboarding-reference-dark` and `onboarding-reference-4k`: 14 originals, eight equal
readbacks each. The independent audit verifies full PNG/RGBA/region hashes,
geometry, text/skin integrity and warm texture stability. Original raw provider
logs and product binaries remain local; their relevant digests/provenance are
recorded. These candidates are not approved pixel goldens.

[Qualification and exact acceptance limits](../../OnboardingQualification.md).
