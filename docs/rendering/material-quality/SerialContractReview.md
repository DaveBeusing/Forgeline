# Serial factory-contract correction

Status: **explicitly approved and applied to both shared target files**. Actual CI unittest discovery passes **9/9, exit 0**. See [serial approval](evidence/serial-contract-approval.json).

Previous [CI failure](https://github.com/DaveBeusing/Forgeline/actions/runs/39468545297/job/118786233490)
stops at the historical full-factory source hash test, before normal smoke/full
suite checks. The material implementation intentionally changes private HLSL
function bodies in its assigned factory files, while retaining the GPU ABI.
The existing Contracts paragraph also freezes exact source bodies. These two
shared files received explicit project-owner approval before application.

## Exact approved target files

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

Both the prepared copy and the actual shared test discovery pass **nine tests**. Negative
cases reject changes to b0/t1 registers, CameraPosition/Normal widths, instance
semantics, root constant count, target format and depth state; entry signatures,
following bindings and malformed shader bodies are also guarded. Historical
hashes still match exactly. The proposal does not waive native timing failures,
existing Game failures, human material/shadow decisions or required CI.

Actual command after approval:

`python -X utf8 -m unittest discover -s build/rendering/parallelization -p 'test_*.py' -v`

Result: **9 tests, 1.054 seconds, exit 0**. The retained log is indexed as
`serial-factory-contract-tests.log`. Patch application normalized CRLF to LF;
its two-file content is otherwise unchanged. Historical fixture hashes remain
unchanged. Separate human material and shadow-contact decisions are approved;
strict timing gates, gameplay failures and remote CI remain independent.
