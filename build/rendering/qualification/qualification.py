#!/usr/bin/env python3
"""Validate preserved render cohorts and report independent acceptance gates.

Exit 0 means valid evidence was reported, not that the product is accepted.
Exit 2 rejects malformed/tampered evidence; --require-accepted returns 3 for
open or failed gates. No input, baseline, threshold, or image is rewritten.
"""
from __future__ import annotations

import argparse
import base64
from datetime import datetime, timezone
import hashlib
import itertools
import json
import math
from pathlib import Path, PurePosixPath
import re
import statistics
import struct
import sys
from typing import Any


SCENES = ("IndustrialBase", "BattlefieldCombat", "CombatSupportRepeated")
PROFILES = ("Medium", "High")
ZOOMS = ("CloseTactical", "NormalGameplay", "Strategic")
DISPLAYS = ("1920x1080", "2560x1440", "3840x2160")
VARIANTS = (("Warm", "Fixed"), ("Warm", "Sweep"), ("Warm", "Traverse"), ("RendererCold", "Fixed"))
PHASES = ("prepare", "host", "renderFrame", "present", "fenceWait")
MEMORY_COUNTERS = ("liveResourceCount", "liveResourceBytes", "retiringResourceCount", "retiringResourceBytes",
                   "textureMipBytes", "stagingBytes", "descriptors", "pendingUploadBytes")


class ValidationError(ValueError):
    """Evidence cannot safely support the claimed identity or measurement."""


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValidationError(message)


def obj(value: Any, label: str) -> dict:
    require(type(value) is dict, f"{label} must be an object")
    return value


def items(value: Any, label: str, *, nonempty: bool = False) -> list:
    require(type(value) is list and (bool(value) or not nonempty), f"{label} must be a {'nonempty ' if nonempty else ''}list")
    return value


def text(value: Any, label: str) -> str:
    require(type(value) is str and bool(value.strip()) and not any(ord(c) < 32 for c in value), f"{label} must be nonempty text")
    return value


def integer(value: Any, label: str, minimum: int = 0) -> int:
    require(type(value) is int and value >= minimum, f"{label} must be an integer >= {minimum}")
    return value


def number(value: Any, label: str, *, positive: bool = False) -> float:
    try:
        valid = type(value) in (int, float) and math.isfinite(value) and (value > 0 if positive else value >= 0)
    except OverflowError:
        valid = False
    require(valid,
            f"{label} must be a finite {'positive' if positive else 'nonnegative'} number")
    return value


def boolean(value: Any, label: str) -> bool:
    require(type(value) is bool, f"{label} must be boolean")
    return value


def digest(value: Any, label: str, length: int = 64) -> str:
    require(type(value) is str and re.fullmatch(rf"[0-9a-fA-F]{{{length}}}", value) is not None,
            f"{label} must be a full {length}-character hexadecimal identity")
    return value.lower()


def utc(value: Any, label: str) -> datetime:
    try:
        result = datetime.fromisoformat(text(value, label).replace("Z", "+00:00"))
        require(result.utcoffset() is not None and result.utcoffset().total_seconds() == 0, f"{label} must use UTC")
        return result
    except (ValueError, TypeError) as exc:
        raise ValidationError(f"{label} must be an ISO 8601 UTC time") from exc


def canonical(value: Any) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False, allow_nan=False).encode("utf-8")


def sha256(path: Path) -> str:
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(chunk)
    return result.hexdigest()


def _unique_object(pairs: list[tuple[str, Any]]) -> dict:
    result: dict = {}
    for key, value in pairs:
        require(key not in result, f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def read_json(path: Path) -> dict:
    def invalid_constant(value: str) -> None:
        raise ValidationError(f"Nonfinite JSON constant: {value}")
    result = json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=_unique_object,
                        parse_constant=invalid_constant)
    # A valid JSON exponent such as 1e999 also overflows a Python float.
    def finite(value: Any) -> None:
        if type(value) is float:
            require(math.isfinite(value), "Nonfinite JSON number")
        elif type(value) is dict:
            for child in value.values():
                finite(child)
        elif type(value) is list:
            for child in value:
                finite(child)
    finite(result)
    return obj(result, str(path))


def artifact(root: Path, relative: Any) -> Path:
    current_root = root.absolute()
    while True:
        require(not current_root.is_symlink(), "Evidence roots and their ancestors cannot be symlinks")
        if current_root == current_root.parent:
            break
        current_root = current_root.parent
    value = text(relative, "artifact path")
    parts = PurePosixPath(value)
    require(not parts.is_absolute() and "\\" not in value and ":" not in value and
            all(p not in ("", ".", "..") for p in value.split("/")), "Artifact path must be a canonical relative POSIX path")
    target = root.joinpath(*parts.parts)
    current = root
    for part in parts.parts:
        current /= part
        require(not current.is_symlink(), "Symlink artifacts are not accepted")
    require(target.resolve().is_relative_to(root.resolve()), "Artifact escaped evidence root")
    require(target.is_file(), f"Missing artifact: {value}")
    return target


def evidence(root: Path, reference: Any) -> Path:
    ref = obj(reference, "evidence reference")
    path = artifact(root, ref.get("path"))
    require(sha256(path) == digest(ref.get("sha256"), "artifact SHA256"), f"Artifact checksum mismatch: {ref.get('path')}")
    return path


def write_new(path: Path, value: Any) -> None:
    require(not path.is_symlink(), "Output cannot be a symlink")
    payload = json.dumps(value, indent=2, ensure_ascii=False, allow_nan=False) + "\n"
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        stream.write(payload)


