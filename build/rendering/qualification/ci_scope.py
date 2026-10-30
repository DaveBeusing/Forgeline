"""Select strict CI path checking for a serial foundation proposal or legacy lane.

A passing proposal is not integration approval or native-run admission. The
historical validator and manually trusted-master gates remain unchanged.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import sys
from qualification import ValidationError, require
from telemetry_contracts import initiative_ownership

CANONICAL = "docs/rendering/render-engine-upgrade/Ownership.json"
HISTORICAL = "docs/rendering/parallelization/ParallelRenderOwnership.json"
WORKFLOW = ".github/workflows/render-qualification.yml"
PACKAGE = "0_CompleteRenderTelemetry"
LEGACY_PACKAGE = "RenderPerformanceAndVisualQualification"
# Reviewed bootstrap footprint. Never derive this allowlist from the proposal.
BOOTSTRAP_OWNS = ('src/ForgeLine.Client/ClientRenderHost.cs', 'src/ForgeLine.Client/VisualReferenceCapture.cs', 'src/ForgeLine.Client/ReferencePerformanceSamples.cs', 'src/ForgeLine.Graphics/D3D12GraphicsDevice.cs', 'src/ForgeLine.Graphics/GraphicsDiagnostics.cs', 'src/ForgeLine.Graphics/GraphicsResourceRetirement.cs', 'src/ForgeLine.Graphics/ResourceScheduling/D3D12UploadArena.cs', 'src/ForgeLine.Presentation/RenderContracts/RenderAssetFrameMetrics.cs', 'tests/ForgeLine.Client.Tests/ReferencePerformanceSamplesTests.cs', 'tests/ForgeLine.Client.Tests/RenderQualification/', 'tests/ForgeLine.Graphics.Tests/ResourceScheduling/', 'tests/ForgeLine.Graphics.Tests/GraphicsResourceRetirementTests.cs', 'build/rendering/qualification/', 'docs/rendering/render-engine-upgrade/Telemetry.md', 'docs/rendering/render-engine-upgrade/Ownership.json', 'src/ForgeLine.Graphics/D3D12GraphicsBuffer.cs', 'src/ForgeLine.Graphics/RenderTelemetry/', 'src/ForgeLine.Client/RenderTelemetry/', 'tests/ForgeLine.Graphics.Tests/RenderTelemetry/', 'src/ForgeLine.Graphics/D3D12FrameCapture.cs', 'src/ForgeLine.Client/ClientApplication.cs', '.github/workflows/render-qualification.yml')


def git(repository: Path, *arguments: str) -> bytes:
    result = subprocess.run(["git", "-C", str(repository), *arguments], stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
    require(result.returncode == 0, "Git scope observation failed: " + result.stderr.decode("utf-8", "replace").strip())
    return result.stdout


def changed_paths(repository: Path, base: str, head: str) -> list[tuple[str, str]]:
    for value in (base, head):
        require(re.fullmatch(r"[0-9a-f]{40}", value) is not None, "Scope base/head must be exact commit SHAs")
        git(repository, "cat-file", "-e", value + "^{commit}")
    parts = git(repository, "diff", "--name-status", "-z", "--no-renames", base + "..." + head).decode("utf-8").split("\0")
    require(parts[-1] == "" and len(parts) % 2 == 1, "Malformed Git change list")
    return [(parts[i], parts[i+1]) for i in range(0, len(parts)-1, 2)]


def validate_serial(repository: Path, base: str, head: str, changes: list[tuple[str, str]], record: dict) -> None:
    require(type(record) is dict, "Canonical ownership record must be an object")
    proposal = record.get("ciScopeProposal")
    require(type(proposal) is dict and set(proposal) == {"kind", "package", "baseSha", "workflow"}, "Changed initiative record requires explicit serial CI proposal metadata")
    require(proposal == dict(kind="serial-foundation-proposal", package=PACKAGE, baseSha=base, workflow=WORKFLOW), "Stale base, unknown package or malformed serial CI proposal")
    require([status for status,path in changes if path == CANONICAL] in (["A"], ["M"]), "Serial proposal record must be added or modified, never deleted/retyped")
    mode = git(repository, "ls-tree", head, "--", CANONICAL).decode("utf-8").split()[0]
    require(mode == "100644", "Canonical ownership record must be an ordinary committed JSON file")
    baseline = record.get("baselineSha")
    require(type(baseline) is str and re.fullmatch(r"[0-9a-f]{40}", baseline) is not None, "Missing exact initiative baseline")
    git(repository, "merge-base", "--is-ancestor", baseline, base)
    require(git(repository, "show", base + ":" + HISTORICAL) == git(repository, "show", head + ":" + HISTORICAL), "Serial proposal cannot alter historical accepted ownership")
    spec = importlib.util.spec_from_file_location("legacy_render_ownership", Path(__file__).parents[1] / "parallelization/ValidateOwnership.py")
    legacy = importlib.util.module_from_spec(spec); spec.loader.exec_module(legacy)
    legacy.validate_manifest(json.loads(git(repository, "show", base + ":" + HISTORICAL)))
    packages = record.get("packages")
    require(type(packages) is list and all(type(entry) is dict for entry in packages), "Missing or malformed initiative packages")
    selected = [entry for entry in packages if entry.get("id") == PACKAGE]
    require(len(selected) == 1 and selected[0].get("wave") == 0 and selected[0].get("dependsOn") == [], "Serial CI selects only dependency-free wave 0")
    selected_owns = selected[0].get("owns")
    require(type(selected_owns) is list and len(selected_owns) == len(BOOTSTRAP_OWNS) and set(selected_owns) == set(BOOTSTRAP_OWNS), "Proposal footprint differs from the bounded reviewed bootstrap scope")
    initiative_ownership(record, PACKAGE, [path for _,path in changes])


def select_scope(repository: Path, base: str, head: str, report: dict, diff_file: Path) -> int:
    changes = changed_paths(repository, base, head)
    report.update(baseSha=base, headSha=head, changedPaths=[dict(status=status,path=path) for status,path in changes])
    supplied = diff_file.read_text(encoding="utf-8-sig").splitlines()
    require(supplied == [path for _,path in changes if path != "Directory.Build.props"], "Supplied path list differs from the complete observed Git diff")
    if any(path == CANONICAL for _,path in changes):
        report.update(mode="serial-foundation-proposal", proposalOnly=True, manifestPath=CANONICAL)
        raw = git(repository, "show", head + ":" + CANONICAL)
        report["manifestSha256"] = hashlib.sha256(raw).hexdigest()
        record = json.loads(raw)
        validate_serial(repository, base, head, changes, record)
        report["baselineSha"] = record["baselineSha"]
        return 0
    report.update(mode="legacy-parallel", proposalOnly=False, manifestPath=HISTORICAL,
                  manifestSha256=hashlib.sha256((repository/HISTORICAL).read_bytes()).hexdigest())
    command = [sys.executable, str(Path(__file__).parents[1] / "parallelization/ValidateOwnership.py"),
               "--manifest", str(repository / HISTORICAL), "--package", LEGACY_PACKAGE, "--diff-file", str(diff_file.resolve())]
    return subprocess.run(command, cwd=repository, check=False).returncode


def main() -> int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base",required=True);parser.add_argument("--head",required=True)
    parser.add_argument("--diff-file",type=Path,required=True);parser.add_argument("--report",type=Path,required=True)
    args=parser.parse_args()
    report=dict(schemaVersion=1,kind="render-ci-scope-check",mode="unresolved",proposalOnly=False,
                interpretation="Path/dependency proposal validation only; no merge approval, master acceptance or native admission.")
    code=2
    try:
        require(not args.report.exists(), "Scope report must use a fresh evidence path")
        code=select_scope(Path.cwd(),args.base,args.head,report,args.diff_file)
        require(code == 0, "Legacy ownership check rejected the diff")
        report["status"]="valid-proposal" if report["proposalOnly"] else "valid-legacy-scope"
    except (ValidationError,OSError,ValueError,KeyError,TypeError,IndexError) as error:
        report.update(status="rejected",reason=str(error));code=2
        print(str(error),file=sys.stderr)
    if not args.report.exists():
        args.report.parent.mkdir(parents=True,exist_ok=True)
        with args.report.open("x",encoding="utf-8") as stream: json.dump(report,stream,indent=2)
    return code
if __name__=="__main__":raise SystemExit(main())
