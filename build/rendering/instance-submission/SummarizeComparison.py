"""Validate and summarize paired CPU observations without treating timings as gates."""
import argparse
import hashlib
import json
from pathlib import Path


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def summarize(root):
    runs = {name: read(root / f"{name}.json") for name in
            ("baseline-a", "candidate-a", "baseline-b", "candidate-b")}
    manifests = {value["AssetManifestSha256"] for value in runs.values()}
    assert len(manifests) == 1, "Assets differ"
    names = [item["Name"] for item in runs["baseline-a"]["Results"]]
    assert all([item["Name"] for item in value["Results"]] == names for value in runs.values())
    semantic = ("TotalInstances", "VisibleInstances", "CulledInstances", "DrawCalls", "HighLodInstances",
                "ReducedLodInstances", "RuntimeMeshInstances", "TexturedRuntimeMeshInstances", "FallbackMeshInstances",
                "Lod1Instances", "Lod2Instances", "AnimatedVegetationInstances", "VisibleVegetationInstances", "SkippedDecalResources")
    cases = []
    for index, name in enumerate(names):
        observations = {key: value["Results"][index] for key, value in runs.items()}
        reference = observations["baseline-a"]["Diagnostics"]
        same = all(all(value["Diagnostics"][key] == reference[key] for key in semantic) for value in observations.values())
        assert same, f"Output shape differs: {name}"
        assert len({value["Metrics"]["SubmittedInstances"] for value in observations.values()}) == 1
        cases.append({"name": name, "equivalentOutputShape": same,
            "observations": {key: {"p50Microseconds": value["P50Microseconds"], "p95Microseconds": value["P95Microseconds"],
                "p99Microseconds": value["P99Microseconds"], "allocatedBytes": sum(value["RawAllocatedBytes"]),
                "nonzeroAllocationSamples": sum(n != 0 for n in value["RawAllocatedBytes"]),
                "metrics": value["Metrics"], "diagnostics": value["Diagnostics"], "work": value["Work"]}
                for key, value in observations.items()}})
    return {"schemaVersion": 1, "status": "equivalent CPU output shapes; timing is descriptive",
        "assetManifestSha256": next(iter(manifests)), "sources": {key: value["SourceVersion"] for key, value in runs.items()},
        "originalHashes": {name: hashlib.sha256((root / f"{name}.json").read_bytes()).hexdigest() for name in runs},
        "cases": cases}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    result = summarize(args.root)
    args.output.write_text(json.dumps(result, indent=2) + "\n")
