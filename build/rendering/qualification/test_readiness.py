"""Admission failures must never schedule an unverified physical runner."""

import hashlib
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import urllib.error

import check_readiness as readiness


SOURCE = "a" * 40
CANDIDATE = "b" * 40


class ReadinessTests(unittest.TestCase):
    def test_pr_and_noncanonical_repository_never_reach_git(self):
        with patch.object(readiness.subprocess, "run") as run:
            for repository, event, ref in (
                    (readiness.REPOSITORY, "pull_request", "refs/pull/2/merge"),
                    ("fork/Forgeline", "workflow_dispatch", "refs/heads/master"),
                    (readiness.REPOSITORY, "workflow_dispatch", "refs/heads/topic")):
                with self.subTest(repository=repository, event=event, ref=ref):
                    with self.assertRaises(readiness.ReadinessError):
                        readiness.trusted_sources(Path("."), repository, event, ref, SOURCE, SOURCE, CANDIDATE)
            run.assert_not_called()

    def test_ref_expressions_cannot_be_commit_inputs(self):
        with patch.object(readiness.subprocess, "run") as run:
            for source in ("master", "a" * 39, "$(touch x)", "refs/pull/42/head", "A" * 40):
                with self.subTest(source=source):
                    with self.assertRaises(readiness.ReadinessError):
                        readiness.trusted_sources(Path("."), readiness.REPOSITORY, "workflow_dispatch",
                                                  "refs/heads/master", SOURCE, source, CANDIDATE)
            run.assert_not_called()

    def test_unmerged_candidate_and_changed_checkout_are_rejected(self):
        with patch.object(readiness.subprocess, "run", side_effect=[
                subprocess.CompletedProcess([], 0, SOURCE + "\n"),
                subprocess.CompletedProcess([], 0, ""),
                subprocess.CompletedProcess([], 1, "")]):
            with self.assertRaisesRegex(readiness.ReadinessError, "ancestor"):
                readiness.trusted_sources(Path("."), readiness.REPOSITORY, "workflow_dispatch",
                                          "refs/heads/master", SOURCE, SOURCE, CANDIDATE)
        with patch.object(readiness.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, CANDIDATE)):
            with self.assertRaisesRegex(readiness.ReadinessError, "exact dispatched"):
                readiness.trusted_sources(Path("."), readiness.REPOSITORY, "workflow_dispatch",
                                          "refs/heads/master", SOURCE, SOURCE, CANDIDATE)

    def test_identical_source_control_is_allowed_after_trust(self):
        with patch.object(readiness.subprocess, "run", side_effect=[
                subprocess.CompletedProcess([], 0, SOURCE + "\n"),
                subprocess.CompletedProcess([], 0, ""),
                subprocess.CompletedProcess([], 0, "")]) as run:
            readiness.trusted_sources(Path("."), readiness.REPOSITORY, "workflow_dispatch",
                                      "refs/heads/master", SOURCE, SOURCE, SOURCE)
            self.assertEqual(3, run.call_count)

    def test_runner_requires_exact_label_set_online_and_idle(self):
        runner = {"id": 12, "status": "online", "busy": False,
                  "labels": [{"name": name} for name in ("self-hosted", "Windows", "X64", "forgeline-performance")]}
        self.assertEqual(12, readiness.eligible_runners([runner])[0]["id"])
        for change in ({"status": "offline"}, {"busy": True}, {"busy": None},
                       {"labels": [{"name": "Windows"}]}, {"id": True}):
            with self.subTest(change=change):
                self.assertEqual([], readiness.eligible_runners([runner | change]))

    def test_later_failed_required_ci_cannot_be_replaced_by_old_green(self):
        valid = {"id": 1, "head_sha": SOURCE, "path": ".github/workflows/ci.yml", "created_at": "2026-01-01",
                 "status": "completed", "conclusion": "success"}
        self.assertEqual(1, readiness.latest_ci([valid], SOURCE)["id"])
        with self.assertRaises(readiness.ReadinessError):
            readiness.latest_ci([valid, valid | {"id": 2, "created_at": "2026-01-02", "conclusion": "failure"}], SOURCE)
        for change in ({"path": ".github/workflows/frame-hotpaths.yml"}, {"head_sha": CANDIDATE},
                       {"status": "in_progress"}, {"conclusion": "skipped"}, {"conclusion": "cancelled"}):
            with self.subTest(change=change):
                with self.assertRaises(readiness.ReadinessError):
                    readiness.latest_ci([valid | change], SOURCE)

    def test_hardware_review_and_calibration_are_bound_to_actual_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            calibration = root / "docs/rendering/qualification/hardware/series.json"
            calibration.parent.mkdir(parents=True)
            calibration.write_text('{"raw":[1,2,3]}', encoding="utf-8")
            value = {"schemaVersion": 1, "status": "qualified", "reviewer": "Owner",
                     "reviewedAtUtc": "2026-01-01T00:00:00Z", "rationale": "Stable paired calibration retained",
                     "hardwareIdentitySha256": "c" * 64, "serialGpu": True, "capacityMinutes": 660,
                     "maximumRegressionPercent": 15, "powerPlan": "Balanced", "powerSource": "ac",
                     "thermalState": "stable", "qualificationRuns": [
                         {"path": "series.json", "sha256": hashlib.sha256(calibration.read_bytes()).hexdigest()}]}
            review = calibration.with_name("review.json")
            review.write_text(json.dumps(value), encoding="utf-8")
            relative = review.relative_to(root).as_posix()
            digest = hashlib.sha256(review.read_bytes()).hexdigest()
            self.assertEqual(660, readiness.hardware_evidence(root, relative, digest)["capacityMinutes"])
            calibration.write_text('{"raw":[1,2,0]}', encoding="utf-8")
            with self.assertRaisesRegex(readiness.ReadinessError, "Calibration evidence digest"):
                readiness.hardware_evidence(root, relative, digest)
            with self.assertRaisesRegex(readiness.ReadinessError, "evidence digest"):
                readiness.hardware_evidence(root, relative, "d" * 64)
            for change in ({"schemaVersion": True}, {"qualificationRuns": []}, {"status": "pending"}, {"serialGpu": False},
                           {"capacityMinutes": 0}, {"capacityMinutes": True}, {"capacityMinutes": 661},
                           {"capacityMinutes": 600.5}, {"maximumRegressionPercent": 16},
                           {"powerSource": "battery"}, {"thermalState": "unknown"},
                           {"reviewedAtUtc": "2200-01-01T00:00:00Z"}, {"reviewedAtUtc": "2026-01-01"}):
                with self.subTest(change=change):
                    review.write_text(json.dumps(value | change), encoding="utf-8")
                    digest = hashlib.sha256(review.read_bytes()).hexdigest()
                    with self.assertRaises(readiness.ReadinessError):
                        readiness.hardware_evidence(root, relative, digest)

    def test_evidence_cannot_escape_or_follow_symlinks(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target = root / "target.json"
            target.write_text("{}", encoding="utf-8")
            for relative in ("../target.json", "/target.json", "x/../target.json", "./target.json", "x\\target.json", "C:target.json"):
                with self.subTest(relative=relative):
                    with self.assertRaises(readiness.ReadinessError):
                        readiness.contained_file(root, relative)
            link = root / "link.json"
            try:
                link.symlink_to(target)
            except OSError:
                return
            with self.assertRaisesRegex(readiness.ReadinessError, "Symlink"):
                readiness.contained_file(root, "link.json")

    def test_duplicate_json_keys_and_nonfinite_values_are_rejected(self):
        for data in (b'{"status":"pending","status":"qualified"}', b'{"capacityMinutes":NaN}',
                     b'{"capacityMinutes":Infinity}'):
            with self.subTest(data=data):
                with self.assertRaises(readiness.ReadinessError):
                    readiness.load_json(data)

    def test_runner_inventory_paginates_before_concluding_none_available(self):
        first = {"runners": [{"id": number} for number in range(100)]}
        second = {"runners": [{"id": 101}]}
        responses = [io.BytesIO(json.dumps(value).encode()) for value in (first, second)]
        with patch.object(readiness.urllib.request, "urlopen", side_effect=responses) as request:
            result = readiness.GitHub("test-token").pages("actions/runners", "runners")
            self.assertEqual(101, len(result))
            self.assertIn("page=2", request.call_args.args[0].full_url)

    def test_api_permissions_failure_is_open_and_never_exposes_token(self):
        error = urllib.error.HTTPError("https://api.github.com/", 403, "Forbidden", {}, None)
        with patch.object(readiness.urllib.request, "urlopen", side_effect=error):
            with self.assertRaisesRegex(readiness.ReadinessError, "HTTP 403") as raised:
                readiness.GitHub("sensitive-example-value").pages("actions/runners", "runners")
            self.assertNotIn("sensitive-example-value", str(raised.exception))

    def test_malformed_inventory_and_ci_shapes_fail_with_readiness_error(self):
        for runners in (None, {}, [None], [{"labels": None}], [{"labels": [None]}],
                        [{"labels": [{"name": True}]}]):
            with self.subTest(runners=runners):
                with self.assertRaises(readiness.ReadinessError):
                    readiness.eligible_runners(runners)
        for runs in (None, {}, [None], [{"path": None}],
                     [{"path": ".github/workflows/ci.yml", "id": True, "created_at": "2026-01-01"}]):
            with self.subTest(runs=runs):
                with self.assertRaises(readiness.ReadinessError):
                    readiness.latest_ci(runs, SOURCE)


if __name__ == "__main__":
    unittest.main()
