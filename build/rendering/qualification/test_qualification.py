"""Synthetic rejection tests. These fixtures are never native qualification evidence."""
from __future__ import annotations

import copy
from datetime import datetime, timedelta, timezone
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
import zlib


SPEC = importlib.util.spec_from_file_location("qualification", Path(__file__).with_name("qualification.py"))
q = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(q)


def plan() -> dict:
    return dict(repetitions=5, warmSamples=512, coldSamples=128, warmups=64, rasterDpi=96,
                scenes=list(q.SCENES), profiles=list(q.PROFILES), zooms=list(q.ZOOMS), displays=list(q.DISPLAYS),
                variants=[dict(runProfile=a, cameraPath=b) for a, b in q.VARIANTS])


def png(path: Path, width: int = 1920, height: int = 1080) -> None:
    def chunk(name: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + name + data + struct.pack(">I", zlib.crc32(name + data))
    pixels = b"\0" * ((width * 4 + 1) * height)
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)) +
                     chunk(b"IDAT", zlib.compress(pixels)) + chunk(b"IEND", b""))


def summary(values: list[float]) -> dict:
    return {key: value for key, value in q.distribution(values).items() if key in ("samples", "mean", "p50", "p95", "p99")}


def native(root: Path, source: str = "a" * 40) -> dict:
    root.mkdir(parents=True, exist_ok=True)
    image = root / "frame.png"
    png(image)
    case = dict(key="IndustrialBase-1920x1080-Medium-CloseTactical-Dpi96", kind="IndustrialBase", requestedQuality="Medium",
                image="frame.png", imageSha256=q.sha256(image),
                fingerprint="fixture-synthetic", seed=2026, tick=32, captureVisualSeconds=1.6, mechanicalSeconds=None,
                camera={"distance": 120}, lighting={"exposure": 1}, rasterDpi=96, uiScale=1,
                requestedDisplay={"width": 1920, "height": 1080}, actualSurface={"width": 1920, "height": 1080},
                profile={"requested": "Medium", "active": "Medium", "textures": "Full"}, repeatPixelsEqual=True, repeatCaptures=2, motionImages=[],
                instances={"fallbackMeshInstances": 0}, materials={"bindingFailureCount": 0},
                graphics={"device": {"isSoftwareAdapter": False, "adapterName": "synthetic"},
                          "debug": {"enabled": False, "errorCount": 0},
                          "health": {"submissionFaultCount": 0, "pendingRetirementCount": 0, "failureReasonCode": None},
                          "resources": {"textureBindingFailureCount": 0}},
                run={"runProfile": "Warm", "cameraPath": "Fixed", "cameraPathVersion": 1, "cameraSamples": 512,
                     "warmupFrames": 64, "setupMilliseconds": 3, "setupAllocatedBytes": 0})
    cpu = [dict(index=index, milliseconds=2.0, allocatedBytes=0, resources={"residentTextureBytes": 10},
                health={"liveResourceCount": 2}, worldMilliseconds=1, overlayMilliseconds=None)
           for index in range(512)]
    gpu = [dict(fence=index + 1, frameMilliseconds=1.0, particleMilliseconds=None) for index in range(512)]
    case["cpu"] = summary([2.0] * 512)
    case["gpu"] = summary([1.0] * 512)
    case["performance"] = dict(cpuFrames=cpu, gpuFrames=gpu, allocations=summary([0] * 512),
                               gpu=summary([1.0] * 512), gpuTiming={"available": True, "timestampFrequencyHz": 1000000000,
                                                                  "unavailableReason": None})
    return dict(schemaVersion=1, status="structural-pass", commit=source, buildIdentity="0.1.424-dev+" + source,
                runtimeFingerprint="b" * 64, software=False, environment={"os": "Windows", "architecture": "X64",
                "configuration": "Release", "logicalProcessors": 8, "cpu": "synthetic"}, runtime=".NET 10.0.12",
                driver="synthetic", clientAssemblySha256="c" * 64, presentationAssemblySha256="d" * 64,
                samples=512, warmups=64, captures=[case])


