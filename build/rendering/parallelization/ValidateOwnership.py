#!/usr/bin/env python3
"""Validate a rendering workstream ownership plan and optional Git path list.

This checks path ownership, frozen files, and the dependency graph only.
It does not prove runtime compatibility, CI success, or merge readiness.
Exit codes: 0 = valid; 2 = invalid input or ownership violation.
"""
from __future__ import annotations

import argparse
from itertools import combinations
import json
from pathlib import Path
import re
import sys
from typing import Any


class ValidationError(ValueError):
    """Invalid manifest, dependency, or changed path."""


def normalized_path(value: Any, *, prefix: bool = False) -> str:
    if not isinstance(value, str) or not value:
        raise ValidationError("Paths must be nonempty strings.")
    if any(ord(ch) < 32 for ch in value) or "\\" in value:
        raise ValidationError(f"Use unescaped POSIX paths, without control characters: {value!r}")
    if value.startswith("/") or ":" in value or any(ch in value for ch in "*?[]"):
        raise ValidationError(f"Absolute paths and glob patterns are not accepted: {value!r}")
    if prefix != value.endswith("/"):
        raise ValidationError(f"Directory prefixes must end with '/', exact files must not: {value!r}")
    parts = value.rstrip("/").split("/")
    if any(part in ("", ".", "..") for part in parts):
        raise ValidationError(f"Non-canonical path: {value!r}")
    # Windows is the production target: reject case-only collisions too.
    return value.casefold()


def string_list(obj: dict[str, Any], key: str) -> list[str]:
    value = obj.get(key)
    if not isinstance(value, list) or any(not isinstance(x, str) for x in value):
        raise ValidationError(f"{key} must be a list of strings.")
    return value


def rules(lane: dict[str, Any]) -> list[tuple[str, bool]]:
    result = [(normalized_path(x), False) for x in string_list(lane, "writeExact")]
    result += [(normalized_path(x, prefix=True), True) for x in string_list(lane, "writePrefixes")]
    if len(result) != len(set(result)):
        raise ValidationError(f"Duplicate ownership entries: {lane.get('name')}")
    return result


def contains(rule: tuple[str, bool], path: str) -> bool:
    value, prefix = rule
    return path.startswith(value) if prefix else path == value


def overlap(a: tuple[str, bool], b: tuple[str, bool]) -> bool:
    av, ap = a
    bv, bp = b
    if ap and bp:
        return av.startswith(bv) or bv.startswith(av)
    if ap:
        return bv.startswith(av)
    if bp:
        return av.startswith(bv)
    return av == bv


