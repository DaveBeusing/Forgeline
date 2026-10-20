"""Fail-closed hosted admission for the optional physical render qualification job."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import urllib.error
import urllib.parse
import urllib.request


REPOSITORY = "DaveBeusing/Forgeline"
REQUIRED_LABELS = frozenset(("self-hosted", "windows", "x64", "forgeline-performance"))
MAX_WORKFLOW_CAPTURE_MINUTES = 660
SHA = re.compile(r"[0-9a-f]{40}")
DIGEST = re.compile(r"[0-9a-f]{64}")


class ReadinessError(ValueError):
    """Required trust, evidence, CI, or physical-runner condition is unverified."""


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ReadinessError(message)


def json_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        require(key not in result, f"Duplicate JSON property: {key}")
        result[key] = value
    return result


def load_json(data: bytes) -> object:
    def invalid_constant(value: str) -> None:
        raise ReadinessError(f"Non-finite JSON value: {value}")

    return json.loads(data.decode("utf-8-sig"), object_pairs_hook=json_object,
                      parse_constant=invalid_constant)


def trusted_sources(root: Path, repository: str, event: str, ref: str,
                    workflow_sha: str, baseline: str, candidate: str) -> None:
    require(repository == REPOSITORY, "Native qualification is restricted to the canonical repository.")
    require(event == "workflow_dispatch" and ref == "refs/heads/master",
            "Native qualification requires a manual dispatch of trusted master.")
    require(all(SHA.fullmatch(value) for value in (workflow_sha, baseline, candidate)),
            "Full lowercase commit SHAs are required; branch names are not executable inputs.")
    head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=root, check=True,
                          capture_output=True, text=True).stdout.strip()
    require(head == workflow_sha, "Readiness code must run from the exact dispatched master commit.")
    for source in (baseline, candidate):
        checked = subprocess.run(["git", "merge-base", "--is-ancestor", source, workflow_sha],
                                 cwd=root, capture_output=True, text=True)
        require(checked.returncode == 0,
                f"Source {source} is not a verified ancestor of trusted master.")


def contained_file(root: Path, relative: str) -> Path:
    require(isinstance(relative, str) and bool(relative), "A relative evidence path is required.")
    path = PurePosixPath(relative)
    require(not path.is_absolute() and ":" not in relative and "\\" not in relative
            and all(part not in ("", ".", "..") for part in relative.split("/")),
            "Evidence paths must be canonical relative paths.")
    location = root
    for part in path.parts:
        location = location / part
        require(not location.is_symlink(), "Symlink evidence is not accepted.")
    require(location.resolve().is_relative_to(root.resolve()) and location.is_file(),
            "Evidence must be an existing regular file beneath its declared root.")
    return location


def hardware_evidence(root: Path, relative: str, digest: str) -> dict[str, object]:
    require(relative.startswith("docs/rendering/qualification/hardware/"),
            "Reviewed hardware evidence must be committed beneath the qualification hardware directory.")
    require(bool(DIGEST.fullmatch(digest)), "Hardware evidence requires its exact SHA-256 digest.")
    path = contained_file(root, relative)
    data = path.read_bytes()
    require(hashlib.sha256(data).hexdigest() == digest, "Reviewed hardware evidence digest differs.")
    value = load_json(data)
    require(isinstance(value, dict), "Hardware evidence must be an object.")
    require(type(value.get("schemaVersion")) is int and value["schemaVersion"] == 1
            and value.get("status") == "qualified",
            "Hardware qualification has not been approved.")
    for key in ("reviewer", "rationale", "powerPlan", "reviewedAtUtc"):
        require(isinstance(value.get(key), str) and bool(value[key].strip()),
                f"Hardware qualification is missing {key}.")
    try:
        reviewed = datetime.fromisoformat(value["reviewedAtUtc"].replace("Z", "+00:00"))
    except (ValueError, AttributeError) as error:
        raise ReadinessError("Hardware review needs an ISO-8601 UTC date.") from error
    require(reviewed.utcoffset() is not None and reviewed.utcoffset().total_seconds() == 0
            and reviewed <= datetime.now(timezone.utc), "Hardware review date must be in UTC and not in the future.")
    capacity = value.get("capacityMinutes")
    require(type(capacity) in (int, float) and math.isfinite(capacity) and capacity > 0,
            "A finite positive runner-capacity qualification is required.")
    require(type(capacity) is int and capacity <= MAX_WORKFLOW_CAPTURE_MINUTES,
            "This workflow requires integral capture capacity of at most 660 minutes, reserving 60 minutes for setup/build in its 720-minute job.")
    require(value.get("serialGpu") is True and value.get("powerSource") == "ac"
            and value.get("thermalState") == "stable"
            and type(value.get("maximumRegressionPercent")) in (int, float)
            and value["maximumRegressionPercent"] == 15,
            "Hardware evidence must qualify serial, stable AC operation with the unchanged 15% gate.")
    require(isinstance(value.get("hardwareIdentitySha256"), str)
            and bool(DIGEST.fullmatch(value["hardwareIdentitySha256"])), "Hardware identity digest is missing.")
    calibrations = value.get("qualificationRuns")
    require(isinstance(calibrations, list) and bool(calibrations), "Raw capacity/calibration evidence is required.")
    seen: set[str] = set()
    for record in calibrations:
        require(isinstance(record, dict) and isinstance(record.get("sha256"), str)
                and bool(DIGEST.fullmatch(record["sha256"])), "Calibration digest is missing.")
        calibration = contained_file(path.parent, record.get("path"))
        require(str(calibration) not in seen, "Duplicate calibration file.")
        seen.add(str(calibration))
        require(hashlib.sha256(calibration.read_bytes()).hexdigest() == record["sha256"],
                "Calibration evidence digest differs.")
    return value


def eligible_runners(runners: list[dict[str, object]]) -> list[dict[str, object]]:
    require(isinstance(runners, list), "Runner inventory must be a list.")
    result: list[dict[str, object]] = []
    for runner in runners:
        require(isinstance(runner, dict), "Runner inventory entries must be objects.")
        labels = runner.get("labels")
        require(isinstance(labels, list) and all(isinstance(label, dict)
                and isinstance(label.get("name"), str) for label in labels), "Runner labels are unavailable or invalid.")
        if (type(runner.get("id")) is int and runner.get("status") == "online"
                and runner.get("busy") is False
                and REQUIRED_LABELS.issubset({label["name"].casefold() for label in labels})):
            result.append({"id": runner["id"], "labels": sorted(REQUIRED_LABELS)})
    return result


def latest_ci(runs: list[dict[str, object]], sha: str) -> dict[str, object]:
    require(isinstance(runs, list) and all(isinstance(run, dict) for run in runs),
            "Required CI history must be a list of objects.")
    for run in runs:
        require(isinstance(run.get("path"), str) and isinstance(run.get("created_at"), str)
                and type(run.get("id")) is int, "Required CI history contains an invalid identity.")
    matching = [run for run in runs if run.get("head_sha") == sha
                and run.get("path", "").split("@")[0] == ".github/workflows/ci.yml"]
    require(bool(matching), f"No mandatory CI result is available for exact source {sha}.")
    latest = max(matching, key=lambda run: (run.get("created_at", ""), run.get("id", 0)))
    require(latest.get("status") == "completed" and latest.get("conclusion") == "success",
            f"Latest mandatory CI for {sha} is {latest.get('conclusion') or latest.get('status')}; native gate stays open.")
    return latest


class GitHub:
    def __init__(self, token: str):
        require(bool(token), "Read-only GitHub token is unavailable; runner inventory is unverified.")
        self.token = token

    def pages(self, resource: str, key: str) -> list[dict[str, object]]:
        records: list[dict[str, object]] = []
        separator = "&" if "?" in resource else "?"
        for page in range(1, 101):
            url = f"https://api.github.com/repos/{REPOSITORY}/{resource}{separator}per_page=100&page={page}"
            request = urllib.request.Request(url, headers={"Accept": "application/vnd.github+json",
                                             "Authorization": f"Bearer {self.token}",
                                             "X-GitHub-Api-Version": "2022-11-28"})
            try:
                with urllib.request.urlopen(request, timeout=20) as response:
                    body = response.read(8 * 1024 * 1024 + 1)
            except urllib.error.HTTPError as error:
                raise ReadinessError(f"GitHub {resource.split('?')[0]} returned HTTP {error.code}; admission is unverified.") from error
            except urllib.error.URLError as error:
                raise ReadinessError("GitHub readiness request failed; admission is unverified.") from error
            require(len(body) <= 8 * 1024 * 1024, "Readiness API page exceeds the evidence limit.")
            document = load_json(body)
            require(isinstance(document, dict) and isinstance(document.get(key), list),
                    "GitHub readiness response is missing its collection.")
            current = document[key]
            require(all(isinstance(item, dict) for item in current), "Invalid readiness collection entry.")
            records.extend(current)
            if len(current) < 100:
                return records
        raise ReadinessError("Readiness pagination limit reached; incomplete inventory cannot admit a run.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    args = parser.parse_args()
    result: dict[str, object] = {"schemaVersion": 1, "eligible": False, "nativeQualification": "open",
                                "checkedAtUtc": datetime.now(timezone.utc).isoformat(), "reasons": [], "sourceCi": []}
    try:
        baseline = os.environ.get("QUALIFICATION_BASELINE_SHA", "")
        candidate = os.environ.get("QUALIFICATION_CANDIDATE_SHA", "")
        workflow_sha = os.environ.get("GITHUB_SHA", "")
        trusted_sources(args.root, os.environ.get("GITHUB_REPOSITORY", ""),
                        os.environ.get("GITHUB_EVENT_NAME", ""), os.environ.get("GITHUB_REF", ""),
                        workflow_sha, baseline, candidate)
        result.update({"workflowSha": workflow_sha, "baselineSha": baseline, "candidateSha": candidate})
        evidence = hardware_evidence(args.root, os.environ.get("QUALIFICATION_HARDWARE_EVIDENCE", ""),
                                     os.environ.get("QUALIFICATION_HARDWARE_SHA256", ""))
        result["hardwareEvidenceSha256"] = os.environ["QUALIFICATION_HARDWARE_SHA256"]
        result["capacityMinutes"] = evidence["capacityMinutes"]
        api = GitHub(os.environ.get("GITHUB_TOKEN", ""))
        for source in dict.fromkeys((baseline, candidate, workflow_sha)):
            runs = api.pages(f"actions/workflows/ci.yml/runs?head_sha={urllib.parse.quote(source)}", "workflow_runs")
            result["sourceCi"].append({"sourceSha": source, "runs": [
                {field: run.get(field) for field in ("id", "head_sha", "status", "conclusion", "path", "event", "run_attempt", "created_at", "html_url")}
                for run in runs]})
            latest_ci(runs, source)
        runners = api.pages("actions/runners", "runners")
        result["eligibleRunners"] = eligible_runners(runners)
        require(bool(result["eligibleRunners"]), "No idle online runner with all required physical-GPU labels was verified.")
        result["eligible"] = True
        result["nativeQualification"] = "pending-execution"
    except (ReadinessError, OSError, UnicodeError, json.JSONDecodeError, subprocess.CalledProcessError) as error:
        result["reasons"].append(str(error))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as output:
        json.dump(result, output, indent=2, allow_nan=False)
        output.write("\n")
    if os.environ.get("GITHUB_OUTPUT"):
        with Path(os.environ["GITHUB_OUTPUT"]).open("a", encoding="utf-8") as output:
            output.write(f"eligible={str(result['eligible']).lower()}\n")
    print("Native admission: " + ("eligible; measurement and acceptance still pending" if result["eligible"] else "OPEN; no physical job admitted"))
    for reason in result["reasons"]:
        print(reason)
    return 0 if result["eligible"] else 3


if __name__ == "__main__":
    raise SystemExit(main())
