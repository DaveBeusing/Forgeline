# Model and texture source integrity

Run from the repository root with Python 3.12, NumPy 2.3.3 and Pillow 12.3.0:

```text
python -m unittest discover -s build/asset-audit -p 'test_*.py' -v
python build/asset-audit/source_audit.py
```

The read-only census checks every `.gltf`, `.glb`, `.png` and `.tga` under
`assets/source`, not a sample. Pure animation glTF is validated as animation,
not misclassified as a missing mesh. DisplayImage textures retain the runtime's
explicit linear color-space contract. The report includes SHA-256, decoded
dimensions, geometry counts, bounds, dependency failures and per-source diagnostics.

Structural failures return a nonzero exit status. Winding/normal disagreements,
collapsed UVs and zero-area faces remain review warnings: intentional backfaces,
proxy geometry and authored state changes must be understood before a repair.
No automatic topology, UV, normal-map orientation or source-format rewriting occurs.

The 22 regression tests include malformed input, typed texture conventions,
frame continuity at vertical pipe bends, valid-profile preservation, convex track
insets, regenerated smelter/artillery source parity and retained export checksums.
Original triangle/index ranges, metadata and geometric bounds remain checked.
Cross-platform normal components may differ by at most 1e-6 during regeneration;
indices and metadata compare exactly. This allowance is not used to change
unrelated canonical source bytes.

Production text exporters with retained checksums write explicit LF newlines.
This prevents Windows CRLF checksums from becoming stale when Git normalizes the
same glTF to repository LF bytes. The checksum regression covers all retained
`*exports.json` records under the production source tree.

The Python census inventories `.blend` files but does not open them. Native
master inspection is separate: `blend_audit.py` must run in the exact pinned
Blender. It opens every tracked master with embedded scripts disabled, validates
mesh copies without saving, checks finite geometry/transforms and records image
references. The original file hashes must remain unchanged. Run the manual
**Model runtime and master qualification** workflow to repeat this census and
independent Windows asset compilation/source-less validation/asset tests.

The normal **Model and texture source audit** workflow is read-only and runs on
relevant PRs and master changes. Existing required CI, compiler qualification,
family-specific Blender round trips and native D3D12 acceptance remain independent
and are not weakened or replaced. No workflow retained by this change can write
repairs or push commits.

Staged preservation masters and historical captures are not promoted into
production authority by this audit. Read
[ModelTextureIntegrity](../../docs/ModelTextureIntegrity.md) for the measured
2026-10-23 results and explicit remaining acceptance boundaries.

The optional `--review-bundle` writes a mesh/JSON/buffer inspection archive under
`artifacts`. It excludes font paths, texture pixels and runtime assets.
