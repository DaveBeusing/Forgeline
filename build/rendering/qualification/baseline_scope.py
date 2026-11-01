"""Separate serial baseline proposal/accepted-measurement CI route; legacy remains unchanged."""
from __future__ import annotations
import argparse,hashlib,json
from pathlib import Path
import re,subprocess,sys
import ci_scope as legacy
from qualification import ValidationError,require
RECORD="docs/rendering/render-engine-upgrade/BaselineOwnership.json"
ORIGINAL="docs/rendering/render-engine-upgrade/Ownership.json"
HISTORICAL="docs/rendering/parallelization/ParallelRenderOwnership.json"
WORKFLOW=".github/workflows/render-qualification.yml"
CONTRACT="0_CompleteBaselineAdmissionContracts"
MEASUREMENT="1_QualifyRepeatableBaseline"
DIAGNOSTICS="0_CompleteBaselineCaptureDiagnostics"
STABILITY='2_ResolveSceneLinearRepeatability'
STABILITY_INSPECTOR_OWNS=('src/ForgeLine.Client/VisualReferenceCapture.cs', 'src/ForgeLine.Client/ClientRenderHost.cs', 'src/ForgeLine.Client/RenderTelemetry/ClientRenderHost.ReferenceFixture.cs', 'src/ForgeLine.Client/RenderTelemetry/ReferenceWorldInputDiagnostics.cs', 'tests/ForgeLine.Client.Tests/RenderQualification/ReferenceWorldInputDiagnosticsTests.cs', 'build/rendering/render-engine-upgrade/world_input_diagnostics.py', 'build/rendering/render-engine-upgrade/test_world_input_diagnostics.py', 'build/rendering/render-engine-upgrade/stage_diagnostics.py', 'build/rendering/render-engine-upgrade/test_stage_diagnostics.py', 'build/rendering/qualification/baseline_scope.py', 'build/rendering/qualification/test_baseline_scope.py', 'docs/rendering/render-engine-upgrade/BaselineOwnership.json', 'docs/rendering/render-engine-upgrade/MeasurementProtocol.md')
STABILITY_OWNS=STABILITY_INSPECTOR_OWNS+('src/ForgeLine.Graphics/D3D12GraphicsCommandContext.cs','src/ForgeLine.Graphics/D3D12FrameCapture.cs','src/ForgeLine.Graphics/RenderTelemetry/GraphicsStageCapture.cs','src/ForgeLine.Graphics/RenderTelemetry/GraphicsWorldBindingJournal.cs','src/ForgeLine.Client/RenderTelemetry/ReferenceStageDiagnostics.cs','tests/ForgeLine.Graphics.Tests/RenderTelemetry/GraphicsWorldBindingJournalTests.cs','tests/ForgeLine.Graphics.Tests/RenderTelemetry/StageCaptureNativeTests.cs','build/rendering/render-engine-upgrade/world_binding_journal.py','build/rendering/render-engine-upgrade/test_world_binding_journal.py')
WORKLOAD='1_ObserveBoundedAllocationWorkload'
WORKLOAD_OWNS=('src/ForgeLine.Client/ClientSimulationHost.cs', 'src/ForgeLine.Client/ClientSimulationTelemetry.cs', 'src/ForgeLine.Client/ClientSimulationControls.cs', 'src/ForgeLine.Client/ClientRenderHost.cs', 'src/ForgeLine.Client/RenderTelemetry/', 'src/ForgeLine.Platform.Windows/WindowsPlatform.cs', 'src/ForgeLine.Platform.Windows/WindowsWindow.cs', 'src/ForgeLine.Platform.Windows/PlatformWorkloadTelemetry.cs', 'src/ForgeLine.Client/WaveOutUiAudio.cs', 'src/ForgeLine.Client/WaveOutWorldAudio.cs', 'src/ForgeLine.Client/WorldAudioPlayback.cs', 'tests/ForgeLine.Client.Tests/RenderQualification/', 'tests/ForgeLine.Platform.Windows.Tests/', 'build/rendering/render-engine-upgrade/', 'docs/rendering/render-engine-upgrade/Admission.md', 'docs/rendering/render-engine-upgrade/MeasurementProtocol.md', 'src/ForgeLine.Client/VisualReferenceCapture.cs', 'build/rendering/qualification/baseline_scope.py', 'build/rendering/qualification/test_baseline_scope.py', 'docs/rendering/render-engine-upgrade/BaselineOwnership.json', 'src/ForgeLine.Client/ClientApplication.cs', 'docs/rendering/render-engine-upgrade/Telemetry.md', 'docs/Development.md')
DIAGNOSTICS_OWNS=('src/ForgeLine.Graphics/IGraphicsDevice.cs', 'src/ForgeLine.Graphics/D3D12FrameCapture.cs', 'src/ForgeLine.Graphics/D3D12SceneComposition.cs', 'src/ForgeLine.Graphics/RenderTelemetry/', 'src/ForgeLine.Client/Program.cs', 'src/ForgeLine.Client/ClientRenderHost.cs', 'src/ForgeLine.Client/VisualReferenceCapture.cs', 'src/ForgeLine.Client/ReferenceCaptureManifest.cs', 'src/ForgeLine.Client/RenderTelemetry/', 'tests/ForgeLine.Graphics.Tests/RenderTelemetry/', 'tests/ForgeLine.Client.Tests/RenderQualification/', 'build/rendering/render-engine-upgrade/', 'build/rendering/qualification/', 'docs/rendering/render-engine-upgrade/Admission.md', 'docs/rendering/render-engine-upgrade/Baseline.md', 'docs/rendering/render-engine-upgrade/MeasurementProtocol.md', 'docs/rendering/render-engine-upgrade/BaselineOwnership.json', 'docs/rendering/render-engine-upgrade/evidence/qualification/', 'docs/DocumentationIndex.md', '.github/workflows/render-qualification.yml')
# Reviewed hard pin: the proposal cannot authorize its own wider scope.
CONTRACT_OWNS=('src/ForgeLine.Client/Program.cs', 'src/ForgeLine.Client/ClientRenderHost.cs', 'src/ForgeLine.Client/ClientApplication.cs', 'src/ForgeLine.Client/VisualReferenceCapture.cs', 'src/ForgeLine.Client/ReferencePerformanceSamples.cs', 'src/ForgeLine.Client/ReferenceCaptureManifest.cs', 'src/ForgeLine.Client/RenderTelemetry/', 'src/ForgeLine.Graphics/D3D12GraphicsDevice.cs', 'src/ForgeLine.Graphics/D3D12FrameCapture.cs', 'src/ForgeLine.Graphics/RenderTelemetry/', 'tests/ForgeLine.Client.Tests/ReferencePerformanceSamplesTests.cs', 'tests/ForgeLine.Client.Tests/RenderQualification/', 'tests/ForgeLine.Graphics.Tests/RenderTelemetry/', 'tests/ForgeLine.Presentation.Tests/BattlefieldStatusNativeTests.cs', 'tests/ForgeLine.Presentation.Tests/BattlefieldStatusOverlayTests.cs', 'build/rendering/qualification/', 'build/rendering/render-engine-upgrade/', 'docs/rendering/render-engine-upgrade/BaselineOwnership.json', 'docs/rendering/render-engine-upgrade/Admission.md', 'docs/rendering/render-engine-upgrade/Telemetry.md', 'docs/rendering/render-engine-upgrade/Baseline.md', 'docs/rendering/render-engine-upgrade/MeasurementProtocol.md', 'docs/rendering/render-engine-upgrade/evidence/admission/', 'docs/rendering/render-engine-upgrade/evidence/baseline/', '.github/workflows/render-qualification.yml', 'Directory.Packages.props', 'docs/DocumentationIndex.md', 'src/ForgeLine.Presentation/SimpleInstanceRenderer.ContentEvidence.cs', 'src/ForgeLine.Presentation/SimpleInstanceRenderer.cs', 'tests/ForgeLine.Presentation.Tests/ContentEvidence/', 'tests/ForgeLine.Presentation.Tests/TerrainResidency/TerrainLifetimeTests.cs', 'src/ForgeLine.Game/MatchRuntime.cs', 'tests/ForgeLine.Game.Tests/DeveloperTools/DeveloperPracticeInitializationTests.cs')
MEASUREMENT_OWNS=('build/rendering/render-engine-upgrade/', 'docs/rendering/render-engine-upgrade/Baseline.md', 'docs/rendering/render-engine-upgrade/MeasurementProtocol.md', 'docs/rendering/render-engine-upgrade/evidence/baseline/')

