#!/usr/bin/env python3
"""Compile production composite shader variants without changing product sources.

The report contains static DXIL instruction/call-site counts, not GPU ISA costs,
executed texture loads, hardware timings, or visual qualification. Specialized AA
sources are compiler-analysis fixtures; runtime-AA variants are retained intact.
"""

from __future__ import annotations

import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time


SHADER_PATH = "src/ForgeLine.Graphics/SceneCompositeShader.cs"
POST_PATH = "src/ForgeLine.Graphics/GraphicsPostProcessingSettings.cs"
HAZE_PATH = "src/ForgeLine.Graphics/GraphicsLocalHazeSettings.cs"
FLAG = "COMPOSITION_SHARED_LINEAR"
AA_TOKEN = re.compile(r"\binput\s*\.\s*Post\s*\.\s*x\b")
DECLARATION = re.compile(
    r"^[ \t]*internal\s+const\s+string\s+(Shared|Vertex|Pixel)\s*=\s*"
    r"(?:(Shared)\s*\+\s*)?(\"{3,})[ \t]*\r?\n",
    re.MULTILINE,
)
INSTRUCTION = re.compile(
    r"^\s+(?:%[\w.$-]+\s*=\s*)?(?:(?:tail|musttail|notail)\s+)?"
    r"(add|addrspacecast|alloca|and|ashr|atomicrmw|bitcast|br|call|callbr|"
    r"catchpad|catchret|catchswitch|cleanuppad|cleanupret|cmpxchg|extractelement|"
    r"extractvalue|fadd|fcmp|fdiv|fence|fmul|fneg|fpext|fptosi|fptoui|fptrunc|"
    r"freeze|frem|fsub|getelementptr|icmp|indirectbr|insertelement|insertvalue|"
    r"inttoptr|invoke|landingpad|load|lshr|mul|or|phi|ptrtoint|resume|ret|sdiv|"
    r"select|sext|shl|shufflevector|sitofp|srem|store|sub|switch|trunc|udiv|"
    r"uitofp|unreachable|urem|va_arg|xor|zext)\b"
)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def git(repository: Path, *arguments: str) -> bytes:
    return subprocess.run(
        ["git", "-C", str(repository), *arguments],
        check=True,
        capture_output=True,
    ).stdout


def extract_shader_strings(source: bytes) -> dict[str, str]:
    """Accept only the three expected non-interpolated multiline C# raw literals.

    Closing-delimiter indentation determines trimming, as in a C# raw string.
    Unexpected concatenation, missing/duplicate fields and indentation fail closed.
    The source's original line endings are retained inside the extracted strings.
    """
    text = source.decode("utf-8-sig")
    result: dict[str, str] = {}
    for opening in DECLARATION.finditer(text):
        name, prefix, quotes = opening.groups()
        if name in result or (name == "Shared") != (prefix is None):
            raise ValueError(f"Unexpected declaration or duplicate raw string: {name}")
        closing_pattern = re.compile(
            r"^(?P<indent>[ \t]*)" + re.escape(quotes) + r";[ \t]*(?:\r?$)",
            re.MULTILINE,
        )
        closing = closing_pattern.search(text, opening.end())
        if closing is None:
            raise ValueError(f"Missing raw-string closing delimiter: {name}")
        body = text[opening.end():closing.start()]
        if body.endswith("\r\n"):
            body = body[:-2]
        elif body.endswith("\n"):
            body = body[:-1]
        else:
            raise ValueError(f"Expected multiline raw-string terminator: {name}")
        indent = closing.group("indent")
        lines = []
        for line in body.splitlines(keepends=True):
            content = line.rstrip("\r\n")
            if content.startswith(indent):
                lines.append(line[len(indent):])
            elif not content.strip() and indent.startswith(content):
                lines.append(line[len(content):])
            else:
                raise ValueError(f"Invalid raw-string indentation in {name}")
        literal = "".join(lines)
        if prefix and prefix not in result:
            raise ValueError(f"Raw-string prefix must be declared before {name}")
        result[name] = result[prefix] + literal if prefix else literal
    if set(result) != {"Shared", "Vertex", "Pixel"}:
        raise ValueError("Expected exactly Shared, Vertex and Pixel raw-string declarations")
    return result


def aa_values(source: bytes) -> dict[str, str | None]:
    text = source.decode("utf-8-sig")
    values: dict[str, str | None] = {"runtime": None, "disabled": "0.0"}
    for label in ("Low", "High"):
        matches = re.findall(
            r"SpatialAntialiasing\." + label + r"\s*=>\s*([0-9]*\.[0-9]+|[0-9]+)f?\b",
            text,
        )
        if len(matches) != 1:
            raise ValueError(f"Cannot establish current {label} AA coefficient")
        values[label.lower()] = str(float(matches[0]))
    return values


