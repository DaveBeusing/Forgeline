"""Summarize immutable terrain observations; never turn missing GPU data into zero."""
import argparse
import hashlib
import json
import statistics
from pathlib import Path


def statistics_ms(values, frequency=None):
    values = [v * 1000 / frequency if frequency else v for v in values if v is not None]
    if not values:
        return None
    ordered = sorted(values)
    return dict(count=len(values), mean=statistics.mean(values),
                p50=ordered[int((len(ordered) - 1) * .5)],
                p95=ordered[int((len(ordered) - 1) * .95)],
                p99=ordered[int((len(ordered) - 1) * .99)])


def summarize(path):
    report = json.loads(path.read_text(encoding="utf-8-sig"))
    result = report["Results"]
    rows = []
    if report["Mode"] == "cpu":
        for sample in result["Results"]:
            rows.append(dict(key=sample["Name"], cpu=statistics_ms(sample["RawTicks"], result["Frequency"]),
                             allocatedBytes=sum(sample["RawAllocatedBytes"])))
    else:
        assert len(result["Cases"]) == 18, "Incomplete terrain matrix"
        for case in result["Cases"]:
            assert case["RepeatPixelsEqual"], "Repeated image differs"
            gpu = case["CompletedGpuFrames"]
            assert len({f["Fence"] for f in gpu}) == len(gpu), "Duplicate GPU observation"
            rows.append(dict(key=f'{case["Site"]}-{case["Distance"]}-{case["Moving"]}',
                             cpu=statistics_ms(case["CpuTicks"], result["Frequency"]),
                             prepare=statistics_ms(case["PrepareTicks"], result["Frequency"]),
                             worldGpu=statistics_ms([f["WorldMilliseconds"] for f in gpu]),
                             shadowGpu=statistics_ms([f["ShadowMilliseconds"] for f in gpu]),
                             totalGpu=statistics_ms([f["TotalMilliseconds"] for f in gpu]),
                             mainTriangles=case["Diagnostics"]["SubmittedTriangles"],
                             work=case["Work"], resources=case["Resources"],
                             managedBytes=case["ManagedBytes"], workingSetBytes=case["WorkingSetBytes"]))
    return dict(sourceVersion=report["SourceVersion"], rawSha256=hashlib.sha256(path.read_bytes()).hexdigest(), rows=rows)


def compare(before, after):
    assert [r["key"] for r in before["rows"]] == [r["key"] for r in after["rows"]]
    rows = []
    for a, b in zip(before["rows"], after["rows"]):
        delta = {"key": a["key"], "ratios": {}}
        for metric in ("cpu", "prepare", "worldGpu", "shadowGpu", "totalGpu"):
            if a.get(metric) is None or b.get(metric) is None:
                continue
            delta["ratios"][metric] = {s: b[metric][s] / a[metric][s] if a[metric][s] else None
                                       for s in ("mean", "p50", "p95", "p99")}
        rows.append(delta)
    return rows


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    cohorts = {label: summarize(args.root / f"{label}.json")
               for label in ("baseline-a", "candidate-a", "candidate-b", "baseline-b")}
    output = {"cohorts": cohorts, "comparisons": {
        "a": compare(cohorts["baseline-a"], cohorts["candidate-a"]),
        "b": compare(cohorts["baseline-b"], cohorts["candidate-b"])},
        "interpretation": "Raw ratios only. Existing 15% gates and human image review are unchanged. Missing GPU timing is unavailable. CPU frame time includes Present and resource reuse. Process memory is not logical payload."}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    if args.output.exists():
        raise FileExistsError("Use a fresh summary.")
    args.output.write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