def distribution(values: list[float]) -> dict:
    """Match the native lower median/nearest-rank tails; preserve every sample."""
    for value in values:
        number(value, "distribution sample")
    if not values:
        return dict(samples=0, mean=None, p50=None, p95=None, p99=None, minimum=None, maximum=None, standardDeviation=None)
    ordered = sorted(values)
    count = len(values)
    try:
        mean = statistics.fmean(values)
        deviation = statistics.stdev(values) if count > 1 else None
        require(math.isfinite(mean) and (deviation is None or math.isfinite(deviation)), "Distribution summary overflow")
        return dict(samples=count, mean=mean, p50=ordered[(count - 1) // 2],
                    p95=ordered[math.ceil(count * .95) - 1], p99=ordered[math.ceil(count * .99) - 1],
                    minimum=ordered[0], maximum=ordered[-1], standardDeviation=deviation)
    except OverflowError as exc:
        raise ValidationError("Distribution summary overflow") from exc


def compare_distribution(before: dict, after: dict) -> list[dict]:
    result = []
    for key in ("mean", "p50", "p95", "p99"):
        old, new = before[key], after[key]
        percent = 100 * (new - old) / old if old is not None and new is not None and old > 0 else None
        require(percent is None or math.isfinite(percent), "Percentage change overflow")
        result.append(dict(statistic=key, baselineMs=old, candidateMs=new,
                           deltaMs=new - old if old is not None and new is not None else None,
                           percent=percent, exceedsThreshold=percent is not None and percent > 15))
    return result


def confirm_summary(declared: Any, values: list[float], label: str) -> None:
    declared = obj(declared, label)
    actual = distribution(values)
    require(integer(declared.get("samples"), label + ".samples") == len(values), f"{label} sample count differs from raw data")
    for key in ("mean", "p50", "p95", "p99"):
        reported = declared.get(key)
        if not values:
            require(key in declared and reported is None, f"{label}.{key} must be null when unavailable")
        else:
            number(reported, label + "." + key)
            require(math.isclose(reported, actual[key], rel_tol=1e-10, abs_tol=1e-10), f"{label}.{key} differs from raw data")


def validate_plan(plan: Any) -> dict:
    plan = obj(plan, "plan")
    integer(plan.get("repetitions"), "plan.repetitions", 5)
    integer(plan.get("warmSamples"), "plan.warmSamples", 512)
    integer(plan.get("coldSamples"), "plan.coldSamples", 8)
    integer(plan.get("warmups"), "plan.warmups", 64)
    require(plan.get("rasterDpi") == 96 and type(plan["rasterDpi"]) is int, "Canonical matrix uses explicit rasterDpi 96")
    for key, expected in (("scenes", SCENES), ("profiles", PROFILES), ("zooms", ZOOMS), ("displays", DISPLAYS)):
        require(items(plan.get(key), "plan." + key) == list(expected), f"Canonical {key} matrix cannot be reduced/reordered")
    expected_variants = [dict(runProfile=a, cameraPath=b) for a, b in VARIANTS]
    require(items(plan.get("variants"), "plan.variants") == expected_variants, "Canonical warm/cold/camera matrix cannot be reduced")
    return plan


def capture_keys(plan: dict, run_profile: str, camera_path: str) -> set[str]:
    suffix = "-Dpi96" + ("-RendererCold" if run_profile == "RendererCold" else "")
    suffix += "-" + camera_path if camera_path != "Fixed" else ""
    return {f"{scene}-{display}-{profile}-{zoom}{suffix}"
            for scene, display, profile, zoom in itertools.product(SCENES, DISPLAYS, PROFILES, ZOOMS)}


def confirm_image(root: Path, value: Any, expected_hash: Any, display: dict) -> None:
    path = artifact(root, value)
    require(len(PurePosixPath(value).parts) == 1, "Native images must be direct children of their manifest directory")
    require(sha256(path) == digest(expected_hash, "image hash"), "Native image checksum mismatch")
    with path.open("rb") as stream:
        header = stream.read(29)
    require(len(header) == 29 and header[:8] == b"\x89PNG\r\n\x1a\n" and header[12:16] == b"IHDR" and
            header[24:29] == b"\x08\x06\x00\x00\x00", "Expected native RGBA8 PNG header")
    require(struct.unpack(">II", header[16:24]) == (display["width"], display["height"]), "Native image resolution differs from request")


def validate_native(manifest: dict, root: Path, run: dict, plan: dict, expected_sha: str) -> tuple[dict, list[str]]:
    require(manifest.get("schemaVersion") == 1 and type(manifest["schemaVersion"]) is int, "Unsupported native manifest schema")
    require(manifest.get("status") == "structural-pass" and not (root / "failure.json").exists(), "Native capture is failed or incomplete")
    require(digest(manifest.get("commit"), "native commit", 40) == expected_sha, "Native source SHA does not match cohort side")
    require(text(manifest.get("buildIdentity"), "build identity").lower().endswith(expected_sha), "Binary build identity does not match source")
    runtime_hash = digest(manifest.get("runtimeFingerprint"), "runtime fingerprint")
    software = boolean(manifest.get("software"), "software adapter")
    environment = obj(manifest.get("environment"), "native environment")
    for key in ("os", "architecture", "cpu", "configuration"):
        text(environment.get(key), "environment." + key)
    integer(environment.get("logicalProcessors"), "logical processors", 1)
    require(environment["architecture"] == "X64" and environment["configuration"] == "Release", "Qualification requires a Release/X64 native build")
    for key in ("runtime", "driver"):
        text(manifest.get(key), key)
    for key in ("clientAssemblySha256", "presentationAssemblySha256"):
        digest(manifest.get(key), key)
    count = plan["warmSamples"] if run["runProfile"] == "Warm" else plan["coldSamples"]
    require(integer(manifest.get("samples"), "native samples", 1) == count, "Native sample request differs from frozen plan")
    require(integer(manifest.get("warmups"), "native warmups") == plan["warmups"], "Native warmup request differs from frozen plan")
    rows: dict = {}
    hardware_ids = set()
    for case in items(manifest.get("captures"), "native captures", nonempty=True):
        case = obj(case, "capture")
        key = text(case.get("key"), "capture key")
        require(key not in rows, "Duplicate native capture key")
        require(key in capture_keys(plan, run["runProfile"], run["cameraPath"]), f"Unexpected native capture key: {key}")
        scene_name, display_name, profile_name, zoom_name = key.split("-")[:4]
        require(case.get("kind") == scene_name and case.get("requestedQuality") == profile_name, "Capture key does not identify the actual scene/requested quality")
        capture_run = obj(case.get("run"), "capture.run")
        require(capture_run.get("runProfile") == run["runProfile"] and capture_run.get("cameraPath") == run["cameraPath"], "Native phase differs from scheduled phase")
        require(capture_run.get("cameraPathVersion") == (2 if run["cameraPath"] == "Traverse" else 1), "Unsupported camera path version")
        require(integer(capture_run.get("cameraSamples"), "camera samples", 1) == count, "Camera sampling density changed")
        require(integer(capture_run.get("warmupFrames"), "warmup frames") == (plan["warmups"] if run["runProfile"] == "Warm" else 0), "Warmup policy changed")
        number(capture_run.get("setupMilliseconds"), "renderer setup milliseconds")
        integer(capture_run.get("setupAllocatedBytes"), "renderer setup allocation")
        require(case.get("repeatPixelsEqual") is True and integer(case.get("repeatCaptures"), "repeat captures", 2) >= 2, "Native repeat pixel contract failed")
        display = obj(case.get("requestedDisplay"), "requested display")
        surface = obj(case.get("actualSurface"), "actual surface")
        for dimension in ("width", "height"):
            integer(display.get(dimension), "requested " + dimension, 1)
            require(surface.get(dimension) == display[dimension] and type(surface[dimension]) is int, "Native display is clamped or malformed")
        require(display_name == f"{display['width']}x{display['height']}", "Capture key resolution differs from native dimensions")
        require(case.get("rasterDpi") == 96 and type(case["rasterDpi"]) is int and case.get("uiScale") == 1, "Native DPI/UI scale changed")
        integer(case.get("seed"), "fixture seed")
        integer(case.get("tick"), "fixture tick")
        fingerprint = text(case.get("fingerprint"), "fixture fingerprint")
        for name in ("camera", "profile", "lighting"):
            obj(case.get(name), name)
        require(case["profile"].get("requested") == profile_name and case["profile"].get("active") == profile_name, "Requested matrix quality was replaced by another profile")
        require(number(case["camera"].get("distance"), "camera distance", positive=True) ==
                {"CloseTactical": 120, "NormalGameplay": 420, "Strategic": 1000}[zoom_name], "Capture camera does not match its planned bookmark")
        graphics = obj(case.get("graphics"), "graphics")
        device = obj(graphics.get("device"), "device")
        text(device.get("adapterName"), "adapter name")
        require(boolean(device.get("isSoftwareAdapter"), "device software adapter") == software, "Adapter type contradicts manifest")
        debug = obj(graphics.get("debug"), "debug")
        boolean(debug.get("enabled"), "debug enabled")
        integer(debug.get("errorCount"), "debug errors")
        require(debug["errorCount"] == 0, "Native debug errors are present")
        health = obj(graphics.get("health"), "health")
        require(integer(health.get("submissionFaultCount"), "submission faults") == 0 and
                integer(health.get("pendingRetirementCount"), "pending retirements") == 0 and
                health.get("failureReasonCode") is None, "Native health contract failed")
        resources = obj(graphics.get("resources"), "resources")
        require(integer(resources.get("textureBindingFailureCount"), "texture binding failures") == 0 and
                integer(obj(case.get("materials"), "materials").get("bindingFailureCount"), "material failures") == 0 and
                integer(obj(case.get("instances"), "instances").get("fallbackMeshInstances"), "fallback instances") == 0,
                "Native content binding contract failed")
        identity = dict(environment=environment, runtime=manifest["runtime"], driver=manifest["driver"], device=device,
                        debugEnabled=debug["enabled"], sdk=run["sdk"])
        hardware_id = hashlib.sha256(canonical(identity)).hexdigest()
        hardware_ids.add(hardware_id)
        performance = obj(case.get("performance"), "original nested performance")
        cpu = items(performance.get("cpuFrames"), "cpuFrames", nonempty=True)
        require(len(cpu) == count, "Missing or extra raw CPU frames")
        cpu_values, allocations = [], []
        for index, frame in enumerate(cpu):
            frame = obj(frame, "CPU frame")
            require(integer(frame.get("index"), "CPU index") == index, "CPU indices missing, duplicated, or reordered")
            cpu_values.append(number(frame.get("milliseconds"), "CPU milliseconds", positive=True))
            allocations.append(integer(frame.get("allocatedBytes"), "thread allocated bytes"))
            for name, value in frame.items():
                if name.endswith("Milliseconds") and value is not None:
                    number(value, name)
            for category in ("resources", "health"):
                for name, value in obj(frame.get(category), "frame." + category).items():
                    if name.endswith(("Count", "Bytes", "Used", "Capacity")):
                        integer(value, "frame." + category + "." + name)
        confirm_summary(case.get("cpu"), cpu_values, "cpu distribution")
        confirm_summary(performance.get("allocations"), allocations, "thread allocation distribution")
        gpu = items(performance.get("gpuFrames"), "gpuFrames")
        timing = obj(performance.get("gpuTiming"), "gpuTiming")
        available = boolean(timing.get("available"), "GPU available")
        require(available == bool(gpu), "GPU availability contradicts raw samples")
        gpu_values, last_fence = [], 0
        if gpu:
            integer(timing.get("timestampFrequencyHz"), "GPU timestamp frequency", 1)
            require(timing.get("unavailableReason") is None, "Available GPU samples have an unavailable reason")
        else:
            text(timing.get("unavailableReason"), "GPU unavailable reason")
            integer(timing.get("timestampFrequencyHz"), "GPU timestamp frequency")
        for frame in gpu:
            frame = obj(frame, "GPU frame")
            fence = integer(frame.get("fence"), "submission fence", 1)
            require(last_fence < fence < 2**64 - 1, "GPU fences must be distinct, increasing and valid within each capture stream")
            last_fence = fence
            gpu_values.append(number(frame.get("frameMilliseconds"), "GPU frame milliseconds"))
            for name, value in frame.items():
                if name.endswith("Milliseconds") and value is not None:
                    number(value, name)
        if "rawGpuTiming" in performance:
            validate_raw_gpu_timing(performance["rawGpuTiming"], gpu)
        require(len(gpu) <= count, "More GPU observations than measured submitted frames")
        confirm_summary(case.get("gpu"), gpu_values, "GPU distribution")
        confirm_summary(performance.get("gpu"), gpu_values, "nested GPU distribution")
        confirm_image(root, case.get("image"), case.get("imageSha256"), display)
        motions = items(case.get("motionImages"), "motionImages")
        seen_images = {case["image"].casefold()}
        for motion in motions:
            motion = obj(motion, "motion image")
            require(text(motion.get("image"), "motion image path").casefold() not in seen_images, "Duplicate motion image path")
            seen_images.add(motion["image"].casefold())
            confirm_image(root, motion["image"], motion.get("imageSha256"), display)
            obj(motion.get("camera"), "motion camera")
        workload = {name: case.get(name) for name in ("fingerprint", "seed", "tick", "captureVisualSeconds", "mechanicalSeconds",
                    "camera", "requestedDisplay", "rasterDpi", "uiScale", "lighting", "localHaze", "surfaceMarks")}
        rows[key] = dict(cpu=distribution(cpu_values), gpu=distribution(gpu_values), threadAllocations=distribution(allocations),
                         runtimeFingerprint=runtime_hash, fixtureFingerprint=fingerprint, profile=case["profile"], workload=workload,
                         imageSha256=digest(case.get("imageSha256"), "image hash"),
                         binaryIdentity=[manifest["clientAssemblySha256"].lower(), manifest["presentationAssemblySha256"].lower()],
                         hardwareIdentitySha256=hardware_id, actualDevice=device, software=software, gpuFrequencyHz=timing["timestampFrequencyHz"],
                         motionImageCount=len(motions), setupMilliseconds=capture_run["setupMilliseconds"],
                         cpuScope="PrepareTerrain and RenderFrame including Present/frame reuse; excludes live host/event/camera work",
                         rawCpuCount=len(cpu), rawGpuCount=len(gpu))
    require(len(hardware_ids) == 1, "Hardware identity changed inside native manifest")
    return rows, sorted(capture_keys(plan, run["runProfile"], run["cameraPath"]) - rows.keys())


def validate_physical_run_conditions(conditions: dict, actual_identity: str, command_start: float, command_end: float, *, after: bool) -> None:
    if "hardwareObservations" not in conditions:
        return  # Historical/unavailable conditions cannot close the hardware gate.
    from telemetry_contracts import hardware_conditions
    require(digest(conditions.get("hardwareIdentitySha256"), "physical hardware identity") == actual_identity, "Physical samples belong to a different actual native adapter/driver/SDK")
    objects = {}
    for label, raw_key, hash_key in (("hardwareObservations", "rawHardwareObservationBase64", "hardwareObservationSha256"),
                                      ("hardwarePolicy", "rawHardwarePolicyBase64", "hardwarePolicySha256")):
        try:
            raw = base64.b64decode(text(conditions.get(raw_key), raw_key), validate=True)
            decoded = json.loads(raw)
        except (ValueError, TypeError) as error:
            raise ValidationError("Invalid preserved physical observation bytes") from error
        require(hashlib.sha256(raw).hexdigest() == digest(conditions.get(hash_key), hash_key) and decoded == conditions.get(label), "Physical observation/policy bytes were changed")
        objects[label] = decoded
    observed = utc(conditions.get("observedAtUtc"), "physical condition time")
    require(observed.timestamp() >= command_end if after else observed.timestamp() <= command_start, "Physical condition snapshot lies on the wrong side of its native command")
    result = hardware_conditions(objects["hardwareObservations"], objects["hardwarePolicy"], actual_identity, observed,
                                 datetime.fromtimestamp(command_start, timezone.utc) if after else None)
    require(result["thermalState"] == conditions.get("thermalState") and result["powerSource"] == conditions.get("powerSource"), "Physical condition summary contradicts raw observations")
    if after:
        last = utc(objects["hardwareObservations"]["samples"][-1]["observedAtUtc"], "physical trace end").timestamp()
        require(last >= command_end - 5, "Physical trace ends before the actual measured command")


def validate_raw_gpu_timing(value: Any, derived: list | None = None) -> dict:
    value = obj(value, "raw GPU export")
    require(value.get("overflowed") is False, "Raw GPU observation storage overflowed")
    require(value.get("scope") == "native-reference-direct-queue" and value.get("worldIntervalIncludesShadow") is True, "Raw native GPU scope is invalid")
    seen, frequencies, by_fence = set(), {}, {}
    for sample in items(value.get("completions"), "raw GPU completions"):
        sample = obj(sample, "raw GPU completion")
        generation = integer(sample.get("generation"), "device generation", 1)
        resource = integer(sample.get("resourceGeneration"), "resource generation", 1)
        require(sample.get("queue") == "direct", "Unexpected GPU queue")
        fence = integer(sample.get("submittedFence"), "raw submitted fence", 1)
        completed = integer(sample.get("completedFence"), "raw completed fence", 1)
        require(fence <= completed < 2**64 - 1 and (generation, fence) not in seen, "Invalid, incomplete or duplicate raw GPU submission")
        seen.add((generation, fence))
        frequency = integer(sample.get("frequencyHz"), "raw GPU frequency", 1)
        require(generation not in frequencies or frequencies[generation] == frequency, "GPU frequency changes within a queue generation")
        frequencies[generation] = frequency
        a, b = integer(sample.get("startTick"), "GPU start"), integer(sample.get("endTick"), "GPU end")
        w, o = integer(sample.get("worldEndTick"), "world end"), integer(sample.get("overlayStartTick"), "overlay start")
        require(a < b and a <= w <= o <= b, "Raw frame/pass endpoints are invalid")
        for name in ("shadow", "particle"):
            start, end = sample.get(name + "StartTick"), sample.get(name + "EndTick")
            require((start is None) == (end is None), "Raw pass endpoint pair is incomplete")
            if start is not None:
                require(a <= integer(start, name + " start") <= integer(end, name + " end") <= b, "Raw pass lies outside its frame")
        require(fence not in by_fence, "A native reference cannot reuse fence numbering across device generations")
        by_fence[fence] = (b-a)*1000/frequency
    if derived is not None:
        for sample in derived:
            fence = sample["fence"]
            require(fence in by_fence and math.isclose(by_fence[fence], sample["frameMilliseconds"], rel_tol=1e-9, abs_tol=1e-9), "Raw GPU ticks disagree with derived native duration")
    return dict(samples=len(seen), generations=len(frequencies))


def validate_frame_envelope(value: Any) -> dict:
    """Validate a supplemental measured enclosing frame without adding overlaps."""
    value = obj(value, "frame envelope")
    require(type(value.get("schemaVersion")) is int and value["schemaVersion"] == 1 and value.get("kind") == "frame-envelope" and value.get("scope") == "live-client", "Invalid live frame envelope contract")
    return validate_frame_samples(value)


def validate_live_owner_telemetry(value: Any) -> dict:
    value = obj(value, "live render-owner telemetry")
    require(type(value.get("schemaVersion")) is int and value["schemaVersion"] == 1 and
            value.get("kind") == "live-render-owner-telemetry" and value.get("scope") == "live-client", "Invalid live owner telemetry contract")
    require(value.get("nativeFixtureBindingAvailable") is False, "Ordinary live gameplay must not claim native fixture identity")
    result = validate_frame_samples(value)
    return dict(**result, nativeFixtureAcceptance="open", scope="active-render-owner; independent platform/simulation work excluded")


def validate_frame_samples(value: dict) -> dict:
    require(value.get("gpuStreamScope") == "independent-live-client", "Live GPU samples must declare their independent client stream, not masquerade as frozen native fences")
    text(value.get("streamId"), "live client stream identity")
    require(value.get("overflowed", False) is False, "Live telemetry overflow invalidates the declared window")
    frequency = integer(value.get("frequencyHz"), "CPU frequency", 1)
    require(items(value.get("includes"), "frame includes") == list(PHASES), "Frame envelope must explicitly include every required phase")
    if "window" in value:
        window = obj(value["window"], "measurement window")
        require(window.get("completed") is True and window.get("contiguous") is True and window.get("arm") == "first-gameplay-iteration", "Live window is incomplete or undeclared")
        integer(window.get("warmups"), "live warmups")
        require(integer(window.get("samples"), "live sample count", 1) == len(items(value.get("frames"), "live frames")), "Declared live sampling count differs")
    if "gpuCompletions" in value:
        validate_raw_gpu_timing(dict(overflowed=value.get("overflowed"), scope="native-reference-direct-queue",
                                    worldIntervalIncludesShadow=True, completions=value["gpuCompletions"]))
    times, process_bytes, thread_bytes, gpu_times = [], [], [], []
    fences: dict[tuple, int] = {}
    frequencies: dict[tuple, int] = {}
    previous_end = -1
    for index, frame in enumerate(items(value.get("frames"), "enclosing frames", nonempty=True)):
        frame = obj(frame, "enclosing frame")
        require(integer(frame.get("index"), "frame index") == index, "Enclosing frame sequence is incomplete")
        require(frame.get("state", "gameplay") == "gameplay", "Non-gameplay observations cannot qualify a gameplay frame window")
        start, end = integer(frame.get("startTick"), "frame start"), integer(frame.get("endTick"), "frame end")
        require(previous_end <= start < end, "Invalid or overlapping enclosing frame timestamps")
        previous_end = end
        for name in PHASES:
            phase = obj(obj(frame.get("phases"), "phases").get(name), name)
            a, b = integer(phase.get("startTick"), name + " start"), integer(phase.get("endTick"), name + " end")
            require(start <= a <= b <= end, f"{name} lies outside enclosing total frame")
        process_bytes.append(integer(frame.get("processAllocatedBytes"), "process allocated bytes"))
        thread_bytes.append(integer(frame.get("threadAllocatedBytes"), "thread allocated bytes"))
        require(process_bytes[-1] >= thread_bytes[-1], "Process allocation interval cannot be smaller than included thread interval")
        times.append((end - start) * 1000 / frequency)
        gpu = frame.get("gpu")
        if gpu is not None:
            gpu = obj(gpu, "GPU ticks")
            stream = (text(gpu.get("queue"), "GPU queue"), integer(gpu.get("generation"), "GPU generation", 1))
            require(stream[0] == "direct", "Supplemental GPU samples must retain the existing direct queue contract")
            fence = integer(gpu.get("submittedFence"), "GPU submitted fence", 1)
            completed = integer(gpu.get("completedFence"), "GPU completed fence", 1)
            if "submittedFence" in frame:
                require(frame["submittedFence"] == fence and frame.get("generation") == gpu["generation"] and
                        frame.get("resourceGeneration") == integer(gpu.get("resourceGeneration"), "resource generation", 1),
                        "Live CPU/GPU actual submission identities differ")
            require(fences.get(stream, 0) < fence <= completed < 2**64 - 1, "GPU readback is duplicated, invalid, or not fence completed")
            fences[stream] = fence
            a, b = integer(gpu.get("startTick"), "GPU start"), integer(gpu.get("endTick"), "GPU end")
            require(a < b, "Invalid GPU timestamp interval")
            gpu_frequency = integer(gpu.get("frequencyHz"), "GPU frequency", 1)
            require(stream not in frequencies or frequencies[stream] == gpu_frequency, "GPU frequency changed within one queue generation")
            frequencies[stream] = gpu_frequency
            gpu_times.append((b - a) * 1000 / gpu_frequency)
    return dict(fullFrame=distribution(times), gpu=distribution(gpu_times),
                processAllocations=distribution(process_bytes), threadAllocations=distribution(thread_bytes))


def validate_lifecycle(value: Any) -> dict:
    """Require a moving 30-minute lifecycle record and comparable settled cycles."""
    value = obj(value, "lifecycle")
    require(type(value.get("schemaVersion")) is int and value["schemaVersion"] == 1 and value.get("kind") == "lifecycle", "Invalid lifecycle contract")
    digest(value.get("scriptSha256"), "lifecycle script hash")
    require(value.get("checkpointPolicy") == "same-workload-after-warmup-no-exclusions", "Lifecycle checkpoint policy must be declared before sampling")
    require(value.get("counterSemantics") == "live-or-outstanding-at-settled-checkpoint", "Cumulative upload/allocation totals cannot serve as memory plateau counters")
    duration = number(value.get("durationSeconds"), "soak duration", positive=True)
    require(duration >= 1800, "Lifecycle soak is shorter than 30 minutes")
    events = items(value.get("events"), "lifecycle events", nonempty=True)
    counts = {name: 0 for name in ("load", "unload", "session", "resize", "camera")}
    camera_states = set()
    camera_times = [0]
    previous_camera = None
    loaded = False
    state_ids = {"session": set(), "resize": set()}
    previous = -1
    for event in events:
        event = obj(event, "lifecycle event")
        at = number(event.get("seconds"), "event seconds")
        require(previous <= at <= duration and event.get("kind") in counts, "Invalid lifecycle event sequence")
        previous = at
        counts[event["kind"]] += 1
        if event["kind"] == "camera":
            state = digest(event.get("stateSha256"), "camera state")
            require(state != previous_camera, "Consecutive camera observations do not demonstrate movement")
            previous_camera = state
            camera_states.add(state)
            camera_times.append(at)
        elif event["kind"] == "load":
            require(not loaded, "Lifecycle load requires preceding unload")
            loaded = True
        elif event["kind"] == "unload":
            require(loaded, "Lifecycle unload without a preceding load")
            loaded = False
        else:
            state_ids[event["kind"]].add(text(event.get("stateId"), "transition state identity"))
    require(all(counts[name] >= 2 for name in ("load", "unload", "session", "resize")) and len(camera_states) >= 30,
            "Soak needs repeated load/unload/session/resize transitions and at least 30 distinct camera states")
    camera_times.append(duration)
    require(all(b - a <= 60 for a, b in zip(camera_times, camera_times[1:])), "Camera activity must span the whole soak with no observation gap above 60 seconds")
    require(all(len(values) >= 2 for values in state_ids.values()), "Session/resize events must change state, not repeat labels")
    samples = items(value.get("settledSamples"), "settled memory samples", nonempty=True)
    require(len(samples) >= 5, "A memory plateau requires at least five comparable settled cycles")
    series = {name: [] for name in MEMORY_COUNTERS}
    previous = -1
    identity = None
    for sample in samples:
        sample = obj(sample, "settled sample")
        at = number(sample.get("seconds"), "settled seconds")
        require(previous < at <= duration, "Settled sample time is invalid or duplicated")
        previous = at
        checkpoint = text(sample.get("checkpoint"), "settled checkpoint")
        identity = checkpoint if identity is None else identity
        require(identity == checkpoint, "Plateau samples need equivalent settled workload checkpoints")
        for name in MEMORY_COUNTERS:
            series[name].append(integer(sample.get(name), "memory " + name))
    require(samples[-1]["seconds"] >= 1800, "Memory observation stops before the soak ends")
    # No new slack budget: compare each later settled checkpoint to the first.
    growth = [name for name, observations in series.items() if any(x > observations[0] for x in observations[1:])]
    return dict(durationSeconds=duration, eventCounts=counts, distinctCameraStates=len(camera_states),
                settledCycles=len(samples), plateau=not growth, growingCounters=growth,
                counters={name: distribution(values) for name, values in series.items()})


def hardware_review(root: Path, hardware: Any, identities: set[str], conditions: list[dict]) -> tuple[bool, str]:
    hardware = obj(hardware, "hardware")
    qualified = boolean(hardware.get("qualified"), "hardware.qualified")
    if not qualified:
        require(hardware.get("qualificationEvidence") is None, "Unqualified hardware must not attach a claimed qualified record")
        return False, "No reviewed hardware calibration/capacity evidence"
    path = evidence(root, hardware.get("qualificationEvidence"))
    review = read_json(path)
    require(type(review.get("schemaVersion")) is int and review["schemaVersion"] == 1 and review.get("status") == "qualified", "Unsupported hardware review")
    for key in ("reviewer", "rationale", "powerPlan"):
        text(review.get(key), "hardware review " + key)
    require(utc(review.get("reviewedAtUtc"), "hardware review time") <= datetime.now(timezone.utc), "Hardware review is future dated")
    require(review.get("serialGpu") is True and review.get("powerSource") == "ac" and review.get("thermalState") == "stable", "Hardware calibration conditions are unqualified")
    require(type(review.get("maximumRegressionPercent")) in (int, float) and review["maximumRegressionPercent"] == 15, "The fixed 15-percent reference threshold cannot be replaced")
    number(review.get("capacityMinutes"), "qualified capacity minutes", positive=True)
    calibration = items(review.get("qualificationRuns"), "hardware qualification runs", nonempty=True)
    for ref in calibration:
        evidence(path.parent, ref)
    reviewed_id = digest(review.get("hardwareIdentitySha256"), "reviewed hardware identity")
    require(not identities or identities == {reviewed_id}, "Actual source/driver/device/debug/SDK differs from reviewed hardware")
    for observed in conditions:
        for key in ("powerPlan", "powerSource", "thermalState"):
            if observed.get(key) != review[key]:
                return False, f"Observed {key} does not meet reviewed hardware conditions"
    return bool(identities), "Reviewed hardware identity and run conditions match" if identities else "No native observations"


def executed_command(value: Any, root: Path, source: str) -> tuple[bool, datetime]:
    command = obj(value, "recorded command")
    require(digest(command.get("sourceSha"), "command source", 40) == source, "Executed command source mismatch")
    text(command.get("executable"), "executed executable")
    require(all(type(arg) is str for arg in items(command.get("arguments"), "executed arguments", nonempty=True)), "Command arguments must be strings")
    started = utc(command.get("startedAtUtc"), "executed start")
    require(type(command.get("exitCode")) is int, "Executed command needs an integer exit code")
    number(command.get("elapsedMilliseconds"), "executed elapsed milliseconds", positive=True)
    evidence(root, command.get("stdout"))
    evidence(root, command.get("stderr"))
    return command["exitCode"] == 0, started


def validation_imports(cohort: dict, root: Path, sources: dict, observations: dict, complete: bool) -> tuple[dict, list]:
    """Consume separately executed gates; an arbitrary caller pass flag is insufficient."""
    gates, imported, seen = {}, [], set()
    for ref in items(cohort.get("validationEvidence", []), "validationEvidence"):
        path = evidence(root, ref)
        record = read_json(path)
        kind = text(ref.get("kind"), "imported gate kind")
        require(kind == record.get("kind") and kind not in seen, "Mismatched or duplicate imported gate kind")
        seen.add(kind)
        require(type(record.get("schemaVersion")) is int and record["schemaVersion"] == 1, "Unsupported imported gate schema")
        for side in ("baseline", "candidate"):
            require(digest(record.get(side + "Sha"), "import " + side, 40) == sources[side + "Sha"], "Imported gate source mismatch")
        failures, reasons = [], []
        if kind == "required-ci":
            sides = items(record.get("sides"), "CI sides", nonempty=True)
            require(len(sides) == 2 and {x.get("role") for x in sides if type(x) is dict} == {"baseline", "candidate"}, "CI proof must cover both exact sources")
            for side in sides:
                role = side["role"]
                runs = []
                total = None
                for page_ref in items(side.get("runsPages"), "original run pages", nonempty=True):
                    page = read_json(evidence(path.parent, page_ref))
                    count = integer(page.get("total_count"), "run total count")
                    total = count if total is None else total
                    require(total == count, "CI pages have inconsistent total counts")
                    runs.extend(items(page.get("workflow_runs"), "original workflow runs"))
                for value in runs:
                    obj(value, "original workflow run")
                require(total == len(runs) and len({integer(x.get("id"), "run ID", 1) for x in runs}) == len(runs), "Incomplete or duplicate original CI pages")
                for workflow in ("ci.yml", "frame-hotpaths.yml", "scalability.yml"):
                    matching = [r for r in runs if r.get("head_sha") == sources[role + "Sha"] and
                                r.get("path", "").split("@")[0] == ".github/workflows/" + workflow]
                    if not matching:
                        reasons.append(role + ": no source-pinned " + workflow)
                        continue
                    latest = max(matching, key=lambda r: (utc(r.get("created_at"), "CI created time"), r["id"]))
                    if latest.get("status") != "completed" or latest.get("conclusion") != "success":
                        failures.append(role + ": latest " + workflow + " is not successful")
                    if workflow == "ci.yml":
                        jobs = []
                        job_total = None
                        for jobs_ref in items(side.get("jobsPages"), "original main CI job pages", nonempty=True):
                            page = read_json(evidence(path.parent, jobs_ref))
                            count = integer(page.get("total_count"), "job total")
                            job_total = count if job_total is None else job_total
                            require(job_total == count, "Job pages have inconsistent totals")
                            jobs.extend(items(page.get("jobs"), "original jobs"))
                        for value in jobs:
                            obj(value, "original job")
                        require(job_total == len(jobs) and len({integer(j.get("id"), "job ID", 1) for j in jobs}) == len(jobs), "Incomplete/duplicate job pages")
                        require(all(j.get("run_id") == latest["id"] and j.get("head_sha") == sources[role + "Sha"] and
                                    j.get("run_attempt") == latest.get("run_attempt") for j in jobs), "Jobs do not belong to latest exact-source CI attempt")
                        required = [j for j in jobs if j.get("name") == "build-test"]
                        if len(required) != 1 or required[0].get("status") != "completed" or required[0].get("conclusion") != "success":
                            failures.append(role + ": required build-test job missing/failed")
            target = "requiredCi"
        elif kind in ("content-budget", "allocation-budget", "gpu-memory-budget"):
            sides = items(record.get("sides"), "measured gate sides", nonempty=True)
            require(len(sides) == 2 and {x.get("role") for x in sides if type(x) is dict} == {"baseline", "candidate"}, "Measured gate must cover both sources")
            policy_hash = None
            for side in sides:
                role = side["role"]
                source = sources[role + "Sha"]
                succeeded, started = executed_command(side.get("command"), path.parent, source)
                if not succeeded:
                    failures.append(role + ": measurement command failed")
                measurement = read_json(evidence(path.parent, side.get("measurement")))
                require(type(measurement.get("schemaVersion")) is int and measurement["schemaVersion"] == 1 and
                        digest(measurement.get("sourceSha"), "measured source", 40) == source, "Measured gate source/schema mismatch")
                runtime_hash = digest(measurement.get("runtimeFingerprint"), "measured runtime")
                source_rows = [row for (_, r, _), row in observations.items() if r == role]
                require(not source_rows or all(row["runtimeFingerprint"] == runtime_hash for row in source_rows), "Measured budget uses different runtime assets")
                if not source_rows:
                    reasons.append(role + ": budget lacks native identity match")
                if kind == "content-budget":
                    require(measurement.get("metric") == "compiled-gameplay-texture-container-bytes", "Content gate must measure compiled gameplay texture containers")
                    value = integer(measurement.get("bytes"), "compiled gameplay texture bytes")
                    require(type(measurement.get("limitBytes")) is int and measurement["limitBytes"] == 56 * 1024 * 1024, "The fixed 56-MiB content budget cannot change")
                    if value > measurement["limitBytes"]:
                        failures.append(role + ": compiled gameplay texture budget exceeded")
                else:
                    require(digest(measurement.get("hardwareIdentitySha256"), "budget hardware") in
                            {row["hardwareIdentitySha256"] for row in source_rows}, "Budget hardware does not match measured native source")
                    policy_path = evidence(path.parent, side.get("policy"))
                    policy = read_json(policy_path)
                    require(type(policy.get("schemaVersion")) is int and policy["schemaVersion"] == 1 and policy.get("kind") == kind, "Wrong measurement budget policy")
                    text(policy.get("reviewer"), "budget reviewer")
                    text(policy.get("rationale"), "budget rationale")
                    require(utc(policy.get("reviewedAtUtc"), "budget review time") <= started, "Budgets must be reviewed before measurement")
                    current_hash = sha256(policy_path)
                    policy_hash = current_hash if policy_hash is None else policy_hash
                    require(current_hash == policy_hash, "Baseline/candidate budget policy changed")
                    samples = items(measurement.get("samples"), "budget raw samples", nonempty=True)
                    require(len(samples) >= 512 and integer(measurement.get("warmupFrames"), "budget warmups", 64) >= 64,
                            "Budget needs at least 512 raw post-warmup observations")
                    names = ("processAllocatedBytes", "threadAllocatedBytes") if kind == "allocation-budget" else MEMORY_COUNTERS
                    maxima = obj(policy.get("maximumPerSample"), "budget maxima")
                    for index, sample in enumerate(samples):
                        sample = obj(sample, "budget sample")
                        require(integer(sample.get("index"), "budget sample index") == index, "Budget sample sequence is incomplete")
                        for name in names:
                            if integer(sample.get(name), name) > integer(maxima.get(name), "budget " + name):
                                failures.append(role + ": " + name + " exceeds reviewed budget")
                        if kind == "allocation-budget":
                            require(sample["processAllocatedBytes"] >= sample["threadAllocatedBytes"], "Process/thread allocation intervals contradict each other")
                    require(measurement.get("scope") == "live-client-full-frame", "Budget observations must use the enclosing live-client frame")
            target = {"content-budget": "content", "allocation-budget": "allocation", "gpu-memory-budget": "gpuMemory"}[kind]
        elif kind == "visual-review":
            text(record.get("reviewer"), "visual reviewer")
            text(record.get("rationale"), "visual rationale")
            require(utc(record.get("reviewedAtUtc"), "visual review time") <= datetime.now(timezone.utc), "Visual review cannot be future dated")
            require(record.get("decision") in ("accepted", "rejected"), "Visual review needs an explicit decision")
            if record["decision"] != "accepted":
                failures.append("Human visual review rejected candidate")
            reviewed = set()
            for case in items(record.get("cases"), "reviewed cases", nonempty=True):
                case = obj(case, "reviewed case")
                key, pair = text(case.get("key"), "review case key"), integer(case.get("pair"), "review pair", 1)
                require((pair, key) not in reviewed, "Duplicate visual review case")
                reviewed.add((pair, key))
                for role in ("baseline", "candidate"):
                    row = observations.get((pair, role, key))
                    require(row is not None, "Review has no captured native case")
                    for prop in ("imageSha256", "manifestSha256"):
                        require(digest(case.get(role + prop[0].upper() + prop[1:]), "review " + prop) == row[prop], "Visual review bound to different native evidence")
                for criterion in ("visibility", "geometryMaterials", "motion"):
                    if case.get(criterion) != "accepted":
                        failures.append(f"{key}: {criterion} not accepted")
            expected = {(pair, key) for pair, role, key in observations if role == "baseline"}
            if not complete or reviewed != expected:
                reasons.append("Review lacks complete paired matrix")
            sequences = items(record.get("sequences"), "reviewed live sequences", nonempty=True)
            require(len(sequences) == 2 and {x.get("role") for x in sequences if type(x) is dict} == {"baseline", "candidate"}, "Both live sequences must be reviewed")
            for sequence in sequences:
                require(digest(sequence.get("sourceSha"), "sequence source", 40) == sources[sequence["role"] + "Sha"], "Reviewed sequence source differs")
                require(sequence.get("scope") == "live-animation" and sequence.get("decision") == "accepted", "Fixed camera poses do not constitute live animation review")
                evidence(path.parent, sequence.get("media"))
                number(sequence.get("durationSeconds"), "reviewed sequence duration", positive=True)
            target = "visualAcceptance"
        else:
            raise ValidationError("Unsupported imported validation gate kind")
        status = "failed" if failures else "open" if reasons else "pass"
        gates[target] = dict(status=status, reason="; ".join(dict.fromkeys(failures + reasons)) or "Preserved source-bound independent gate evidence verified")
        imported.append(dict(kind=kind, path=ref["path"], sha256=ref["sha256"], status=status))
    return gates, imported


def evaluate(cohort: dict, root: Path) -> dict:
    require(cohort.get("schemaVersion") == 1 and type(cohort["schemaVersion"]) is int, "Unsupported qualification cohort schema")
    text(cohort.get("workstream"), "workstream")
    require(cohort.get("comparisonKind") in ("optimization", "visual-candidate"), "Comparison must declare optimization or visual-candidate")
    sources = {key: digest(cohort.get(key), key, 40) for key in
               ("analysisMasterSha", "integratedFoundationSha", "integratedRepairSha", "baselineSha", "candidateSha")}
    source_sdks, source_pin_commands = {}, set()
    if cohort.get("sourcePins"):
        for role in ("baseline", "candidate"):
            pin = obj(obj(cohort["sourcePins"], "source pins").get(role), role + " source pin")
            require(pin.get("sourceSha") == sources[role + "Sha"], "SDK pin source mismatch")
            command_path = evidence(root, pin.get("globalJsonCommand"))
            command = read_json(command_path)
            source_pin_commands.add(hashlib.sha256(canonical(command)).hexdigest())
            args = items(command.get("arguments"), "source pin command arguments")
            require(command.get("exitCode") == 0 and len(args) == 4 and args[0] == "-C" and args[2:] == ["show", sources[role + "Sha"] + ":global.json"], "SDK pin is not read from the exact Git source")
            global_path = evidence(command_path.parent, command.get("stdout"))
            source_sdks[role] = text(obj(read_json(global_path).get("sdk"), "SDK pin").get("version"), "pinned SDK")
            provenance = read_json(evidence(root, pin.get("provenance")))
            require(provenance.get("sourceRevision") == sources[role + "Sha"] and provenance.get("sdk") == source_sdks[role], "Package SDK provenance differs from exact source")
    plan = validate_plan(cohort.get("plan"))
    plan_path = evidence(root, cohort.get("planEvidence"))
    require(read_json(plan_path) == plan, "Executed plan differs from immutable predeclared plan")
    plan_digest = hashlib.sha256(canonical(plan)).hexdigest()
    for ref in items(cohort.get("inventory", []), "inventory"):
        evidence(root, ref)
    other_command_failures = []
    journalled_commands = set()
    for reference in items(cohort.get("commands", []), "command artifacts"):
        command_path = evidence(root, reference)
        record = read_json(command_path)
        require(type(record.get("exitCode")) is int, "Command artifact needs an explicit exit code")
        for name in ("stdout", "stderr", "monitor"):
            if record.get(name) is not None:
                evidence(command_path.parent, record[name])
        journalled_commands.add(hashlib.sha256(canonical(record)).hexdigest())
        if record["exitCode"] != 0:
            other_command_failures.append(reference["path"])
    require(source_pin_commands <= journalled_commands, "SDK source-pin commands must belong to the immutable executed command journal")
    gates: dict[str, dict] = {}
    def gate(name: str, status: str, reason: str) -> None:
        gates[name] = dict(status=status, reason=reason)
    observations, missing, command_failures = {}, [], []
    identities: set[str] = set()
    paths, hashes, run_keys = set(), set(), set()
    observed_conditions = []
    native_records = []
    case_identities, role_identities, command_intervals = {}, {}, []
    for run in items(cohort.get("runs"), "runs"):
        run = obj(run, "run")
        pair = integer(run.get("pair"), "pair", 1)
        require(pair <= plan["repetitions"] and run.get("role") in ("baseline", "candidate"), "Unexpected pair/role")
        role = run["role"]
        variant = (run.get("runProfile"), run.get("cameraPath"))
        require(variant in VARIANTS, "Unexpected run variant")
        first = "baseline" if pair % 2 else "candidate"
        require(integer(run.get("position"), "position") == (0 if role == first else 1), "Paired execution order is not counterbalanced")
        run_key = (pair, role, *variant)
        require(run_key not in run_keys, "Duplicate run in paired cohort")
        run_keys.add(run_key)
        command = obj(run.get("command"), "command")
        require(hashlib.sha256(canonical(command)).hexdigest() in journalled_commands, "Run command has no matching immutable command journal")
        text(command.get("executable"), "command executable")
        for argument in items(command.get("arguments"), "command arguments"):
            require(type(argument) is str, "Command arguments must be strings")
        command_start = utc(command.get("startedAtUtc"), "command start").timestamp()
        elapsed = number(command.get("elapsedMilliseconds"), "command elapsed milliseconds")
        command_intervals.append((command_start, command_start + elapsed / 1000, pair, run["position"], variant))
        require(type(command.get("exitCode")) is int, "Every completed command needs an explicit integer exit code")
        if command["exitCode"] != 0:
            command_failures.append(dict(pair=pair, role=role, variant=list(variant), exitCode=command["exitCode"]))
            continue
        require(text(run.get("sdk"), "SDK") == source_sdks.get(role, "10.0.401"), "SDK differs from its source-pinned package evidence (legacy cohorts retain their original qualified SDK)")
        conditions = obj(run.get("conditions"), "run conditions")
        for key in ("powerPlan", "powerSource", "thermalState"):
            text(conditions.get(key), "conditions." + key)
        if conditions["thermalState"] != "stable":
            text(conditions.get("thermalReason"), "unavailable thermal reason")
        observed_conditions.append(conditions)
        if "conditionsAfter" in run:
            after_conditions = obj(run["conditionsAfter"], "conditionsAfter")
            for key in ("powerPlan", "powerSource", "thermalState"):
                text(after_conditions.get(key), "after conditions." + key)
            observed_conditions.append(after_conditions)
        path = evidence(root, dict(path=run.get("manifest"), sha256=run.get("manifestSha256")))
        normalized_path = str(path.resolve()).casefold()
        file_hash = sha256(path)
        require(normalized_path not in paths and file_hash not in hashes, "A native artifact cannot be reused as an independent repetition")
        paths.add(normalized_path)
        hashes.add(file_hash)
        rows, absent = validate_native(read_json(path), path.parent, run, plan, sources[role + "Sha"])
        actual_identity = next(iter(rows.values()))["hardwareIdentitySha256"] if rows else None
        if actual_identity is not None:
            validate_physical_run_conditions(conditions, actual_identity, command_start, command_start + elapsed / 1000, after=False)
            if "conditionsAfter" in run:
                validate_physical_run_conditions(after_conditions, actual_identity, command_start, command_start + elapsed / 1000, after=True)
                if "hardwarePolicySha256" in conditions:
                    require(conditions["hardwarePolicySha256"] == after_conditions.get("hardwarePolicySha256"), "Hardware policy changed during the native run")
        missing.extend(dict(pair=pair, role=role, key=key) for key in absent)
        for key, row in rows.items():
            source_identity = (row["runtimeFingerprint"], row["binaryIdentity"])
            require(role not in role_identities or role_identities[role] == source_identity, "Runtime assets or binaries changed between repetitions of the same source")
            role_identities[role] = source_identity
            case_identity = (row["workload"], row["profile"], row["gpuFrequencyHz"])
            require((role, key) not in case_identities or case_identities[(role, key)] == case_identity,
                    "Fixture/profile/timestamp frequency changed between repetitions")
            case_identities[(role, key)] = case_identity
            row["manifestSha256"] = file_hash
            observations[(pair, role, key)] = row
            identities.add(row["hardwareIdentitySha256"])
        native_records.append(dict(pair=pair, role=role, variant=list(variant), manifest=run["manifest"], sha256=file_hash,
                                   captures=len(rows), command=command))
    require(len(identities) <= 1, "Hardware/driver/debug/runtime/SDK changed between paired runs")
    previous_end, previous_order = -math.inf, (-1, -1)
    for start, end, pair, position, variant in sorted(command_intervals):
        require(previous_end <= start, "Native commands overlap; a performance GPU must run one capture at a time")
        require((pair, position) >= previous_order, "Recorded execution times contradict counterbalanced pair order")
        previous_end, previous_order = end, (pair, position)
    expected_runs = set(itertools.product(range(1, plan["repetitions"] + 1), ("baseline", "candidate"), VARIANTS))
    absent_runs = [dict(pair=p, role=r, runProfile=v[0], cameraPath=v[1]) for p, r, v in sorted(expected_runs)
                   if (p, r, *v) not in run_keys]
    complete = not absent_runs and not missing and not command_failures
    comparisons, zero_denominators, changed_quality = [], [], []
    partial_gpu = [dict(pair=pair, role=role, key=key, expected=row["rawCpuCount"], observed=row["rawGpuCount"])
                   for (pair, role, key), row in observations.items() if row["rawGpuCount"] != row["rawCpuCount"]]
    regressions = []
    for pair in range(1, plan["repetitions"] + 1):
        for variant in VARIANTS:
            for key in sorted(capture_keys(plan, *variant)):
                before, after = observations.get((pair, "baseline", key)), observations.get((pair, "candidate", key))
                if before is None or after is None:
                    continue
                require(before["workload"] == after["workload"], "Scene/seed/camera/lighting/resolution inputs changed")
                quality_changed = before["runtimeFingerprint"] != after["runtimeFingerprint"] or before["profile"] != after["profile"]
                require(not quality_changed or cohort["comparisonKind"] == "visual-candidate", "Optimization changed runtime assets or effective quality profile")
                if quality_changed:
                    changed_quality.append(dict(pair=pair, key=key))
                row = dict(pair=pair, key=key, baseline=before, candidate=after, qualityChanged=quality_changed, metrics={})
                for metric in ("cpu", "gpu"):
                    deltas = compare_distribution(before[metric], after[metric])
                    row["metrics"][metric] = deltas
                    for delta in deltas:
                        identity = dict(pair=pair, key=key, metric=metric, statistic=delta["statistic"])
                        if delta["percent"] is None:
                            zero_denominators.append(identity)
                        if delta["exceedsThreshold"]:
                            regressions.append(identity)
                comparisons.append(row)
    qualified, hardware_reason = hardware_review(root, cohort.get("hardware"), identities, observed_conditions)
    software = any(row["software"] for row in observations.values())
    if software:
        qualified, hardware_reason = False, "Software adapters provide functional evidence only"
    gate("artifactIntegrity", "pass", "Declared manifests/images/evidence match preserved SHA256; hashes detect changes, not authorship")
    gate("matrix", "pass" if complete else "failed" if command_failures else "open", "Full canonical paired matrix recorded" if complete else "Required cases/runs are missing or failed; no subset promotion")
    gate("hardware", "pass" if qualified else "open", hardware_reason)
    gate("content", "open", "Native binding checks pass where captured; unchanged compiled texture-byte/content budget remains an independent required CI gate")
    gate("referenceTiming", "failed" if qualified and regressions and not changed_quality else "open" if not complete or not qualified or zero_denominators or partial_gpu or changed_quality else "pass",
         "Per-pair raw CPU/GPU tails use the fixed 15-percent threshold; shared/software/changed-quality/unavailable data cannot qualify")
    gate("fullFrame", "open", "Frozen native captures omit live host/event/camera work; PrepareTerrain+RenderFrame is not total gameplay time")
    gate("gpuTimestampIntegrity", "open", "Native completed-fence IDs/frequency are retained; original begin/end ticks and explicit completion values are not serialized")
    gate("gpuMemory", "open", "Logical texture/DXGI/count observations cannot establish complete live/retiring/staging/mip byte budgets")
    gate("allocation", "open", "Native current-thread allocations do not measure process-wide GC allocations or satisfy the independent allocation budget")
    gate("lifecycle", "open", "Requires measured 30-minute moving workload with repeated load/unload/session/resize and comparable memory checkpoints")
    gate("visualAcceptance", "open", "Native images are preserved; motion-pose captures and hashes do not replace explicit human image/sequence review")
    supplemental_rows, supplemental_seen = [], set()
    full_frames, gpu_verified = set(), set()
    frame_results, stream_ids = {}, set()
    lifecycle_roles = set()
    for reference in items(cohort.get("supplemental", []), "supplemental"):
        path = evidence(root, reference)
        require(str(path.resolve()).casefold() not in supplemental_seen, "Duplicate supplemental evidence")
        supplemental_seen.add(str(path.resolve()).casefold())
        record = read_json(path)
        require(record.get("kind") == reference.get("kind"), "Supplemental kind differs from reference")
        role = record.get("role")
        require(role in ("baseline", "candidate") and digest(record.get("sourceSha"), "supplemental source", 40) == sources[role + "Sha"], "Supplemental source/side mismatch")
        require(digest(record.get("hardwareIdentitySha256"), "supplemental hardware") in identities, "Supplemental hardware identity mismatch")
        if record["kind"] == "frame-envelope":
            lookup = (integer(record.get("pair"), "supplemental pair", 1), role, text(record.get("caseKey"), "supplemental case"))
            require(lookup in observations and lookup not in full_frames, "Supplemental frames have no unique matching native case")
            native = observations[lookup]
            if "actualDevice" in record:
                require(record["actualDevice"] == native["actualDevice"], "Live output adapter differs from native case")
            require(digest(record.get("runtimeFingerprint"), "supplemental runtime") == native["runtimeFingerprint"] and
                    text(record.get("fixtureFingerprint"), "supplemental fixture") == native["fixtureFingerprint"], "Supplemental workload does not match native case")
            if "rawTelemetry" in record:
                original = read_json(evidence(path.parent, record["rawTelemetry"]))
                context = read_json(evidence(path.parent, record.get("bindingContext")))
                require(all(original.get(name) == record.get(name) for name in ("sourceSha", "runtimeFingerprint", "actualDevice", "frames", "gpuCompletions", "streamId", "frequencyHz", "window", "overflowed")), "Bound live measurements differ from the immutable original stream")
                require(all(context.get(name) == record.get(name) for name in ("sourceSha", "role", "pair", "caseKey", "runtimeFingerprint", "hardwareIdentitySha256", "fixtureFingerprint")), "Bound live workload context was changed")
            result = validate_frame_envelope(record)
            require(record["streamId"] not in stream_ids, "One live-client stream cannot be relabelled as independent repetitions/cases")
            stream_ids.add(record["streamId"])
            succeeded, _ = executed_command(record.get("command"), path.parent, sources[role + "Sha"])
            require(succeeded, "Live frame collection command failed")
            require(result["fullFrame"]["samples"] == native["rawCpuCount"], "Supplemental frame count differs from planned native case")
            require(all(frame["gpu"] is None or native["gpuFrequencyHz"] == frame["gpu"]["frequencyHz"] for frame in record["frames"]),
                    "Independent live-client GPU frequency differs from the same hardware native measurement")
            full_frames.add(lookup)
            frame_results[lookup] = result
            if result["gpu"]["samples"] == result["fullFrame"]["samples"]:
                gpu_verified.add(lookup)
        elif record["kind"] == "lifecycle":
            require(role not in lifecycle_roles, "Duplicate lifecycle side")
            result = validate_lifecycle(record)
            script = evidence(path.parent, record.get("script"))
            require(sha256(script) == digest(record.get("scriptSha256"), "lifecycle script hash"), "Soak script identity mismatch")
            succeeded, _ = executed_command(record.get("command"), path.parent, sources[role + "Sha"])
            require(succeeded and record["command"]["elapsedMilliseconds"] >= result["durationSeconds"] * 1000,
                    "Lifecycle command failed or ran for less than the recorded soak duration")
            require(digest(record.get("runtimeFingerprint"), "lifecycle runtime") in
                    {row["runtimeFingerprint"] for (_, r, _), row in observations.items() if r == role}, "Lifecycle uses different runtime assets")
            lifecycle_roles.add(role)
            if not result["plateau"]:
                gate("lifecycle", "failed", "Comparable settled memory checkpoints grow above their first observation")
        else:
            raise ValidationError("Unsupported supplemental evidence kind")
        supplemental_rows.append(dict(kind=record["kind"], role=role, path=reference["path"], sha256=reference["sha256"], result=result))
    full_comparisons, full_regressions, full_unavailable = [], [], []
    for (pair, role, key), result in frame_results.items():
        if role != "baseline" or (pair, "candidate", key) not in frame_results:
            continue
        candidate = frame_results[(pair, "candidate", key)]
        deltas = compare_distribution(result["fullFrame"], candidate["fullFrame"])
        full_comparisons.append(dict(pair=pair, key=key, baseline=result["fullFrame"], candidate=candidate["fullFrame"], deltas=deltas))
        full_regressions.extend(dict(pair=pair, key=key, statistic=d["statistic"]) for d in deltas if d["exceedsThreshold"])
        full_unavailable.extend(dict(pair=pair, key=key, statistic=d["statistic"]) for d in deltas if d["percent"] is None)
    if complete and observations and full_frames == observations.keys() and qualified and not changed_quality:
        gate("fullFrame", "failed" if full_regressions else "open" if full_unavailable else "pass",
             "Paired enclosing live-client frame tails checked against unchanged 15-percent limit; overlapping phase times are not added")
    if complete and observations and gpu_verified == observations.keys() and qualified:
        gate("gpuTimestampIntegrity", "pass", "Every planned supplemental GPU sample has valid original ticks and completed unique stream fence")
    if lifecycle_roles == {"baseline", "candidate"} and gates["lifecycle"]["status"] != "failed":
        gate("lifecycle", "pass", "Both sources retain moving 30-minute transition sequences and conservative settled plateaus")
    collector = cohort.get("collectorStatus")
    if collector is not None:
        collector = obj(collector, "collectorStatus")
        require(collector.get("status") in ("completed", "failed", "plan-only"), "Unknown collector status")
        if collector["status"] != "completed":
            gate("execution", "failed" if collector["status"] == "failed" else "open", text(collector.get("reason"), "collector reason"))
    gate("requiredCi", "open", "Source-pinned required CI/build/content/allocation gates must be independently verified; collector success cannot waive them")
    imported_gates, imported_records = validation_imports(cohort, root, sources, observations, complete)
    gates.update(imported_gates)
    if other_command_failures:
        gate("execution", "failed", "Preserved command failures: " + ", ".join(other_command_failures))
    state = "failed" if any(g["status"] == "failed" for g in gates.values()) else "open" if any(g["status"] != "pass" for g in gates.values()) else "accepted"
    # Independent repetitions are the experimental units; pooled frame tails do not substitute for them.
    pair_summary = []
    for key in sorted({row["key"] for row in comparisons}):
        for metric in ("cpu", "gpu"):
            matched = [row for row in comparisons if row["key"] == key]
            values = [delta["deltaMs"] for row in matched for delta in row["metrics"][metric]
                      if delta["statistic"] == "mean" and delta["deltaMs"] is not None]
            pair_summary.append(dict(key=key, metric=metric, pairs=len(values), meanDeltaMs=statistics.fmean(values) if values else None,
                                     minimumDeltaMs=min(values) if values else None, maximumDeltaMs=max(values) if values else None,
                                     standardDeviationMs=statistics.stdev(values) if len(values) > 1 else None))
    return dict(schemaVersion=1, status=state, workstream=cohort["workstream"], comparisonKind=cohort["comparisonKind"],
                sources=sources, planDigest=plan_digest, gates=gates, hardwareIdentitySha256=next(iter(identities), None),
                plannedCapturesPerSidePerPair=216, nativeRecords=native_records, missingRuns=absent_runs, missingCases=missing,
                commandFailures=command_failures, comparisons=comparisons, pairSummary=pair_summary, regressions=regressions,
                unavailablePercentChanges=zero_denominators, changedQualityCases=changed_quality, supplemental=supplemental_rows,
                incompleteGpuWindows=partial_gpu,
                fullFrameComparisons=full_comparisons, fullFrameRegressions=full_regressions, validationEvidence=imported_records,
                interpretation="Raw files remain authoritative. No outliers removed, no overlapping CPU/GPU/pass times added, no baseline promoted. Valid reporting is not product acceptance.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--cohort", type=Path)
    mode.add_argument("--validate-json", type=Path, nargs="+", help="Strict syntax/type-root check before native work; no acceptance claim")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--require-accepted", action="store_true")
    args = parser.parse_args()
    try:
        if args.validate_json is not None:
            require(args.output is None and not args.require_accepted, "JSON parsing mode does not produce a qualification report")
            for path in args.validate_json:
                read_json(path)
            print(f"Valid JSON objects: {len(args.validate_json)}; acceptance semantics have not been evaluated")
            return 0
        require(args.output is not None, "--cohort requires --output")
        require(not args.output.exists(), "Output already exists; use a new report path")
        require(args.output.resolve() != args.cohort.resolve(), "Output cannot replace cohort input")
        cohort = read_json(args.cohort)
        # Reports may be adjacent to the cohort, but never inside a native baseline/candidate directory.
        for run in items(cohort.get("runs"), "runs"):
            if type(run) is dict and run.get("manifest"):
                native_root = (args.cohort.parent / run["manifest"]).parent.resolve()
                require(not args.output.resolve().is_relative_to(native_root), "Report cannot modify native evidence directories")
        result = evaluate(cohort, args.cohort.parent)
        write_new(args.output, result)
        print(f"Render qualification: {result['status']}; report {args.output}")
        return 3 if args.require_accepted and result["status"] != "accepted" else 0
    except (OSError, UnicodeError, json.JSONDecodeError, ValidationError, OverflowError, RecursionError) as exc:
        print(f"INVALID: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