def static_counts(disassembly: str) -> dict[str, object]:
    instructions: Counter[str] = Counter()
    calls: Counter[str] = Counter()
    in_function = False
    for line in disassembly.splitlines():
        if line.startswith("define "):
            in_function = True
            continue
        if in_function and line == "}":
            in_function = False
            continue
        if not in_function:
            continue
        instruction = INSTRUCTION.match(line)
        if instruction:
            instructions[instruction[1]] += 1
            call = re.search(r"\bcall\b.*?@dx\.op\.([\w.]+)\(", line)
            if call:
                calls[call[1]] += 1
    if not instructions:
        raise ValueError("No LLVM IR instructions recognized in compiler disassembly")
    return {
        "llvmIrInstructions": sum(instructions.values()),
        "textureLoadCallSites": sum(count for name, count in calls.items() if name.startswith("textureLoad.")),
        "dxOpCallSites": sum(calls.values()),
        "instructionOpcodes": dict(sorted(instructions.items())),
        "dxOpIntrinsics": dict(sorted(calls.items())),
    }


def write_report(output: Path, report: dict[str, object]) -> None:
    temporary = output / "manifest.json.tmp"
    temporary.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    temporary.replace(output / "manifest.json")


def compile_case(
    compiler: Path, output: Path, name: str, source: str,
    stage: str, defines: dict[str, int], timeout: float, metadata: dict[str, object],
) -> dict[str, object]:
    source_path = output / f"{name}.hlsl"
    object_path = output / f"{name}.dxil"
    assembly_path = output / f"{name}.ll"
    source_bytes = source.encode("utf-8")
    source_path.write_bytes(source_bytes)
    entry = "VSMain" if stage == "vertex" else "PSMain"
    profile = "vs_6_0" if stage == "vertex" else "ps_6_0"
    # Match DxcShaderCompiler Release arguments, adding only output destinations.
    arguments = [str(compiler), str(source_path), "-E", entry, "-T", profile,
                 "-HV", "2021", "-Ges", "-WX", "-O3"]
    for key, value in defines.items():
        arguments.extend(["-D", f"{key}={value}"])
    arguments.extend(["-Fo", str(object_path), "-Fc", str(assembly_path)])
    row: dict[str, object] = {
        "name": name, "stage": stage, **metadata, "compilerArguments": arguments,
        "hlsl": source_path.name, "hlslSha256": sha256(source_bytes),
        "startedAtUtc": utc_now(), "status": "failed", "exitCode": None,
        "gpuMilliseconds": None, "staticCounts": None,
    }
    started = time.monotonic()
    try:
        result = subprocess.run(arguments, capture_output=True, timeout=timeout)
        row["exitCode"] = result.returncode
        row["stdout"] = result.stdout.decode("utf-8", errors="replace")
        row["stderr"] = result.stderr.decode("utf-8", errors="replace")
        if result.returncode == 0:
            bytecode = object_path.read_bytes()
            assembly = assembly_path.read_bytes()
            row.update({
                "dxil": object_path.name, "dxilSha256": sha256(bytecode),
                "dxilBytes": len(bytecode), "disassembly": assembly_path.name,
                "disassemblySha256": sha256(assembly),
                "staticCounts": static_counts(assembly.decode("utf-8")), "status": "compiled",
            })
    except subprocess.TimeoutExpired as error:
        row["error"] = f"Compiler exceeded {timeout} seconds"
        row["stdout"] = (error.stdout or b"").decode("utf-8", errors="replace")
        row["stderr"] = (error.stderr or b"").decode("utf-8", errors="replace")
    except (OSError, ValueError) as error:
        row["error"] = str(error)
    row["compilerWallSeconds"] = time.monotonic() - started
    return row


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository-root", type=Path, default=Path(__file__).resolve().parents[3])
    parser.add_argument("--base-ref", required=True, help="Verified baseline commit or ref; resolved to its full SHA")
    parser.add_argument("--dxc", default="dxc", help="Official DXC executable; uses existing installation")
    parser.add_argument("--output", type=Path, required=True, help="Fresh artifact directory outside sources")
    parser.add_argument("--timeout-seconds", type=float, default=60)
    arguments = parser.parse_args()
    repository = arguments.repository_root.resolve()
    output = arguments.output.resolve()
    if arguments.timeout_seconds <= 0:
        parser.error("--timeout-seconds must be positive")
    if output.exists():
        parser.error("Use a fresh artifact directory; evidence is never overwritten")
    if output.is_relative_to(repository) and not output.is_relative_to(repository / "artifacts"):
        parser.error("Output must be outside the checkout or beneath its artifacts directory")
    executable = shutil.which(arguments.dxc)
    if executable is None:
        parser.error("DXC executable not found")
    compiler = Path(executable).resolve()
    version = subprocess.run([str(compiler), "--version"], check=True, capture_output=True)
    base_sha = git(repository, "rev-parse", "--verify", "--end-of-options", arguments.base_ref + "^{commit}").decode().strip()
    head_sha = git(repository, "rev-parse", "HEAD").decode().strip()
    sources = {
        "baseline": {path: git(repository, "show", f"{base_sha}:{path}") for path in (SHADER_PATH, POST_PATH, HAZE_PATH)},
        "current": {path: (repository / path).read_bytes() for path in (SHADER_PATH, POST_PATH, HAZE_PATH)},
    }
    shader_strings = {name: extract_shader_strings(files[SHADER_PATH]) for name, files in sources.items()}
    coefficients = {name: aa_values(files[POST_PATH]) for name, files in sources.items()}
    default_marker = re.findall(r"^\s*#define\s+" + FLAG + r"\s+([01])\s*$", shader_strings["current"]["Pixel"], re.MULTILINE)
    if default_marker != ["0"]:
        raise ValueError("Expected exactly one disabled shared-linear default marker; product opt-in needs separate native review")
    haze_prelude = (
        "#define LOCAL_HAZE 1\n"
        "static const float3 HazeCenter=float3(900,15,750);\n"
        "static const float HazeRadius=450;\n"
        "static const float HazeDensity=0.001;\n"
    )
    compiler_files = {"executableSha256": sha256(compiler.read_bytes())}
    library = compiler.parent.parent / "lib" / "libdxcompiler.so"
    if library.is_file():
        compiler_files["libdxcompilerSha256"] = sha256(library.read_bytes())
    report: dict[str, object] = {
        "schemaVersion": 1, "status": "running", "startedAtUtc": utc_now(),
        "measurementKind": "static-dxil-compilation", "hardwareQualified": False,
        "gpuMilliseconds": None, "baseCommit": base_sha, "currentCommit": head_sha,
        "currentSourcesDirty": bool(git(repository, "status", "--porcelain=v1", "--", SHADER_PATH, POST_PATH, HAZE_PATH)),
        "sourceFiles": {name: {path: sha256(data) for path, data in files.items()} for name, files in sources.items()},
        "compiler": {"path": str(compiler), "versionExitCode": version.returncode,
                     "version": (version.stdout + version.stderr).decode("utf-8", errors="replace").strip(), **compiler_files},
        "aaCoefficients": coefficients,
        "localHazeFixture": {"center": [900, 15, 750], "radiusMeters": 450, "density": 0.001},
        "interpretation": (
            "LLVM IR instructions and texture-load call sites are static compiler counts. "
            "They are not device ISA instructions, runtime samples, register pressure, bandwidth, "
            "overdraw, GPU timing or a hardware performance gate. Constant-AA fixtures replace "
            "input.Post.x only in exported HLSL; runtime-AA variants retain the product expression. "
            "Local haze with atmosphere disabled is compiled for preprocessor coverage but is not "
            "reachable through the production lifecycle. Candidate defines affect extracted files only."
        ),
        "cases": [],
    }
    output.mkdir(parents=True)
    for family, files in sources.items():
        (output / f"{family}-SceneCompositeShader.cs").write_bytes(files[SHADER_PATH])
    write_report(output, report)
    rows = report["cases"]
    for family in ("baseline", "current"):
        rows.append(compile_case(compiler, output, f"{family}.vertex", shader_strings[family]["Vertex"],
                                 "vertex", {}, arguments.timeout_seconds, {"sourceFamily": family}))
        write_report(output, report)
    for family, variant, defines in (
        ("baseline", "baseline", {}),
        ("current", "default", {}),
        ("current", "shared-linear", {FLAG: 1}),
    ):
        for scaled in (False, True):
            for atmosphere in (False, True):
                for haze in (False, True):
                    prefix = (haze_prelude if haze else "") + f"#define UPSAMPLE {int(scaled)}\n#define ATMOSPHERE {int(atmosphere)}\n"
                    for aa, coefficient in coefficients[family].items():
                        shader = shader_strings[family]["Pixel"]
                        replacements = 0
                        if coefficient is not None:
                            shader, replacements = AA_TOKEN.subn(f"({coefficient})", shader)
                            if replacements == 0:
                                raise ValueError("AA specialization did not find the expected product expression")
                        name = f"{variant}.{'scaled' if scaled else 'native'}.atmosphere{int(atmosphere)}.haze{int(haze)}.aa-{aa}"
                        row = compile_case(compiler, output, name, prefix + shader, "pixel", defines,
                                           arguments.timeout_seconds, {
                            "sourceFamily": family, "variant": variant, "scaled": scaled,
                            "atmosphere": atmosphere, "localHaze": haze,
                            "productionMacroCombination": not haze or atmosphere,
                            "aa": aa, "aaConstant": coefficient, "aaReplacements": replacements,
                            "sourceSpecializedForCosting": coefficient is not None,
                            "candidateOverride": defines,
                        })
                        rows.append(row)
                        write_report(output, report)
                        if row["status"] != "compiled":
                            print(f"FAILED {name}: {row.get('stderr', row.get('error', 'unknown error'))}", file=sys.stderr)
        print(f"Completed {variant} compiler matrix", flush=True)
    failures = [row["name"] for row in rows if row["status"] != "compiled"]
    report.update({"status": "compile-failed" if failures else "compiled-not-hardware-qualified",
                   "finishedAtUtc": utc_now(), "caseCount": len(rows), "failures": failures})
    write_report(output, report)
    print(f"{len(rows) - len(failures)}/{len(rows)} variants compiled; report: {output / 'manifest.json'}")
    print("Static DXIL evidence only. Native image and paired hardware performance acceptance remain open.")
    return 1 if failures else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(f"Composite compilation failed: {error}", file=sys.stderr)
        sys.exit(2)
