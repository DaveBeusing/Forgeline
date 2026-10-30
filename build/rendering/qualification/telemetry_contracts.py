"""Strict measured telemetry seams. No provider or acceptance is fabricated here."""
from __future__ import annotations
import argparse
import base64
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import sys
from qualification import ValidationError, require, obj, items, integer, number, text, digest, utc, sha256


def hardware_conditions(value, policy, identity, now, required_since=None):
    value, policy = obj(value, "hardware observations"), obj(policy, "hardware bounds policy")
    require(type(value.get("schemaVersion")) is int and value["schemaVersion"] == 1, "Unsupported hardware observations")
    require(value.get("kind") == "physical-gpu-observations", "Physical hardware samples required")
    require(digest(value.get("hardwareIdentitySha256"), "observed hardware") == digest(identity, "expected hardware"), "Hardware identity mismatch")
    require(type(policy.get("schemaVersion")) is int and policy["schemaVersion"] == 1 and policy.get("kind") == "hardware-stability-policy", "Reviewed stability policy required")
    require(digest(policy.get("hardwareIdentitySha256"), "policy hardware") == identity, "Policy identity mismatch")
    text(policy.get("reviewer"), "policy reviewer"); text(policy.get("rationale"), "policy rationale")
    reviewed = utc(policy.get("reviewedAtUtc"), "review time")
    text(value.get("provider"), "physical observation provider")
    rows = items(value.get("samples"), "physical samples", nonempty=True)
    previous = None
    times = []
    stable = True
    for row in rows:
        row = obj(row, "physical sample")
        at = utc(row.get("observedAtUtc"), "sample time")
        require(reviewed <= at <= now and (previous is None or previous < at), "Physical samples must follow review, be ordered and not future dated")
        require(previous is None or (at - previous).total_seconds() <= 5, "Physical sample coverage has a gap")
        previous = at; times.append(at)
        for metric in ("temperatureCelsius", "clockMHz", "powerWatts"):
            measured = number(row.get(metric), metric, positive=True)
            bounds = obj(policy.get(metric), metric + " policy")
            low, high = number(bounds.get("minimum"), "minimum"), number(bounds.get("maximum"), "maximum", positive=True)
            require(low <= high, "Invalid reviewed physical bounds")
            stable &= low <= measured <= high
        require(type(row.get("throttling")) is bool, "Throttling observation must be explicit")
        require(row.get("powerSource") in ("ac", "battery"), "Physical power source is unavailable")
        stable &= not row["throttling"] and row["powerSource"] == "ac"
    require(0 <= (now - times[-1]).total_seconds() <= 5, "Physical observations are stale")
    if required_since is not None:
        require(times[0] <= required_since, "Physical trace does not cover the native run start")
    return dict(thermalState="stable" if stable else "unstable", powerSource=rows[-1]["powerSource"],
                thermalReason=None if stable else "Physical observations exceed reviewed limits or report throttling/battery power.",
                hardwareIdentitySha256=identity, hardwareObservations=value, hardwarePolicy=policy)


def initiative_ownership(manifest, package=None, paths=()):
    manifest = obj(manifest, "initiative ownership")
    require(type(manifest.get("schemaVersion")) is int and manifest["schemaVersion"] == 1 and manifest.get("kind") == "render-upgrade-ownership", "Invalid initiative ownership schema")
    digest(manifest.get("baselineSha"), "initiative baseline", 40)
    require(manifest.get("serializedVersionException") == "Directory.Build.props", "Version exception must remain explicit")
    packages = items(manifest.get("packages"), "initiative packages", nonempty=True)
    by_id = {}
    def canonical(path):
        text(path, "owned path")
        require(not path.startswith("/") and not any(c in path for c in "\\:*?[]") and all(p not in ("", ".", "..") for p in path.rstrip("/").split("/")), "Noncanonical ownership path")
        return path.casefold()
    def overlap(a,b):
        return a == b or (a.endswith("/") and b.startswith(a)) or (b.endswith("/") and a.startswith(b))
    for entry in packages:
        name = text(entry.get("id"), "package id")
        require(name not in by_id, "Duplicate package")
        wave = integer(entry.get("wave"), "wave")
        require(name.startswith(str(wave) + "_"), "Package wave/name differs")
        own = [canonical(p) for p in items(entry.get("owns"), "owned paths", nonempty=True)]
        require(len(own) == len(set(own)), "Duplicate owned path")
        by_id[name] = (wave, own, items(entry.get("dependsOn"), "dependencies"))
    for name, (wave, own, deps) in by_id.items():
        for dep in deps:
            require(dep in by_id and by_id[dep][0] < wave, "Prerequisites must be in earlier waves")
        for other, (other_wave, other_own, _) in by_id.items():
            if other > name and wave == other_wave:
                require(not any(overlap(a,b) for a in own for b in other_own), "Same-wave ownership overlap")
    if package is not None:
        require(package in by_id, "Unknown initiative package")
        for path in paths:
            path = canonical(path)
            require(path == "directory.build.props" or any(path == p or (p.endswith("/") and path.startswith(p)) for p in by_id[package][1]), "Changed path outside initiative ownership: " + path)
    return by_id


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    sub=parser.add_subparsers(dest="mode", required=True)
    hardware=sub.add_parser("hardware")
    hardware.add_argument("--samples",type=Path,required=True); hardware.add_argument("--policy",type=Path,required=True)
    hardware.add_argument("--identity",required=True); hardware.add_argument("--since")
    owner=sub.add_parser("ownership")
    owner.add_argument("--manifest",type=Path,required=True); owner.add_argument("--package"); owner.add_argument("--diff-file",type=Path)
    args=parser.parse_args()
    try:
        if args.mode == "hardware":
            sample_bytes, policy_bytes = args.samples.read_bytes(), args.policy.read_bytes()
            cutoff = datetime.now(timezone.utc)
            result=hardware_conditions(json.loads(sample_bytes),json.loads(policy_bytes),args.identity,cutoff,utc(args.since,"run start") if args.since else None)
            result.update(observedAtUtc=cutoff.isoformat(),hardwareObservationSha256=hashlib.sha256(sample_bytes).hexdigest(),hardwarePolicySha256=hashlib.sha256(policy_bytes).hexdigest(),
                          rawHardwareObservationBase64=base64.b64encode(sample_bytes).decode("ascii"),
                          rawHardwarePolicyBase64=base64.b64encode(policy_bytes).decode("ascii"))
            print(json.dumps(result,allow_nan=False))
        else:
            require(bool(args.package)==bool(args.diff_file), "Package and diff file must be supplied together")
            initiative_ownership(json.loads(args.manifest.read_text()),args.package,args.diff_file.read_text().splitlines() if args.diff_file else ())
            print("PASS: additive initiative ownership; no runtime or approval claim.")
        return 0
    except (ValidationError,ValueError,OSError) as error:
        print(str(error),file=sys.stderr);return 2
if __name__ == "__main__":
    raise SystemExit(main())
