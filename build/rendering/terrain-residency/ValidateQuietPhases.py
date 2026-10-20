"""Associate complete predefined subexperiments with original process observations.

The combined-window rejection is retained, not overridden. This reports temporal
evidence for independently invoked CPU/native protocols, never selected frames.
"""
import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path


def instant(value):
    return dt.datetime.fromisoformat(value.replace("Z", "+00:00"))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    quiet = args.root / "qualified-quiet"
    summary_path = quiet / "summary.json"
    observations_path = quiet / "observations.jsonl"
    summary = json.loads(summary_path.read_text(encoding="utf-8-sig"))
    samples = [json.loads(line) for line in observations_path.read_text(encoding="utf-8-sig").splitlines()]
    phases = []
    for mode in ("cpu", "native"):
        protocol_path = args.root / f"qualified-{mode}" / "protocol.json"
        protocol = json.loads(protocol_path.read_text(encoding="utf-8-sig"))
        start, end = instant(protocol["startedUtc"]), instant(protocol["completedUtc"])
        assert end > start
        assert list(protocol["exits"]) == protocol["order"]
        assert len(protocol["exits"]) == 4 and set(protocol["exits"].values()) == {0}
        observed = [sample for sample in samples if start <= instant(sample["utc"]) <= end]
        violations = [v for v in summary["violations"] if start <= instant(v["utc"]) <= end]
        busy = [dict(utc=sample["utc"], process=process) for sample in observed
                for process in sample["foreign"] if process["busy"]]
        phases.append(dict(mode=mode, startedUtc=protocol["startedUtc"], completedUtc=protocol["completedUtc"],
                           completeProtocol=True, observedSamples=len(observed), violations=violations, busyObservations=busy,
                           observedQuiet=(len(observed) >= 2 and not violations and not busy),
                           protocolSha256=hashlib.sha256(protocol_path.read_bytes()).hexdigest()))
    result = dict(combinedWindowQualified=summary["qualified"], originalViolations=summary["violations"],
                  summarySha256=hashlib.sha256(summary_path.read_bytes()).hexdigest(),
                  observationsSha256=hashlib.sha256(observations_path.read_bytes()).hexdigest(), phases=phases,
                  interpretation="Only complete independently invoked protocols are assessed. The original combined-window failure is unchanged; client performance acceptance remains unqualified. Bounded process observations do not exclude all activity or prove thermal equivalence.")
    if args.output.exists():
        raise FileExistsError("Use a fresh report.")
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))
