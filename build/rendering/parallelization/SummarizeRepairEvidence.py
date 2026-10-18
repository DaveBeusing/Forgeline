"""Reproduce repair summaries from complete native evidence, including failed cohorts."""
import argparse
import gzip
import json
import math
import statistics
from collections import defaultdict
from pathlib import Path


def read_json(path):
    raw = path.read_bytes()
    return json.loads(gzip.decompress(raw) if path.suffix == ".gz" else raw)


def manifest(root, name):
    path = root / name / "manifest.json"
    return read_json(path if path.exists() else path.with_suffix(".json.gz"))


def distribution(values):
    values = sorted(values)
    return {"mean": statistics.mean(values), **{
        f"p{p}": values[math.ceil(p / 100 * len(values)) - 1]
        for p in (50, 95, 99)}}


def summarize_pairs(root, prefix):
    rows, gates = [], []
    for pair in (1, 2):
        before = manifest(root, f"{prefix}{pair}-before")
        after = manifest(root, f"{prefix}{pair}-after")
        assert before["runtimeFingerprint"] == after["runtimeFingerprint"]
        previous = {c["key"]: c for c in before["captures"]}
        current = {c["key"]: c for c in after["captures"]}
        assert previous.keys() == current.keys() and len(current) == 6
        gate = read_json(root / f"{prefix}{pair}-performance.json")
        assert gate["baselineCommit"] == before["commit"]
        assert gate["currentCommit"] == after["commit"]
        gates.append({"pair": pair, "regressions": gate["regressions"],
                      "unqualified": gate["unqualified"]})
        for key, case in current.items():
            baseline = previous[key]
            assert baseline["pixelSha256"] == case["pixelSha256"]
            frames = case["performance"]["cpuFrames"]
            resources = {name: {"min": min(f["resources"][name] for f in frames),
                                "max": max(f["resources"][name] for f in frames)}
                         for name in ("loadedTextureCount", "residentTextureBytes",
                                      "shaderResourceDescriptorsUsed", "textureUploadCount")}
            assert all(r["min"] == r["max"] for r in resources.values())
            assert all(f["health"]["pendingRetirementCount"] == 0 and
                       f["health"]["submissionFaultCount"] == 0 for f in frames)
            rows.append({"pair": pair, "key": key, "exactPixels": True,
                         "baselineSource": before["commit"], "repairSource": after["commit"],
                         "cpuBefore": baseline["cpu"], "cpuAfter": case["cpu"],
                         "gpuBefore": baseline["gpu"], "gpuAfter": case["gpu"],
                         "meanAllocationBytesBefore": statistics.mean(
                             f["allocatedBytes"] for f in baseline["performance"]["cpuFrames"]),
                         "meanAllocationBytesAfter": statistics.mean(f["allocatedBytes"] for f in frames),
                         "resourcePlateaus": resources})
    return {"gates": gates, "cases": rows}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path, nargs="?", default=Path("artifacts"))
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = {"schemaVersion": 2, "cohorts": {}}
    for prefix in ("foundation-serial-pair", "foundation-final-pair"):
        report["cohorts"][prefix] = summarize_pairs(args.root, prefix)
    trace = manifest(args.root, "foundation-repair-wait-trace")
    report["waitTraceSource"] = trace["commit"]
    report["waitTrace"] = []
    for case in trace["captures"]:
        frames = case["performance"]["cpuFrames"]
        metrics = [f["submission"]["assetPreparation"] for f in frames]
        wait = [m["waitMilliseconds"] for m in metrics]
        present = [m["presentMilliseconds"] for m in metrics]
        assert all(v is not None and v >= 0 for v in wait + present)
        report["waitTrace"].append({"key": case["key"], "frameElapsed": case["cpu"],
            "frameReuseWaitMs": distribution(wait), "presentCallMs": distribution(present),
            "observedWaitAndPresentMeanFraction":
                (statistics.mean(wait) + statistics.mean(present)) / case["cpu"]["mean"]})
    scheduling = read_json(args.root / "foundation-kernel-after-scheduling.json.gz")
    assert scheduling["ExitCode"] == 0 and scheduling["TargetWindowThreadId"] > 0
    assert scheduling["DroppedIntervals"] == 0
    groups = defaultdict(list)
    for interval in scheduling["Intervals"]:
        groups[f'{interval["State"]}/{interval["WaitReason"]}'].append(interval["OffCpuMilliseconds"])
    report["scheduling"] = {"scope": scheduling["Scope"], "droppedIntervals": 0,
        "groups": {key: {"count": len(values), "totalMs": sum(values), "maxMs": max(values)}
                   for key, values in groups.items()}}
    report["interpretation"] = (
        "All complete failed cohorts are retained. Frame elapsed includes waits; phase elapsed is not OS thread CPU. "
        "Kernel intervals cover the reference window owner for the whole diagnostic run. Without aligned phase "
        "markers or stacks they do not identify the native call responsible for any particular comparison tail. "
        "No failed gate is overridden by diagnostic attribution or a successful cohort.")
    output = args.output or args.root / "foundation-repair-complete-summary.json"
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    for name, cohort in report["cohorts"].items():
        print(name, [(g["pair"], len(g["regressions"]), len(g["unqualified"])) for g in cohort["gates"]])
    print("All 24 paired images exact; per-frame resource plateaus and health valid.")


if __name__ == "__main__":
    main()
