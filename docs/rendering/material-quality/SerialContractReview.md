# Serial factory-contract correction proposal

Status: **prepared and tested as a copy; shared target files are unchanged**.

Current [CI failure](https://github.com/DaveBeusing/Forgeline/actions/runs/39468545297/job/118786233490)
stops at the historical full-factory source hash test, before normal smoke/full
suite checks. The material implementation intentionally changes private HLSL
function bodies in its assigned factory files, while retaining the GPU ABI.
The existing Contracts paragraph also freezes exact source bodies. These two
shared files require a narrow serial decision; no change is applied implicitly.

## Exact proposed target files

- `build/rendering/parallelization/test_ownership.py`
- `docs/rendering/parallelization/Contracts.md` (one contract paragraph)

The [reviewable patch](../../../build/rendering/material-quality/serial-factory-contract.patch)
preserves `FactorySources.json` and all original image goldens. The historical
mechanical extraction is checked against immutable integrated-foundation source
`17810ee192fac495173a7e9da44c973595833f2b`. The current-file comparison retains
all C# configuration and HLSL entry signatures, structs, constant-buffer/input
declarations and resource registers, masking only HLSL function implementation
statements. Root/vertex/material interfaces, settings, thresholds and workflows
are unchanged. Existing compiled-variant and native acceptance gates still apply.

The prepared copy `FactoryOwnershipProposal.py` passes **nine tests**. Negative
cases reject changes to b0/t1 registers, CameraPosition/Normal widths, instance
semantics, root constant count, target format and depth state; entry signatures,
following bindings and malformed shader bodies are also guarded. Historical
hashes still match exactly. The proposal does not waive native timing failures,
existing Game failures, human material/shadow decisions or required CI.

Command executed against the copy (with the existing validator on Python's
module path): the prepared OwnershipTests suite completes 9/9, exit 0. The
original test file remains SHA256
`0b5b3d4c6db677e3c4a5a91a741dcf7c3883cd024d44f5684e9731f97311ffbd`.
After serial approval, apply only the two-file patch, run the actual existing CI
unittest discovery, retain its result and push the correction in this PR.
