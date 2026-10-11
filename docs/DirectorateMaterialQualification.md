# Directorate material review

Baseline: `57c2883`, native RTX PRO 5000 Blackwell Laptop, driver 32.0.15.9653,
Medium 1920x1080, yaw 45/pitch -55 degrees, 120/420/1000m. Eight warmups,
32 completed samples, eight exact repeated readbacks per image. All nine
images passed structural contracts with no fallback meshes or binding failures.
This is a local visual baseline, not an accepted performance golden.

The integrated follow-up includes master `b25f003` mechanical presentation.
Fixture version changes from 1 to 2; therefore the original captures support
qualitative comparisons but cannot isolate the cost of the material changes.
The comparison report must retain its fixture-changed classification.

Assessment grades: retained = useful role silhouette; partial = weak separation
or limited camera coverage; pending = fixture cannot establish the property.
The old IndustrialBase fixture applies 16x10x16 to every building at ground 0;
scale/proportion acceptance for all 13 is pending from that fixture. The new
three-family state gallery uses gameplay footprints and half-height placement.

| Family | Silhouette / mechanical feature at 120m | Material separation before | Decision |
|---|---|---|---|
| Command Core | partial, stacked command mass | partial, roof reads as grille | painted roof, observation glass, dedicated vent |
| Power Plant | partial, generator/exhaust mass | partial, dark roof dominates | painted roof, louvers and heat exchangers |
| Vehicle Factory | retained, wide hall and warning portal | partial, dark roof dominates | painted roof, bounded portal and roof vents |
| Smelter | partial, exhaust remains identifiable | partial, dark manufactured surfaces | retain geometry; shared material correction |
| Electronics Plant | partial, compact enclosed mass | partial, similar dark roof | retain geometry; role review remains open |
| Refinery | partial, process mass | partial, similar dark surfaces | retain geometry; role review remains open |
| Storage Depot | partial, low storage mass | partial, dark roof | retain geometry |
| Supply Depot | partial, service/storage mass | partial, dark roof | retain geometry |
| Logistics Hub | partial, loading structures | partial, dark roof | retain geometry |
| Ammunition Plant | partial, reinforced industrial mass | partial, dark roof | retain geometry |
| Barracks | retained, paired long quarters | retained, canvas/paint contrast | retain geometry |
| Radar | retained, raised sensor feature | partial at normal distance | retain geometry |
| Extractor | retained, non-box machinery | retained blue/steel distinction | retain non-box source |
| Rifle Squad | retained close; tiny at 420m | limited by projected size | retain four-person source |
| Combat Engineer | shares rifle source; identity distinction pending | same as rifle | preserve intentional reuse |
| Scout | retained, sensor-dominant | partial, glass absent | bounded glass; sensor glass follows radar |
| Tank | retained, wide tracked gun silhouette | partial, broad flat paint | hatches/hinges; original turret unchanged |
| Artillery | retained, long cannon distinct from tank | retained close | no extra invisible details |
| Cargo Truck | retained, cab and long load | partial, flat cab | glass, grille and straps |
| Supply Truck | retained, cab and service body | partial, flat cab | glass, grille and service vents |

At 420m vehicles span roughly 20–40 pixels and infantry only a few pixels in the
native 1080 image; at 1000m added fittings cannot be individually resolved.
These are visual estimates, not per-triangle projected-texel instrumentation.
All reduced unit silhouettes remain; building roof material separation carries
through existing reduced meshes. No evidence justifies adding LOD3/impostors.

Material vocabulary: intact paint metallic 0 / roughness .72; steel .88/.56;
gunmetal .85/.65; rubber 0/.94; glass 0/.23; concrete 0/.91; warning paint 0/.78.
Paint wear blends reflectance, roughness and metallic consistently toward
exposed substrate. Dirt/terrain materials remain unchanged. Existing extracted
operational/unpowered/critical indicators remain authoritative; no new emissive
decoration or baked lighting is introduced.

Atlas base/normal/ORM caps remain 2048/1024/512, BC7, terminal 128. The .018 UV
inset represents 2.304 texels at the terminal mip. Existing source 4k masters are
deterministic math, not upsampled imagery; fittings are repository-authored.
No third-party license is added. Physical texture residency 51,364,204 bytes,
atlas 7,323,648 bytes. All 341 stable IDs, collision assets and gameplay definitions
are retained. Added primary geometry 396 triangles; reduced counts unchanged.

## Integrated evidence

