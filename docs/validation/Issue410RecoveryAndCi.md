# Recovery capacity and CI enforcement (#410)

## Supported recovery capacity (#406)

Persisted InitialEntityCapacity must be between 1 and 1,048,576, inclusive.
The allocator alone uses roughly nine MiB of element storage at the upper
boundary, before component stores, terrain, and the remaining match state.
This is a supported initial-capacity ceiling, not a whole-process memory cap.
Runtime-only hosts remain free to choose larger capacities.

Shared configuration preflight rejects excessive declarations as CorruptDocument
before resolving composition or allocating a reconstructed runtime. Reading save
and replay documents, file discovery, and direct restoration use this preflight.
Valid values are preserved exactly; no clamping or state/hash rewriting occurs.

Boundary coverage exercises 1, 8192, the maximum, maximum plus one and int.MaxValue.
Only preflight is exercised for the maximum: no million-slot runtime is created.
Both direct entry points use a composition-resolution spy to prove early failure.

## Restored master protection (#307)

On 28 October 2026, the master branch protection was restored and read back:
required status check build-test, GitHub Actions app ID 15368, strict up-to-date
checking enabled. Administrator enforcement, required pull requests, disabled
force pushes, and disabled deletion were retained.

To verify enforcement without writing to master, a temporary validation branch
at 201af6aad7ace07df21608c1d058d23b0828b070 received the identical policy.
An attempted ordinary ref update to a content-identical child commit without
checks was rejected with HTTP 422:

> Changes must be made through a pull request. Required status check "build-test" is expected.

The branch remained at its original SHA. Its temporary protection and ref were
then removed. No write-test targeted master. The rejection and read-back were
also recorded on issue #307.

Future merges must wait for successful build-test on the current PR revision.
Existing failing gameplay regression tests tracked in #405 must be repaired;
they do not justify bypassing protection or weakening the terminal-match gate.
