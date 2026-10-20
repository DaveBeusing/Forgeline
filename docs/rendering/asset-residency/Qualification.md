# Asset residency qualification

Status: implementation under qualification; complete affected suites, native cold/warm evidence and CI acceptance are pending.

Baseline master is 47f4230f487e37f699b6572354e89f800476c513, version 0.1.420.
The accepted render foundation and initialized static-buffer implementation are integrated.
No existing asset-residency branch or open PR is present; source/compiler ownership is reserved
for this change. The unrelated local ignore-file modification remains excluded.

Master CI 39421043011 fails only the version gate (expected 0.1.421, found 0.1.420);
later checks are skipped. This is not evidence of a runtime failure. The baseline solution
build passes with zero warnings/errors (20.79s), runtime validation passes, and the 26
specified renderer/extraction/index tests pass (6.040s). Baseline binaries are isolated
before source changes. The final version delta will be exactly one PATCH over current master.

The inventory distinguishes exact FLAS container bytes, payload section bytes, format-specific
logical mip bytes, effective profile mip upload bytes and a separately named RGBA8 equivalent.
Native Default-heap allocation, staging and DXGI budgets remain null until observed; container
or mip byte counts do not pretend to measure heap allocation. The gameplay gate retains the
same texture-container metric and studio-ID exclusion as window qualification, capped at 56 MiB.
No source master, profile, runtime format, shader, graphics backend or gameplay data is changed.


The current content gate genuinely passes: 57,665,206 gameplay texture-container bytes
against 58,720,256; studio splash containers are 1,786,417 bytes. All 1,357 containers
sum to 75,122,812 bytes; payload sections sum to 73,991,270 and logical texture mips to
58,735,312. These equal the existing baseline qualification totals in their respective
metrics. The historical 99,608,050-byte defect does not reproduce; no content/profile
or 4K source changes are justified by this current gate.

Seven inventory contracts pass (3.170s), including full-container positive/negative
56-MiB fixtures, BC7 block-aligned profile selection, separate RGBA8 equivalence and
studio-prefix exclusion. The first run had one invalid test fixture (DisplayImage
incorrectly declared sRGB); the fixture now follows the existing linear rule and
both logs remain retained. Inventory compilation has zero warnings/errors.


The real RuntimeWorldAssetResources facade now leases one render-owner device/catalog/profile
store shared by terrain and instances. A closed mesh/material dependency plan prepares typed
payloads before first-frame publication, in bounded startup steps; the session constructor
publishes all required GPU resources using the existing initialized-buffer API. Normal and
reference starts already construct this same session, so no frozen host/renderer ABI changes
are needed. Scope details and limits are recorded in Decision.md. There is no background GPU
work or unbounded streaming/LRU cache.

The first residency build passes with zero warnings/errors. Existing boundary/material contracts
pass all fourteen tests; expanded residency plus original boundary contracts pass all 21 cases
(2.706s). The tests delete prepared container files, then verify 1,024 warm lookups add no managed
allocations, file reads, buffers or textures; shared wrappers survive first-owner disposal, profile
and device wrappers stay distinct, pinned generations reject excess admission, missing/hash-damaged
main containers fail, cancellation never publishes partial readiness, and a retirement exception
still releases remaining wrappers without double-decrementing the scope. These are functional
contracts, not native timing or physical heap acceptance.


The full Presentation suite on 15a0c56e passes 1,424 tests, zero failures and twelve
optional skips / 1,436 total (1m10.254s). The complete solution builds with zero
warnings/errors (18.02s). The next narrow refinement validates texture usage/color
space and mesh material/LOD types before any required GPU publication. Thirteen
focused residency tests pass (6.091s), including incompatible normal/BaseColor
bindings and a small explicit resident budget that cannot evict an active texture.
The final full suite is rerun on the committed refinement before native qualification.
