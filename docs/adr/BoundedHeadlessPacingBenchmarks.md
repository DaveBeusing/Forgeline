# Bounded headless pacing benchmarks

Status: Accepted implementation contract.

## Context

Existing gameplay telemetry retains first progression events and numeric aggregates. Missing events, absent sample populations, host pauses and censored matches must not be flattened into zero values or claims about strategic intent. Decision metrics must not become another simulation authority or a source of hidden player intelligence.

## Decision

Extend the existing headless telemetry host with explicitly enabled completed-tick observation and a schema-1 pacing report. Reuse existing milestone/progress evidence and read actual owned facility, supply and capability state. Preserve nullable availability, coverage and censoring counts. Keep thresholds advisory and compare only compatible scenario/configuration/seed/budget cohorts. Include executable identity and actual stop reasons.

Bound additional visits to 4,096 dense entity entries per completed tick, participants to 16, batches to 128, and input file sizes. Retain scalar counters and bounded membership sets. Starting structures and unit-population changes cannot fabricate expansion or front movement. Capability unlock is independent from generic research completion.

## Consequences

Normal client telemetry stays unchanged; no new UI or AI behavior is introduced. Authoritative persistence schemas are unchanged. Historical telemetry without the optional benchmark block remains readable but cannot invent new pacing comparisons. Quiet windows and unit pressure are diagnostic proxies with explicit limitations. Long design windows remain aspirations until separately measured.
