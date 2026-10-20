# Asset residency qualification

Status: inventory implementation; runtime residency implementation and acceptance are pending.

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
