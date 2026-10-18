# Procedural PBR qualification

Baseline is remote master cd85373f5e9610cea485b5c2c1755c318cb52315,
version 0.1.399, containing the previously integrated material/LOD/tank pipelines.
Related material PRs 149, 173, 226, 227, 228, 229 and 231 were merged; no open
material-production branch needed continuation. Open vehicle-docking work is outside
this change. Implementation preserves the sixteen-cell atlas and curated generator.

The final source generation and native evidence are recorded below after execution.
Human art approval remains pending. No performance or moving-camera acceptance is
inferred from successful file or image validation.

Release solution build: 45 projects, zero warnings/errors. Ordinary Python source
contracts: 82 passed, including recipe bounds, stable source identities, map metadata,
CRC/image samples, corrupt-cache rejection and existing UV/LOD failure paths.

## Executed CPU/source qualification

Pinned Blender 5.2.2 LTS, build d13f752e3b9c, Python 3.13.13, CPU Cycles,
one thread/sample, seed 1701. Final generation key:
`271d4aa63fdf392cc12917c7b1dd65616659d316df2cb89912a4da01d5eb91dd`.
Two fresh runs produced identical source closure bytes. Verified cache reuse,
clean clone compilation and validation with an absent source root passed. The
initial clone compile took 88.927 seconds. All eight families were additionally
baked successfully, including constant ORM samples and the flat glass normal.
Reopened Blender data contains three 4096-square packed masters and three
1024-square bake controls; the saved project is 27,195,765 bytes.

The earlier experimental cache check was invalidated by producer edits made while
that experiment was running; it was not accepted. The final clean cohort above is
complete. An initial `.blend` omitted unreferenced packed images; persistent image
ownership was fixed before the final cohort and the reopen verification.

Pixel checks prove the fifteen unselected cells are unchanged in all three maps.
The selected armor cell changes in each map. Curated production sources remain
untouched and the candidate is stored separately with SHA256 lineage.

| Source map | Master baseline bytes | Pilot bytes |
| --- | ---: | ---: |
| Base Color | 3,347,911 | 4,401,677 |
| Normal | 8,679,181 | 14,695,329 |
| ORM | 4,900,868 | 7,689,127 |

The candidate is RGBA8 with opaque alpha; baseline sources were RGB. The larger
lossless source encoding does not increase the compiled mip allocation. Existing
2K/1K/512 BC7 profiles and 5/4/3 mips retain 7,323,648 atlas payload bytes exactly.
The complete catalog retains 1,357 records and 75,122,812 runtime bytes: 309 meshes,
900 textures, 127 materials, 2 skins, 18 clips and one UI skin. A full isolated
catalog compiles and validates successfully. Standard development asset build and
existing runtime process/cancellation/cache-repair/source-less contracts passed.
Twenty-four focused existing texture/BC7/mip/budget tests and 25 launcher protocol
contracts passed; ordinary source contracts passed 82 tests. Release build and the
committed native client build both report zero warnings and errors.

Sequential clean subset builds using the same compiler measured master 84.689 s
and candidate 73.155 s. The measured master window is the authoring-cost reference;
the candidate does not require a relaxed 15% comparison allowance. This is a local
observation with ordinary host activity, not a universal compilation SLA. Container
sizes, exact mip memory and source-size growth are reported separately.

## Native rendering and resource cost

Native binary/source revision: `2d8496dc2e5fcc004935b5b875081de9befa0017`,
0.1.400-dev, Release/.NET 10.0.12, Windows x64, driver 32.0.15.9653.
High 1920x1080, actual camera distances 120/420/1000m; A/B/B/A order, eight warmups,
32 samples, two readbacks per case. All twelve cases pass structural checks and
repeat-pixel equality with no fallback mesh, binding or submission failures.
The scene has 519 submitted instances; visible counts are 11/97/233. Instance draws
remain exactly 27/28/29 in both revisions. Peak texture residency remains
47,912,856 / 47,912,856 / 47,935,736 bytes. Material slots and geometry are unchanged.

| Pair / camera | CPU mean baseline -> pilot ms | GPU mean baseline -> pilot ms |
| --- | ---: | ---: |
| A/B close | 46.022 -> 47.224 | 15.190 -> 16.100 |
| A/B normal | 10.942 -> 40.144 | 12.080 -> 11.837 |
| A/B strategic | 11.202 -> 11.236 | 12.292 -> 12.317 |
| D/C close | 32.350 -> 30.209 | 15.094 -> 16.275 |
| D/C normal | 11.009 -> 10.917 | 12.075 -> 12.052 |
| D/C strategic | 11.124 -> 11.299 | 12.284 -> 12.425 |

The unchanged 15% gate FAILS: five A/B and four D/C distributions regress. A/B
includes normal-view CPU mean/p95/p99 and other CPU tails; D/C includes CPU tails
and strategic GPU p99. These failures are retained in full, without reruns to get
passing values. Both cohorts are comparable under the existing tool; no cases are
silently dropped. GPU telemetry records P8/180 MHz and active SW Power Cap 20 W
versus a default 115 W. This is an execution limitation, not proof that every
outlier is caused by the power cap. Sustained hardware performance acceptance is
open; no engine or global GPU settings were changed.

Additional matched game-camera/light detail images at 30m are labelled separately;
they do not replace the three actual RTS distances. Blender studio lighting and
game lighting are distinct. The pilot retains the existing silhouette and role
features in these inspection images; subjective surface quality, continuous zoom,
moving-camera shimmer and final human art sign-off remain pending.

## Installed player

Reproducible source-less archive: `FORGELINE-0.1.400-dev-win-x64.zip`, 54,880,998 bytes,
SHA256 `d087d3db3495d500f8cbc51682e6540dc5029b74bd320145b07f26a73edd6c02`.
All 1,481 installed file hashes passed. No Blender, compiler, Python, glTF/GLB or
`.blend` source is packaged. Native smoke exited zero from an empty working folder
with an absent Blender path. The installed 30m readback is pixel-exact with the
development pilot; the manifest and all 1,357 sealed payloads are byte-exact.
The whole-directory fingerprint differs because publishing excludes compiler cache
and stale unlisted payloads; it is not misreported as catalog corruption or equality.

The optional authoring workflow is implemented but has not been dispatched on a
remote self-hosted Blender worker. Required PR CI must complete before integration.
No merge, release or tag is performed. Production atlas promotion awaits art review
and the unresolved runtime performance qualification.

Raw data and image evidence are under [review/procedural-pbr](review/procedural-pbr/).
