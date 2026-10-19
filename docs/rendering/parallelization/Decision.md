# Decision: serial render boundaries before independent implementations

Status: implemented and integrated. Technical qualification and explicit human
native review pass. The parallel release record takes effect after PR #241 is
integrated into master. See [current acceptance](Acceptance.md).

The renderer already has functional instancing, immutable snapshots, native fence
retirement, material caches and quality gates. Independent changes need separate
implementation files and stable read/lifetime contracts before concurrency work.

Keep behavior-preserving pipeline factory extraction, separate correspondence and
conservative visibility, a copied publication mailbox and a thin owner-bound asset
facade. Reuse existing caches, GPU submission and retirement. A synchronous initialized
Upload-buffer fallback provides a useful seam for later heap/upload work. Freeze
existing vertex/binding layouts and explicit CPU frame phases. Do not introduce a
universal render graph, pooled snapshot lifetime or new async resource system.

The consequence is one serial coordination boundary with eight disjoint later owners.
Initial Prepare repeats conservative selection and can add CPU work; its remaining
synchronous stages and measurements must remain visible. Only integrated, checked
results and approved native equality permit a parallel start. Draft documentation,
path validation or successful Null graphics alone cannot release that gate.