def run(role: str = "baseline", pair: int = 1) -> dict:
    position = 0 if role == ("baseline" if pair % 2 else "candidate") else 1
    started = datetime(2026, 10, 1, tzinfo=timezone.utc) + timedelta(minutes=pair * 10 + position * 4)
    return dict(pair=pair, position=0 if role == ("baseline" if pair % 2 else "candidate") else 1, role=role,
                runProfile="Warm", cameraPath="Fixed", sdk="10.0.401", conditions={"powerPlan": "test", "powerSource": "ac",
                "thermalState": "unavailable", "thermalReason": "synthetic"}, command={"executable": "pwsh",
                "arguments": ["capture.ps1"], "exitCode": 0, "startedAtUtc": started.isoformat(), "elapsedMilliseconds": 2000})


def frame_envelope() -> dict:
    return dict(schemaVersion=1, kind="frame-envelope", scope="live-client", frequencyHz=1000, gpuStreamScope="independent-live-client",
                streamId="synthetic-stream", includes=list(q.PHASES), frames=[
        dict(index=0, startTick=10, endTick=30, phases={name: {"startTick": 10, "endTick": 25} for name in q.PHASES},
             processAllocatedBytes=8, threadAllocatedBytes=4, gpu={"queue": "direct", "generation": 1,
             "submittedFence": 3, "completedFence": 3, "startTick": 100, "endTick": 200, "frequencyHz": 1000000})])


def lifecycle() -> dict:
    events = [dict(seconds=i * 60, kind="camera", stateSha256=hashlib.sha256(str(i).encode()).hexdigest()) for i in range(31)]
    events += [dict(seconds=100 + i * 200 + j, kind=kind, stateId=str(i))
               for i in range(2) for j, kind in enumerate(("load", "unload", "session", "resize"))]
    samples = [dict(seconds=at, checkpoint="post-unload", **{name: 3 for name in q.MEMORY_COUNTERS})
               for at in (300, 600, 900, 1200, 1800)]
    return dict(schemaVersion=1, kind="lifecycle", scriptSha256="a" * 64, durationSeconds=1800,
                checkpointPolicy="same-workload-after-warmup-no-exclusions", counterSemantics="live-or-outstanding-at-settled-checkpoint",
                events=sorted(events, key=lambda event: event["seconds"]), settledSamples=samples)