def validate_manifest(manifest: dict[str, Any]) -> dict[str, dict[str, Any]]:
    if not isinstance(manifest, dict) or manifest.get("schemaVersion") != 1:
        raise ValidationError("Only ownership manifest schemaVersion 1 is supported.")
    sha = manifest.get("analysisBaseSha", "")
    if not isinstance(sha, str) or not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValidationError("analysisBaseSha must be a full lowercase Git SHA.")
    lanes = manifest.get("lanes")
    if not isinstance(lanes, list) or not lanes:
        raise ValidationError("A nonempty lanes list is required.")
    by_name: dict[str, dict[str, Any]] = {}
    for lane in lanes:
        if not isinstance(lane, dict):
            raise ValidationError("Each lane must be an object.")
        name = lane.get("name")
        if not isinstance(name, str) or not re.fullmatch(r"[A-Za-z][A-Za-z0-9]+", name):
            raise ValidationError(f"Invalid technical lane name: {name!r}")
        if any(name.casefold() == n.casefold() for n in by_name):
            raise ValidationError(f"Duplicate lane name: {name}")
        if lane.get("phase") not in ("foundation", "parallel", "integration"):
            raise ValidationError(f"Invalid phase: {name}")
        rules(lane)
        string_list(lane, "dependsOn")
        by_name[name] = lane
    frozen = [(normalized_path(x), False) for x in string_list(manifest, "frozenParallelExact")]
    frozen += [(normalized_path(x, prefix=True), True) for x in string_list(manifest, "frozenParallelPrefixes")]
    suffixes = [s.casefold() for s in string_list(manifest, "frozenParallelSuffixes")]
    visiting: set[str] = set()
    visited: set[str] = set()

    def visit(name: str) -> None:
        if name in visiting:
            raise ValidationError(f"Dependency cycle at {name}")
        if name in visited:
            return
        visiting.add(name)
        for dependency in by_name[name]["dependsOn"]:
            if dependency not in by_name:
                raise ValidationError(f"Unknown dependency {dependency} in {name}")
            visit(dependency)
        visiting.remove(name)
        visited.add(name)

    for name in by_name:
        visit(name)
    parallel = [p for p in lanes if p["phase"] == "parallel"]
    for lane in parallel:
        dependencies = lane["dependsOn"]
        if not dependencies or any(by_name[d]["phase"] != "foundation" for d in dependencies):
            raise ValidationError(f"Parallel lane must depend on the foundation, not a sibling: {lane['name']}")
        for rule in rules(lane):
            if any(overlap(rule, blocked) for blocked in frozen):
                raise ValidationError(f"Frozen-path overlap in {lane['name']}: {rule[0]}")
            if not rule[1] and any(rule[0].endswith(s) for s in suffixes):
                raise ValidationError(f"Frozen project-file type in {lane['name']}: {rule[0]}")
    for a, b in combinations(parallel, 2):
        for ar in rules(a):
            for br in rules(b):
                if overlap(ar, br):
                    raise ValidationError(f"Parallel overlap: {a['name']}:{ar[0]} / {b['name']}:{br[0]}")
    return by_name


def validate_diff(manifest: dict[str, Any], lanes: dict[str, dict[str, Any]], name: str, paths: list[str]) -> int:
    lane = lanes.get(name)
    if lane is None:
        raise ValidationError(f"Unknown workstream: {name}")
    if lane["phase"] != "parallel":
        raise ValidationError("Diff validation targets parallel implementation only; serial owner slots need separate review.")
    own = rules(lane)
    frozen = [(normalized_path(x), False) for x in manifest["frozenParallelExact"]]
    frozen += [(normalized_path(x, prefix=True), True) for x in manifest["frozenParallelPrefixes"]]
    suffixes = [s.casefold() for s in manifest["frozenParallelSuffixes"]]
    count = 0
    failures: list[str] = []
    for raw in paths:
        if raw == "":
            continue
        path = normalized_path(raw)
        count += 1
        if any(contains(rule, path) for rule in frozen) or any(path.endswith(s) for s in suffixes):
            failures.append(f"Frozen during parallel implementation: {raw}")
        elif not any(contains(rule, path) for rule in own):
            failures.append(f"Outside {name} ownership: {raw}")
    if failures:
        raise ValidationError("\n".join(failures))
    return count


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=Path(__file__).resolve().parents[3] / "docs/rendering/parallelization/ParallelRenderOwnership.json")
    parser.add_argument("--package", help="Technical workstream name, as listed in the manifest")
    parser.add_argument("--diff-file", type=Path, help="UTF-8 output of git diff --name-only --no-renames")
    args = parser.parse_args()
    if bool(args.package) != bool(args.diff_file):
        parser.error("--package and --diff-file must be provided together")
    try:
        manifest = json.loads(args.manifest.read_text(encoding="utf-8-sig"))
        lanes = validate_manifest(manifest)
        parallel_count = sum(lane["phase"] == "parallel" for lane in lanes.values())
        print(f"VALID: {len(lanes)} workstreams, {parallel_count} mutually disjoint parallel lanes; dependency graph valid.")
        if args.diff_file is not None:
            count = validate_diff(manifest, lanes, args.package, args.diff_file.read_text(encoding="utf-8-sig").splitlines())
            print(f"VALID: {count} changed paths belong to {args.package}.")
        print("This is a path/dependency check, not a semantic, runtime, or merge-approval guarantee.")
        return 0
    except (OSError, UnicodeError, json.JSONDecodeError, ValidationError) as exc:
        print(f"INVALID: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
