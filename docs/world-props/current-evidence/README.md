# Current world prop integration qualification

Base: a2a88f06f24b6a4351b7f1f3c7a56819f0527d5d (0.1.437).
Product source: 828984660fb60b32df6609c7b686828dfa0335d2 (0.1.438).

The source was rebased after terrain and vegetation integration. All 103
existing reference-scene values are preserved; four prop scenes are appended.
The original native authoring inputs, retained rest/inspection masters, 44
GLBs, bindings and static authoring components remain byte-identical. Their
actual historical qualification stays in ../evidence and on the source branch
art/world-prop-qualification. Those reports are not relabelled.

This directory retains newly executed current-base compiler/source-less,
headless, solution, native D3D12 and installed-package observations. Main native
originals remain under artifacts/pv2; supplemental traversals under artifacts/pt2
and artifacts/pseq2; installed views under artifacts/pinst2. Selected originals
are copied byte-for-byte. All original image hashes and per-frame measurements
are retained. Timings are short observations with uncontrolled host activity;
controlled physical performance and human art/moving-LOD approval remain open.

The inherited master main CI failure is retained explicitly: its version gate
expected 0.1.438 but received 0.1.437. The prop branch carries exactly one PATCH
to 0.1.438 and its product-source CI passed all six enabled jobs. Three optional
native runner jobs were skipped.
