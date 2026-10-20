"""Summarize source-pinned equal-work CPU cohorts; never infer gameplay FPS or GPU time."""
import argparse
import hashlib
import json
from pathlib import Path
from statistics import mean, median


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def summarize(root):
    runs = {}
    for label in ("baseline-a", "candidate-a", "candidate-b", "baseline-b"):
        folder = root / label
        meta = read(folder / "run.json")
        assert meta["profileExit"] == 0
        assert meta["frameHotpathsExit"] == 0
        assert meta["combatGroupHotpathsExit"] == 0
        assert meta["tieredCompilation"] in (None, "1"), "Do not hide allocation behavior by disabling tiering."
        profiles = [read(folder / "gameplay" / f"gameplay-ratio-{ratio}.json") for ratio in (1, 3, 7)]
        for profile in profiles:
            assert profile["RuntimeAssetsProvided"]
            assert profile["Source"].endswith(meta["repositorySource"])
        runs[label] = (meta, profiles)
    signatures = {(m["harnessSha256"], m["runtimeManifestSha256"], m["runtimeContentSignature"]) for m, _ in runs.values()}
    assert len(signatures) == 1, "Harness/runtime mismatch."
    results = []
    for ratio in (1, 3, 7):
        before = [p for key, (_, ps) in runs.items() if key.startswith("baseline") for p in ps if p["RenderRatio"] == ratio]
        after = [p for key, (_, ps) in runs.items() if key.startswith("candidate") for p in ps if p["RenderRatio"] == ratio]
        shapes = {tuple((x["Tick"], x["Instances"], x["Members"]) for x in p["Observations"]) for p in before + after}
        assert len(shapes) == 1, "Different completed gameplay data."
        assert len({p["Checksum"] for p in before + after}) == 1, "Consumer values changed."
        row = {"renderRatio": ratio, "samplesPerCohort": before[0]["Samples"], "sources": sorted({p["Source"] for p in before + after})}
        for field in ("TickMicroseconds", "TickAllocated", "CaptureMicroseconds", "CaptureAllocated", "ConsumerMicroseconds", "ConsumerAllocated", "IndexRetainedBytes", "ProducerScratchRetainedBytes"):
            a = [x[field] for p in before for x in p["Observations"]]
            b = [x[field] for p in after for x in p["Observations"]]
            row[field] = {"beforeMean": mean(a), "afterMean": mean(b), "beforeMedian": median(a), "afterMedian": median(b)}
        row["reusedCombatPayloads"] = {"before": [p["ReusedCombatPayloads"] for p in before], "after": [p["ReusedCombatPayloads"] for p in after]}
        results.append(row)
    files = {str(p.relative_to(root)).replace("\\", "/"): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(root.rglob("*.json")) if p.name != "summary.json"}
    return {"schemaVersion": 1, "method": "Sequential baseline/candidate/candidate/baseline with identical real completed gameplay data, runtime assets and probe. CPU/thread bytes only; timing observational; GPU/Present unavailable. Retained index payload excludes snapshot arrays and other engine state.", "results": results, "fileSha256": files}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    result = summarize(args.root)
    (args.root / "summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result["results"], indent=2))