def owned(path,owns):return any(path==entry or entry.endswith('/') and path.startswith(entry) for entry in owns)
def blob(repo,sha,path):return legacy.git(repo,"show",sha+":"+path)
def exists(repo,sha,path):return bool(legacy.git(repo,"ls-tree",sha,"--",path).strip())
def validate_record(record):
    require(type(record) is dict and type(record.get("schemaVersion")) is int and record["schemaVersion"]==1 and record.get("kind")=="render-baseline-ownership","Malformed baseline ownership record")
    require(record.get("serializedVersionException")=="Directory.Build.props","Unknown shared ownership exception")
    require(set(record)=={"schemaVersion","kind","baselineSha","serializedVersionException","packages","ciScopeProposal"},"Unknown baseline record fields")
    packages=record.get("packages");require(type(packages) is list and len(packages) in (1,2),"Unknown/mixed baseline packages")
    workload=type(packages[0]) is dict and packages[0].get("id")==WORKLOAD
    stability=type(packages[0]) is dict and packages[0].get("id")==STABILITY
    require(len(packages)==(1 if workload or stability else 2),"Unknown/mixed baseline package rows")
    diagnostics=packages[0].get("id")==DIAGNOSTICS if type(packages[0]) is dict else False
    # Both exact reviewed footprints remain literal; malformed/mixed proposals cannot widen either.
    supplied=packages[0].get("owns") if stability else None
    stability_paths=STABILITY_INSPECTOR_OWNS if type(supplied) is list and len(supplied)==len(STABILITY_INSPECTOR_OWNS) and set(supplied)==set(STABILITY_INSPECTOR_OWNS) else STABILITY_OWNS
    expected=[(STABILITY,2,[WORKLOAD],stability_paths)] if stability else [(WORKLOAD,1,[DIAGNOSTICS],WORKLOAD_OWNS)] if workload else [(DIAGNOSTICS,0,[],DIAGNOSTICS_OWNS),(MEASUREMENT,1,[DIAGNOSTICS],MEASUREMENT_OWNS)] if diagnostics else [(CONTRACT,0,[],CONTRACT_OWNS),(MEASUREMENT,1,[CONTRACT],MEASUREMENT_OWNS)]
    for package,(name,wave,deps,paths) in zip(packages,expected):
        require(type(package) is dict and set(package)=={"id","wave","dependsOn","owns"},"Malformed baseline package")
        require(package["id"]==name and type(package["wave"]) is int and package["wave"]==wave and package["dependsOn"]==deps,"Unknown package/wave/dependency")
        require(type(package["owns"]) is list and len(package["owns"])==len(paths) and set(package["owns"])==set(paths),"Baseline footprint differs from reviewed hard pin")
    baseline=record.get("baselineSha");require(type(baseline) is str and re.fullmatch('[0-9a-f]{40}',baseline),"Missing exact baseline")