class StatisticsTests(unittest.TestCase):
    def test_lower_median_nearest_rank_and_tail_not_smoothed(self):
        actual = q.distribution([1, 2, 3, 1000])
        self.assertEqual((actual["p50"], actual["p95"], actual["p99"], actual["mean"]), (2, 1000, 1000, 251.5))
        self.assertGreater(actual["standardDeviation"], 400)

    def test_missing_values_are_null(self):
        actual = q.distribution([])
        self.assertEqual(actual["samples"], 0)
        self.assertTrue(all(value is None for key, value in actual.items() if key != "samples"))

    def test_single_sample_has_no_sample_spread(self):
        self.assertIsNone(q.distribution([2])["standardDeviation"])

    def test_zero_denominator_has_no_percentage(self):
        actual = q.compare_distribution(q.distribution([0]), q.distribution([1]))
        self.assertTrue(all(row["percent"] is None and not row["exceedsThreshold"] for row in actual))

    def test_fixed_threshold_retains_strict_exceedance(self):
        self.assertFalse(q.compare_distribution(q.distribution([100]), q.distribution([115]))[0]["exceedsThreshold"])
        self.assertTrue(q.compare_distribution(q.distribution([100]), q.distribution([115.001]))[0]["exceedsThreshold"])

    def test_bool_nonfinite_negative_rejected(self):
        for value in (True, None, float("nan"), float("inf"), -1, "2"):
            with self.subTest(value=value), self.assertRaises(q.ValidationError):
                q.distribution([value])

    def test_distribution_overflow_is_rejected(self):
        for values in ([1e308, 1e308], [2**4096]):
            with self.subTest(values=str(values)[:30]), self.assertRaises(q.ValidationError):
                q.distribution(values)


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def test_duplicate_keys_and_numeric_overflow_rejected(self):
        path = self.root / "bad.json"
        for value in ('{"samples":0,"samples":512}', '{"x":NaN}', '{"x":1e999}'):
            path.write_text(value)
            with self.subTest(value=value), self.assertRaises(q.ValidationError):
                q.read_json(path)

    def test_paths_escape_symlink_and_hash_tampering_rejected(self):
        path = self.root / "value.json"
        path.write_text("{}")
        ref = dict(path="value.json", sha256=q.sha256(path))
        self.assertEqual(q.evidence(self.root, ref), path)
        for bad in ("../value.json", "/value.json", "a\\value.json", "a/../value.json", "a//b", "C:/value.json"):
            with self.subTest(path=bad), self.assertRaises(q.ValidationError):
                q.artifact(self.root, bad)
        path.write_text('{"changed":true}')
        with self.assertRaises(q.ValidationError):
            q.evidence(self.root, ref)
        link = self.root / "link.json"
        try:
            link.symlink_to(path)
        except OSError:
            return
        with self.assertRaises(q.ValidationError):
            q.artifact(self.root, "link.json")

    def test_exclusive_output_preserves_original(self):
        path = self.root / "report.json"
        q.write_new(path, {"a": 1})
        with self.assertRaises(FileExistsError):
            q.write_new(path, {"a": 2})
        self.assertEqual(q.read_json(path), {"a": 1})

    def test_plan_cannot_drop_profiles_phases_samples_or_repetitions(self):
        original = plan()
        self.assertEqual(len(q.capture_keys(original, "Warm", "Fixed")), 54)
        q.validate_plan(original)
        for key, value in (("profiles", ["Medium"]), ("variants", original["variants"][:-1]),
                           ("warmSamples", 511), ("repetitions", 4), ("repetitions", True), ("warmups", 63)):
            changed = copy.deepcopy(original)
            changed[key] = value
            with self.subTest(key=key), self.assertRaises(q.ValidationError):
                q.validate_plan(changed)

    def test_native_raw_and_mapped_source_are_validated(self):
        manifest = native(self.root)
        rows, missing = q.validate_native(manifest, self.root, run(), plan(), "a" * 40)
        self.assertEqual(len(rows), 1)
        self.assertEqual(len(missing), 53)
        row = next(iter(rows.values()))
        self.assertEqual(row["cpu"]["samples"], 512)
        self.assertEqual(row["gpu"]["samples"], 512)
        with self.assertRaises(q.ValidationError):
            q.validate_native(manifest, self.root, run(), plan(), "e" * 40)

    def test_native_mutation_failures(self):
        original = native(self.root)
        changes = {
            "missing frames": lambda c: c["performance"]["cpuFrames"].pop(),
            "duplicate CPU index": lambda c: c["performance"]["cpuFrames"][1].update(index=0),
            "duplicate fence": lambda c: c["performance"]["gpuFrames"][1].update(fence=1),
            "zero frequency": lambda c: c["performance"]["gpuTiming"].update(timestampFrequencyHz=0),
            "nonfinite duration": lambda c: c["performance"]["gpuFrames"][0].update(frameMilliseconds=float("nan")),
            "forged summary": lambda c: c["cpu"].update(p99=0),
            "missing summary field": lambda c: c["gpu"].pop("mean"),
            "boolean duration": lambda c: c["performance"]["cpuFrames"][0].update(milliseconds=True),
            "clamped surface": lambda c: c["actualSurface"].update(width=1280),
            "bad camera version": lambda c: c["run"].update(cameraPathVersion=99),
            "fallback asset": lambda c: c["instances"].update(fallbackMeshInstances=1),
            "bad image hash": lambda c: c.update(imageSha256="f" * 64),
            "flattened projection": lambda c: c.pop("performance"),
            "key wrong scene": lambda c: c.update(kind="BattlefieldCombat"),
            "key wrong profile": lambda c: c.update(requestedQuality="High"),
            "unrequested active profile": lambda c: c["profile"].update(active="High"),
            "key wrong zoom": lambda c: c["camera"].update(distance=420),
        }
        for name, mutate in changes.items():
            value = copy.deepcopy(original)
            mutate(value["captures"][0])
            with self.subTest(name=name), self.assertRaises(q.ValidationError):
                q.validate_native(value, self.root, run(), plan(), "a" * 40)

    def test_unavailable_gpu_is_explicit_null_distribution(self):
        value = native(self.root)
        case = value["captures"][0]
        case["gpu"] = summary([])
        case["performance"].update(gpuFrames=[], gpu=summary([]), gpuTiming={"available": False,
                                  "timestampFrequencyHz": 0, "unavailableReason": "disabled"})
        rows, _ = q.validate_native(value, self.root, run(), plan(), "a" * 40)
        self.assertIsNone(next(iter(rows.values()))["gpu"]["mean"])
        case["gpu"]["mean"] = 0
        with self.assertRaises(q.ValidationError):
            q.validate_native(value, self.root, run(), plan(), "a" * 40)

    def cohort(self) -> dict:
        frozen = plan()
        q.write_new(self.root / "plan.json", frozen)
        return dict(schemaVersion=1, workstream="RenderPerformanceAndVisualQualification", comparisonKind="optimization",
                    analysisMasterSha="1" * 40, integratedFoundationSha="2" * 40, integratedRepairSha="3" * 40,
                    baselineSha="a" * 40, candidateSha="e" * 40, plan=frozen,
                    planEvidence={"path": "plan.json", "sha256": q.sha256(self.root / "plan.json")},
                    hardware={"qualified": False, "qualificationEvidence": None}, runs=[], commands=[])

    def journal(self, cohort: dict, entry: dict) -> None:
        index = len(cohort["commands"])
        command = entry["command"]
        for name in ("stdout", "stderr", "monitor"):
            path = self.root / f"command{index}.{name}.log"
            path.write_text("Synthetic command stream only")
            command[name] = dict(path=path.name, sha256=q.sha256(path))
        path = self.root / f"command{index}.json"
        q.write_new(path, command)
        cohort["commands"].append(dict(path=path.name, sha256=q.sha256(path)))

    def refresh_journal(self, cohort: dict, index: int) -> None:
        path = self.root / cohort["commands"][index]["path"]
        path.write_text(json.dumps(cohort["runs"][index]["command"]))
        cohort["commands"][index]["sha256"] = q.sha256(path)

    def add_run(self, cohort: dict, role: str, pair: int = 1) -> dict:
        entry = run(role, pair)
        directory = self.root / f"pair{pair}" / role
        value = native(directory, cohort[role + "Sha"])
        path = directory / "manifest.json"
        q.write_new(path, value)
        entry.update(manifest=path.relative_to(self.root).as_posix(), manifestSha256=q.sha256(path))
        self.journal(cohort, entry)
        cohort["runs"].append(entry)
        return value

    def replace_native(self, cohort: dict, index: int, value: dict) -> None:
        entry = cohort["runs"][index]
        path = self.root / entry["manifest"]
        path.write_text(json.dumps(value))
        entry["manifestSha256"] = q.sha256(path)

    def test_incomplete_cohort_reports_open_and_preserves_pairs(self):
        cohort = self.cohort()
        self.add_run(cohort, "baseline")
        self.add_run(cohort, "candidate")
        result = q.evaluate(cohort, self.root)
        self.assertEqual(result["status"], "open")
        self.assertEqual(result["gates"]["matrix"]["status"], "open")
        self.assertEqual(len(result["comparisons"]), 1)
        self.assertEqual(len(result["missingRuns"]), 38)
        self.assertEqual(result["gates"]["gpuTimestampIntegrity"]["status"], "open")
        self.assertEqual(result["gates"]["fullFrame"]["status"], "open")

    def test_hardware_driver_and_workload_changes_rejected(self):
        cohort = self.cohort()
        self.add_run(cohort, "baseline")
        candidate = self.add_run(cohort, "candidate")
        for field in ("driver", "profile", "runtime", "camera"):
            changed = copy.deepcopy(candidate)
            if field == "driver":
                changed["driver"] = "different"
            elif field == "runtime":
                changed["runtimeFingerprint"] = "f" * 64
            elif field == "profile":
                changed["captures"][0]["profile"]["textures"] = "Half"
            else:
                changed["captures"][0]["camera"]["distance"] = 420
            self.replace_native(cohort, 1, changed)
            with self.subTest(field=field), self.assertRaises(q.ValidationError):
                q.evaluate(cohort, self.root)

    def test_visual_candidate_allows_declared_quality_change_without_promotion(self):
        cohort = self.cohort()
        cohort["comparisonKind"] = "visual-candidate"
        self.add_run(cohort, "baseline")
        candidate = self.add_run(cohort, "candidate")
        candidate["runtimeFingerprint"] = "f" * 64
        self.replace_native(cohort, 1, candidate)
        result = q.evaluate(cohort, self.root)
        self.assertEqual(len(result["changedQualityCases"]), 1)
        self.assertEqual(result["gates"]["referenceTiming"]["status"], "open")
        self.assertEqual(result["gates"]["visualAcceptance"]["status"], "open")

    def test_software_is_functional_only(self):
        cohort = self.cohort()
        value = self.add_run(cohort, "baseline")
        value["software"] = True
        value["captures"][0]["graphics"]["device"]["isSoftwareAdapter"] = True
        self.replace_native(cohort, 0, value)
        result = q.evaluate(cohort, self.root)
        self.assertEqual(result["gates"]["hardware"]["status"], "open")
        self.assertIn("Software", result["gates"]["hardware"]["reason"])

    def test_same_artifact_cannot_be_reused_in_an_independent_pair(self):
        cohort = self.cohort()
        self.add_run(cohort, "baseline")
        entry = copy.deepcopy(cohort["runs"][0])
        entry.update(pair=2, position=1)
        cohort["runs"].append(entry)
        with self.assertRaises(q.ValidationError):
            q.evaluate(cohort, self.root)

    def test_manifest_copy_cannot_be_relabelled_as_a_new_run(self):
        cohort = self.cohort()
        self.add_run(cohort, "baseline")
        entry = copy.deepcopy(cohort["runs"][0])
        target = self.root / "copy"
        shutil.copytree((self.root / entry["manifest"]).parent, target)
        entry.update(pair=2, position=1, manifest="copy/manifest.json")
        cohort["runs"].append(entry)
        with self.assertRaises(q.ValidationError):
            q.evaluate(cohort, self.root)

    def test_balanced_order_and_plan_identity_are_enforced(self):
        cohort = self.cohort()
        self.add_run(cohort, "baseline")
        cohort["runs"][0]["position"] = 1
        with self.assertRaises(q.ValidationError):
            q.evaluate(cohort, self.root)
        cohort["runs"][0]["position"] = 0
        cohort["plan"]["warmSamples"] += 1
        with self.assertRaises(q.ValidationError):
            q.evaluate(cohort, self.root)

    def test_actual_overlapping_command_intervals_are_rejected(self):
        cohort = self.cohort()
        self.add_run(cohort, "baseline")
        self.add_run(cohort, "candidate")
        cohort["runs"][1]["command"]["startedAtUtc"] = cohort["runs"][0]["command"]["startedAtUtc"]
        self.refresh_journal(cohort, 1)
        with self.assertRaises(q.ValidationError):
            q.evaluate(cohort, self.root)

    def test_same_source_payload_changes_between_repetitions_are_rejected(self):
        cohort = self.cohort()
        self.add_run(cohort, "baseline")
        value = self.add_run(cohort, "baseline", 2)
        value["runtimeFingerprint"] = "f" * 64
        self.replace_native(cohort, 1, value)
        with self.assertRaises(q.ValidationError):
            q.evaluate(cohort, self.root)

    def test_failed_command_or_failed_collector_never_report_accepted(self):
        cohort = self.cohort()
        entry = run()
        entry["command"]["exitCode"] = 2
        self.journal(cohort, entry)
        cohort["runs"].append(entry)
        self.assertEqual(q.evaluate(cohort, self.root)["status"], "failed")
        cohort["runs"] = []
        cohort["collectorStatus"] = dict(status="failed", reason="preflight failed")
        self.assertEqual(q.evaluate(cohort, self.root)["status"], "failed")

    def test_partial_gpu_windows_never_claim_full_gpu_observation(self):
        cohort = self.cohort()
        value = self.add_run(cohort, "baseline")
        case = value["captures"][0]
        case["gpu"] = summary([1.0])
        case["performance"]["gpuFrames"] = case["performance"]["gpuFrames"][:1]
        case["performance"]["gpu"] = summary([1.0])
        self.replace_native(cohort, 0, value)
        result = q.evaluate(cohort, self.root)
        self.assertEqual(result["incompleteGpuWindows"][0]["observed"], 1)
        self.assertEqual(result["incompleteGpuWindows"][0]["expected"], 512)
        self.assertEqual(result["gates"]["referenceTiming"]["status"], "open")

    def test_journal_log_tampering_is_detected(self):
        cohort = self.cohort()
        self.add_run(cohort, "baseline")
        (self.root / cohort["runs"][0]["command"]["stdout"]["path"]).write_text("Changed")
        with self.assertRaises(q.ValidationError):
            q.evaluate(cohort, self.root)

    def test_hardware_boolean_alone_cannot_certify(self):
        cohort = self.cohort()
        cohort["hardware"]["qualified"] = True
        with self.assertRaises(q.ValidationError):
            q.evaluate(cohort, self.root)

    def test_cli_open_strict_and_overwrite_exit_codes(self):
        cohort = self.cohort()
        path = self.root / "cohort.json"
        q.write_new(path, cohort)
        command = [sys.executable, str(Path(q.__file__)), "--cohort", str(path), "--output", str(self.root / "report.json"), "--require-accepted"]
        self.assertEqual(subprocess.run(command, capture_output=True, check=False).returncode, 3)
        self.assertEqual(subprocess.run(command, capture_output=True, check=False).returncode, 2)


