# Cancellable command replay verification

Status: implemented; final acceptance follows required CI.

Reuse the existing typed command journal, persistence envelope and reconstruction
owner. Extend playback with the save recovery progress/cancellation contract.
All failure, cancellation and mismatch paths dispose the isolated candidate;
only fully verified playback transfers ownership.

Optional schema-1 intermediate samples compare the current authoritative
snapshot after tick/control completion, omitting only the pending external
command-count domain because recovery pre-schedules later inputs. Final
verification retains the complete original hash and all lifecycle/RNG/capability
checks. Samples are bounded, copied, validated before construction and never
added to normal match recording or persistence by default.

No new saved protocol, player export, event authority or visibility policy is
introduced. Qualified build/content/runtime identity and explicit divergence
remain the determinism boundary. See [command replay](../DeterministicCommandReplay.md).