def select_scope(repo,base,head,report,diff_file):
    changes=legacy.changed_paths(repo,base,head);paths=[path for _,path in changes]
    supplied=diff_file.read_text(encoding="utf-8-sig").splitlines()
    require(supplied==[p for p in paths if p!="Directory.Build.props"],"Supplied paths differ from complete Git diff")
    changed_record=RECORD in paths
    measurement_touch=any(owned(p,MEASUREMENT_OWNS) for p in paths)
    if not changed_record and not measurement_touch:return legacy.select_scope(repo,base,head,report,diff_file)
    require(changed_record or exists(repo,base,RECORD),"Measurement changes lack an ownership contract committed in actual event base")
    legacy.git(repo,"merge-base","--is-ancestor",base,head)
    report.update(mode="serial-baseline-contract-proposal" if changed_record else "accepted-baseline-measurement-scope",proposalOnly=changed_record)
    source=head if changed_record else base
    raw=blob(repo,source,RECORD)
    def unique(pairs):
        result={}
        for key,value in pairs:
            require(key not in result,"Duplicate ownership JSON key");result[key]=value
        return result
    record=json.loads(raw,object_pairs_hook=unique);validate_record(record)
    require(legacy.git(repo,"ls-tree",source,"--",RECORD).decode().split()[0]=="100644","Baseline record must be an ordinary committed file")
    legacy.git(repo,"merge-base","--is-ancestor",record["baselineSha"],base)
    for historical in (ORIGINAL,HISTORICAL):require(blob(repo,base,historical)==blob(repo,head,historical),"Baseline route cannot alter historical ownership")
    if changed_record:
        require([status for status,path in changes if path==RECORD] in (["A"],["M"]),"Changed baseline record must be an explicit ordinary proposal")
        selected=record["packages"][0]["id"]
        proposal=dict(kind="serial-baseline-contract-proposal",package=selected,baseSha=base,workflow=WORKFLOW)
        require(record.get("ciScopeProposal")==proposal,"Stale base, unknown package or malformed proposal; no fallback")
        if selected in (WORKLOAD,STABILITY):require(record["baselineSha"]==base,"Workload proposal baseline differs from actual event base")
        allowed=tuple(record["packages"][0]["owns"]) if selected==STABILITY else {DIAGNOSTICS:DIAGNOSTICS_OWNS,CONTRACT:CONTRACT_OWNS,WORKLOAD:WORKLOAD_OWNS}[selected];mode="serial-baseline-contract-proposal"
    else:
        require(blob(repo,head,RECORD)==raw,"Accepted baseline ownership bytes changed without proposal")
        allowed=MEASUREMENT_OWNS;mode="accepted-baseline-measurement-scope"
    require(all(p=="Directory.Build.props" or owned(p,allowed) for p in paths),"Unowned production or mixed package change in baseline route")
    for status,path in changes:
        if status!="D":require(legacy.git(repo,"ls-tree",head,"--",path).decode().split()[0]=="100644","Baseline changes must be ordinary files")
    if "Directory.Packages.props" in paths:
        before=blob(repo,base,"Directory.Packages.props");after=blob(repo,head,"Directory.Packages.props")
        addition=b'    <PackageVersion Include="Microsoft.Diagnostics.Tracing.TraceEvent" Version="4.0.0" />\n'
        require(after.count(addition)==1 and after.replace(addition,b"")==before,"Only the isolated pinned TraceEvent dependency addition is owned")
    report.update(baseSha=base,headSha=head,mode=mode,proposalOnly=changed_record,manifestPath=RECORD,manifestSource=source,
                  manifestSha256=hashlib.sha256(raw).hexdigest(),changedPaths=[dict(status=s,path=p) for s,p in changes],
                  interpretation="CI scope only; no merge approval, native admission or accepted performance baseline")
    return 0

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ('base','head','diff-file','report'):parser.add_argument('--'+name,required=True)
    args=parser.parse_args();report=dict(schemaVersion=1,kind='baseline-ci-scope',status='rejected');code=2
    try:
        require(not Path(args.report).exists(),"Scope report must use a fresh path")
        code=select_scope(Path.cwd(),args.base,args.head,report,Path(args.diff_file));require(code==0,"Legacy scope rejected diff")
        report['status']='valid-proposal' if report.get('proposalOnly') else 'valid-scope'
    except (ValidationError,OSError,ValueError,KeyError,TypeError,IndexError) as error:report['reason']=str(error);print(str(error),file=sys.stderr);code=2
    path=Path(args.report)
    if not path.exists():
        path.parent.mkdir(parents=True,exist_ok=True)
        with path.open('x',encoding='utf-8') as stream:json.dump(report,stream,indent=2)
    return code
if __name__=='__main__':raise SystemExit(main())
