# Budgeted fallback sensing (#408)

The no-index path rotates a stable list of current in-range full entity identities. It retains no historical entity cache. The existing indexed path, trace budget, confidence, range and visibility rules remain unchanged.

Visual and radar regressions exercise 129 targets against the 128-trace budget, repeated fallback runs, exact indexed/fallback contact timelines and entity-index reuse with a new generation. All pass locally.

Normal MatchRuntime composition supplies the spatial index and is unchanged by this correction; its persistence and replay contract does not require a schema change. Standalone compositions using the optional fallback retain responsibility for their own simulation-version compatibility.
