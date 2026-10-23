# Model and texture source integrity

Run from the repository root with Python 3.12, NumPy 2.3.3 and Pillow 12.3.0:

```text
python -m unittest discover -s build/asset-audit -p 'test_*.py' -v
python build/asset-audit/source_audit.py
```

The read-only census checks every `.gltf`, `.glb`, `.png` and `.tga` under
`assets/source`, not a sample. The report includes SHA-256, decoded dimensions,
geometry counts, bounds, dependency failures and per-source diagnostics.
Structural failures return a nonzero exit status. Winding/normal disagreements,
collapsed UVs and zero-area faces remain review warnings: intentional backfaces,
proxy geometry and authored state changes must be understood before a repair.
No automatic topology, UV, normal-map orientation or source-format rewriting occurs.

`.blend` files are explicitly inventoried, not opened. Staged assets, reference
boards and historical review captures outside `assets/source` are not production
inputs and are outside this census. Existing family-specific Blender master
round trips, runtime compilation/qualification and native D3D12 captures remain
required independent checks; this report cannot grant visual acceptance.

The optional `--review-bundle` writes a mesh/JSON/buffer inspection archive under
`artifacts`. It excludes font paths and does not include texture pixels or runtime
assets. Existing CI and compiler validation are not replaced or weakened.