Final native capture source is `315120dc8abea0a20d9ace9de20442fa9b9d905c`,
build `0.1.341-dev`, runtime fingerprint
`690EEFC2DC8669F2F7D5906501D9A28FE57E731536CC15119BF51AE92E45430A`.
`artifacts/material-accepted-candidate` contains nine Medium 1080p images;
`artifacts/material-camera-candidate` contains four Medium QHD normal-distance
images and 32 additional camera poses, including MechanicalMoving. Both
manifests pass native dimensions, image hashes, exact repeated pixels and zero
fallback meshes/material/texture binding failures. Earlier local candidate
directories used an older static-part partition and are superseded.

Visual review finds clearer painted-roof/vent separation and brighter structural
steel, with unchanged vehicle role silhouettes. Glass and fittings are close-view
features; at normal/strategic distance they should not be required for recognition.
The three building state rows remain visibly distinct: operational paint,
dark unpowered treatment and the pre-existing orange critical overlays.
The close gallery clips the nearest row behind the HUD; normal and strategic
views cover the full grid. Critical overlays remain stylized and do not qualify
final destruction art. Camera-pose endpoints retain material separation without
obvious atlas bleeding, but frozen images do not establish continuous shimmer
or transition acceptance. Strategic terrain edges and background placeholder
props are inherited scene limitations outside this material change.

| Scene / distance | CPU mean / p95 ms | GPU mean / p95 ms | FPS p50 | Instance draws | LOD0/1/2 |
|---|---:|---:|---:|---:|---:|
| Industrial / 120m | 14.31 / 17.03 | 13.14 / 14.58 | 71.13 | 22 | 34/0/0 |
| Industrial / 420m | 13.81 / 18.05 | 13.42 / 16.74 | 74.46 | 20 | 3/33/0 |
| Industrial / 1000m | 16.70 / 21.58 | 14.42 / 17.30 | 62.45 | 23 | 4/39/0 |
| Units / 120m | 14.24 / 17.19 | 13.22 / 15.32 | 68.21 | 43 | 26/2/0 |
| Units / 420m | 13.66 / 17.95 | 13.17 / 17.71 | 67.38 | 13 | 3/25/2 |
| Units / 1000m | 16.46 / 28.47 | 15.48 / 21.43 | 61.26 | 16 | 4/26/7 |
| Building states / 120m | 13.50 / 18.88 | 13.29 / 19.20 | 73.53 | 27 | 33/0/0 |
| Building states / 420m | 14.18 / 20.22 | 14.69 / 19.82 | 77.31 | 19 | 11/24/0 |
| Building states / 1000m | 16.01 / 19.95 | 14.51 / 20.79 | 62.12 | 21 | 4/35/0 |

Counts include background objects and state attachments. LOD selection uses
projected size as well as distance; large strategic buildings may retain LOD1.
Timing is observational: CPU includes frame reuse/Present, PNG/readbacks are
excluded, concurrent CPU validation occurred, and the original baseline predates
mechanical partitioning. These numbers establish neither a material-only speedup
nor a sustained 60 FPS guarantee. Full p50/p95/p99 data remain in the manifests.
All nine frames retain 52,707,564 resident texture bytes and 186 SRV descriptors.

Clean compilation produced all 341 assets in 705.04 seconds under concurrent
CPU load. Following final metadata regrouping, targeted recompilation and runtime
qualification produced 56,742,327 bytes: 1,797,283 mesh, 54,795,759 texture and
149,285 material bytes. The integrated mechanical baseline was 56,698,111 bytes,
so this refinement adds 44,216 bytes (0.078%) entirely to mesh payloads.
Texture/material bytes, texture caps and residency budgets are unchanged.
This is runtime package content, not a measured distribution ZIP size.

The 1,000-instance windowed smoke passed at 1600x900: 938/1,073 visible instances,
62 total measured draws including shadows, GPU 6.424ms, CPU render 17.562ms,
41.02 observed FPS, zero material/texture binding failures. Its intentional
generic stress instances use fallback geometry; this is not a 1,000-detailed-unit
benchmark. The 5120x2160 borderless run rendered around 21–23 FPS but remained
unfocused, preventing LastMeasuredRunningRates and smoke completion. It was
terminated after several minutes and is not a passing borderless qualification.
Requested native debug layer was unavailable; zero reported warnings are not
evidence of debug-layer cleanliness. No device loss was observed in these runs.

Release build passed with zero warnings/errors; project references retain all
42 project boundaries. Seventeen reference-gallery tests passed. Map qualification
and 10,000-entity headless stress passed. The terminal-match test requested up to
80,000 ticks and ended normally at tick 42,922 with CommandCoreDestroyed.

Pending acceptance: continuous zoom popping/shimmer, complete canonical-scale
13-building review, focused borderless completion, native debug layer, controlled
whole-match performance and the mandatory hosted build-test gate. No acceptance
threshold or existing test is relaxed for these limitations.