class SupplementalTests(unittest.TestCase):
    def test_enclosing_frame_uses_direct_bracket_not_sum_of_overlaps(self):
        result = q.validate_frame_envelope(frame_envelope())
        self.assertEqual(result["fullFrame"]["mean"], 20)
        self.assertEqual(result["gpu"]["mean"], .1)
        self.assertEqual(result["processAllocations"]["mean"], 8)

    def test_envelope_rejects_shifted_work_noncompleted_fence_and_invalid_ticks(self):
        changes = {
            "work outside bracket": lambda f: f["phases"]["prepare"].update(startTick=0),
            "missing host": lambda f: f["phases"].pop("host"),
            "incomplete fence": lambda f: f["gpu"].update(completedFence=2),
            "zero timestamp denominator": lambda f: f["gpu"].update(frequencyHz=0),
            "reverse GPU ticks": lambda f: f["gpu"].update(endTick=99),
            "negative process allocation": lambda f: f.update(processAllocatedBytes=-1),
            "process less than thread": lambda f: f.update(processAllocatedBytes=1),
        }
        for name, mutate in changes.items():
            value = frame_envelope()
            mutate(value["frames"][0])
            with self.subTest(name=name), self.assertRaises(q.ValidationError):
                q.validate_frame_envelope(value)

    def test_fence_uniqueness_is_scoped_to_generation(self):
        value = frame_envelope()
        second = copy.deepcopy(value["frames"][0])
        second.update(index=1, startTick=30, endTick=50, phases={name: {"startTick": 30, "endTick": 45} for name in q.PHASES})
        value["frames"].append(second)
        with self.assertRaises(q.ValidationError):
            q.validate_frame_envelope(value)
        second["gpu"]["generation"] = 2
        self.assertEqual(q.validate_frame_envelope(value)["gpu"]["samples"], 2)

    def test_lifecycle_requires_duration_motion_transitions_and_plateau(self):
        original = lifecycle()
        self.assertTrue(q.validate_lifecycle(original)["plateau"])
        for field in ("short", "static", "missing resize", "too few cycles", "counter unknown", "frontloaded motion", "fake transitions"):
            value = copy.deepcopy(original)
            if field == "short":
                value["durationSeconds"] = 1799
            elif field == "static":
                for event in value["events"]:
                    if event["kind"] == "camera":
                        event["stateSha256"] = "a" * 64
            elif field == "missing resize":
                value["events"] = [event for event in value["events"] if event["kind"] != "resize"]
            elif field == "too few cycles":
                value["settledSamples"].pop()
            elif field == "frontloaded motion":
                for event in value["events"]:
                    if event["kind"] == "camera":
                        event["seconds"] = 0
                value["events"].sort(key=lambda event: event["seconds"])
            elif field == "fake transitions":
                for event in value["events"]:
                    if event["kind"] in ("resize", "session"):
                        event["stateId"] = "unchanged"
            else:
                value["settledSamples"][0]["stagingBytes"] = None
            with self.subTest(field=field), self.assertRaises(q.ValidationError):
                q.validate_lifecycle(value)
        value = copy.deepcopy(original)
        value["settledSamples"][-1]["retiringResourceBytes"] += 1
        result = q.validate_lifecycle(value)
        self.assertFalse(result["plateau"])
        self.assertIn("retiringResourceBytes", result["growingCounters"])


class ImportedGateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.sources = dict(baselineSha="a" * 40, candidateSha="e" * 40)
        self.observations = {(1, role, "case"): dict(runtimeFingerprint="b" * 64, hardwareIdentitySha256="c" * 64,
                            imageSha256=("d" if role == "baseline" else "f") * 64, manifestSha256=("1" if role == "baseline" else "2") * 64)
                             for role in ("baseline", "candidate")}
        self.counter = 0

    def save(self, value: dict, prefix: str = "raw") -> dict:
        self.counter += 1
        path = self.root / f"{prefix}{self.counter}.json"
        q.write_new(path, value)
        return dict(path=path.name, sha256=q.sha256(path))

    def command(self, role: str) -> dict:
        return dict(sourceSha=self.sources[role + "Sha"], executable="dotnet", arguments=["test"],
                    startedAtUtc="2026-10-01T00:00:00Z", elapsedMilliseconds=1000, exitCode=0,
                    stdout=self.save({"output": "synthetic"}), stderr=self.save({}))

    def record(self, kind: str) -> dict:
        return dict(schemaVersion=1, kind=kind, **self.sources)

    def evaluate(self, record: dict, complete: bool = True) -> dict:
        ref = self.save(record, "gate")
        ref["kind"] = record["kind"]
        return q.validation_imports({"validationEvidence": [ref]}, self.root, self.sources, self.observations, complete)[0]

    def content(self, value: int = 58720256) -> dict:
        record = self.record("content-budget")
        record["sides"] = [dict(role=role, command=self.command(role), measurement=self.save(dict(schemaVersion=1,
                           sourceSha=self.sources[role + "Sha"], runtimeFingerprint="b" * 64,
                           metric="compiled-gameplay-texture-container-bytes", bytes=value, limitBytes=58720256)))
                           for role in ("baseline", "candidate")]
        return record

    def test_exact_compiled_container_metric_and_unchanged_limit(self):
        self.assertEqual(self.evaluate(self.content())["content"]["status"], "pass")
        self.assertEqual(self.evaluate(self.content(58720257))["content"]["status"], "failed")
        changed = self.content()
        path = self.root / changed["sides"][0]["measurement"]["path"]
        value = q.read_json(path)
        value["metric"] = "logical-resident-texture-bytes"
        changed["sides"][0]["measurement"] = self.save(value)
        with self.assertRaises(q.ValidationError):
            self.evaluate(changed)
        value["metric"] = "compiled-gameplay-texture-container-bytes"
        value["limitBytes"] += 1
        changed["sides"][0]["measurement"] = self.save(value)
        with self.assertRaises(q.ValidationError):
            self.evaluate(changed)

    def test_source_and_successful_command_required(self):
        changed = self.content()
        changed["sides"][0]["command"]["exitCode"] = 1
        self.assertEqual(self.evaluate(changed)["content"]["status"], "failed")
        changed["sides"][0]["command"]["sourceSha"] = "f" * 40
        with self.assertRaises(q.ValidationError):
            self.evaluate(changed)

    def ci(self, *, latest_failure: bool = False) -> dict:
        record = self.record("required-ci")
        record["sides"] = []
        for role in ("baseline", "candidate"):
            source = self.sources[role + "Sha"]
            runs = [dict(id=index, head_sha=source, path=".github/workflows/" + workflow, created_at="2026-10-01T00:00:00Z",
                         status="completed", conclusion="success", run_attempt=1)
                    for index, workflow in enumerate(("ci.yml", "frame-hotpaths.yml", "scalability.yml"), start=1)]
            main_id = 1
            if latest_failure:
                main_id = 4
                failed = dict(runs[0])
                failed.update(id=4, conclusion="failure", created_at="2026-10-02T00:00:00Z")
                runs.append(failed)
            jobs = [dict(id=10, run_id=main_id, head_sha=source, run_attempt=1, name="build-test", status="completed",
                         conclusion="failure" if latest_failure else "success")]
            record["sides"].append(dict(role=role, runsPages=[self.save(dict(total_count=len(runs), workflow_runs=runs))],
                                       jobsPages=[self.save(dict(total_count=1, jobs=jobs))]))
        return record

    def test_latest_failed_source_ci_cannot_select_older_success(self):
        self.assertEqual(self.evaluate(self.ci())["requiredCi"]["status"], "pass")
        self.assertEqual(self.evaluate(self.ci(latest_failure=True))["requiredCi"]["status"], "failed")

    def test_missing_ci_pages_and_foreign_jobs_are_rejected(self):
        changed = self.ci()
        pages = changed["sides"][0]["runsPages"]
        raw = q.read_json(self.root / pages[0]["path"])
        raw["total_count"] += 1
        pages[0] = self.save(raw)
        with self.assertRaises(q.ValidationError):
            self.evaluate(changed)
        changed = self.ci()
        jobs = changed["sides"][0]["jobsPages"]
        raw = q.read_json(self.root / jobs[0]["path"])
        raw["jobs"][0]["head_sha"] = "f" * 40
        jobs[0] = self.save(raw)
        with self.assertRaises(q.ValidationError):
            self.evaluate(changed)

    def allocation(self) -> dict:
        record = self.record("allocation-budget")
        policy = self.save(dict(schemaVersion=1, kind="allocation-budget", reviewer="Synthetic reviewer",
                           rationale="Synthetic policy fixture only", reviewedAtUtc="2026-09-01T00:00:00Z",
                           maximumPerSample={"processAllocatedBytes": 0, "threadAllocatedBytes": 0}))
        record["sides"] = [dict(role=role, command=self.command(role), policy=policy,
                              measurement=self.save(dict(schemaVersion=1, sourceSha=self.sources[role + "Sha"],
                              runtimeFingerprint="b" * 64, hardwareIdentitySha256="c" * 64, warmupFrames=64,
                              scope="live-client-full-frame", samples=[dict(index=index, processAllocatedBytes=0, threadAllocatedBytes=0)
                                                                     for index in range(512)]))) for role in ("baseline", "candidate")]
        return record

    def test_allocation_budget_needs_process_and_thread_raw_observations(self):
        record = self.allocation()
        self.assertEqual(self.evaluate(record)["allocation"]["status"], "pass")
        side = record["sides"][1]
        raw = q.read_json(self.root / side["measurement"]["path"])
        raw["samples"][50]["processAllocatedBytes"] = 1
        side["measurement"] = self.save(raw)
        self.assertEqual(self.evaluate(record)["allocation"]["status"], "failed")
        raw["samples"][50]["processAllocatedBytes"] = None
        side["measurement"] = self.save(raw)
        with self.assertRaises(q.ValidationError):
            self.evaluate(record)

    def test_simple_pass_flag_cannot_override_gate(self):
        record = self.record("content-budget")
        record["status"] = "pass"
        with self.assertRaises(q.ValidationError):
            self.evaluate(record)


if __name__ == "__main__":
    unittest.main()
