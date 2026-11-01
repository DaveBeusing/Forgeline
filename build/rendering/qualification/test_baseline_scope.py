"""Real Git lifecycle tests: proposal, accepted event-base record and preserved legacy route."""
import copy,json,subprocess,sys,tempfile,unittest
from pathlib import Path
import baseline_scope as scope
ROOT=Path(__file__).resolve().parents[3]
class Repository:
    def __init__(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)/'repo';self.root.mkdir();self.run('init','-q')
        for path in (scope.ORIGINAL,scope.HISTORICAL):self.write(path,(ROOT/path).read_text(encoding='utf-8'))
        self.write('src/ForgeLine.Game/Unowned.cs','original');self.commit('base');self.base=self.head()
        self.record=json.loads((ROOT/scope.RECORD).read_bytes());self.record.update(baselineSha=self.base,packages=[dict(id=scope.DIAGNOSTICS,wave=0,dependsOn=[],owns=list(scope.DIAGNOSTICS_OWNS)),dict(id=scope.MEASUREMENT,wave=1,dependsOn=[scope.DIAGNOSTICS],owns=list(scope.MEASUREMENT_OWNS))]);self.record['ciScopeProposal'].update(baseSha=self.base,package=scope.DIAGNOSTICS);self.checks=0
    def run(self,*args):return subprocess.check_output(['git','-C',str(self.root),*args],stderr=subprocess.STDOUT,text=True)
    def write(self,path,text):
        target=self.root/path;target.parent.mkdir(parents=True,exist_ok=True);target.write_text(text,encoding='utf-8')
    def commit(self,message):self.run('add','.');self.run('-c','user.name=Fixture','-c','user.email=fixture@example.invalid','commit','-qm',message)
    def head(self):return self.run('rev-parse','HEAD').strip()
    def proposal(self,record=None,extra=None):
        actual=record if record is not None else self.record
        self.write(scope.RECORD,json.dumps(actual));self.write('src/ForgeLine.Client/RenderTelemetry/ReferenceWorldInputDiagnostics.cs' if actual.get('ciScopeProposal',{}).get('package')==scope.STABILITY else 'src/ForgeLine.Client/RenderTelemetry/Example.cs','owned')
        for path,value in (extra or {}).items():self.write(path,value)
        self.commit('proposal');return self.head()
    def check(self,head=None,base=None):
        head=head or self.head();base=base or self.base;self.checks+=1
        paths=scope.legacy.changed_paths(self.root,base,head);diff=Path(self.temp.name)/f'diff{self.checks}.txt';diff.write_text('\n'.join(p for _,p in paths if p!='Directory.Build.props')+'\n')
        report=Path(self.temp.name)/f'report{self.checks}.json'
        result=subprocess.run([sys.executable,str(Path(scope.__file__)),'--base',base,'--head',head,'--diff-file',str(diff),'--report',str(report)],cwd=self.root,capture_output=True,text=True)
        return result,json.loads(report.read_bytes())
class BaselineScopeTests(unittest.TestCase):
    def setUp(self):self.repo=Repository();self.addCleanup(self.repo.temp.cleanup)
    def test_initial_bounded_proposal(self):
        result,report=self.repo.check(self.repo.proposal());self.assertEqual(result.returncode,0,result.stderr);self.assertTrue(report['proposalOnly']);self.assertEqual(report['manifestSource'],self.repo.head())
    def test_diagnostics_exact_new_graphics_and_evidence_paths(self):
        result,report=self.repo.check(self.repo.proposal(extra={'src/ForgeLine.Graphics/IGraphicsDevice.cs':'additive','src/ForgeLine.Graphics/D3D12SceneComposition.cs':'hooks','docs/rendering/render-engine-upgrade/evidence/qualification/example.json':'{}','Directory.Build.props':'version'}))
        self.assertEqual(result.returncode,0,result.stderr);self.assertTrue(report['proposalOnly'])
    def test_historical_foundation_variant_still_supported(self):
        record=copy.deepcopy(self.repo.record)
        record['packages'][0]=dict(id=scope.CONTRACT,wave=0,dependsOn=[],owns=list(scope.CONTRACT_OWNS))
        record['packages'][1]['dependsOn']=[scope.CONTRACT];record['ciScopeProposal']['package']=scope.CONTRACT
        result,_=self.repo.check(self.repo.proposal(record));self.assertEqual(result.returncode,0,result.stderr)
    def test_diagnostics_cannot_take_old_foundation_only_path(self):
        result,_=self.repo.check(self.repo.proposal(extra={'src/ForgeLine.Game/MatchRuntime.cs':'unowned'}));self.assertNotEqual(result.returncode,0)
    def test_stale_base_no_fallback(self):
        record=copy.deepcopy(self.repo.record);record['ciScopeProposal']['baseSha']='a'*40
        result,report=self.repo.check(self.repo.proposal(record));self.assertNotEqual(result.returncode,0);self.assertEqual(report['mode'],'serial-baseline-contract-proposal')
    def test_broad_self_ownership_rejected(self):
        record=copy.deepcopy(self.repo.record);record['packages'][0]['owns']=['src/']
        result,_=self.repo.check(self.repo.proposal(record));self.assertNotEqual(result.returncode,0)
    def test_unknown_package_or_malformed_schema_rejected(self):
        record=copy.deepcopy(self.repo.record);record['packages'][0]['id']='Unknown';record['schemaVersion']=True
        result,_=self.repo.check(self.repo.proposal(record));self.assertNotEqual(result.returncode,0)
    def test_unowned_production_mixed_package_rejected(self):
        result,_=self.repo.check(self.repo.proposal(extra={'src/ForgeLine.Game/Unowned.cs':'changed'}));self.assertNotEqual(result.returncode,0)
    def test_historical_both_records_immutable(self):
        result,_=self.repo.check(self.repo.proposal(extra={scope.ORIGINAL:'{}',scope.HISTORICAL:'{}'}));self.assertNotEqual(result.returncode,0)
    def test_deleted_unowned_source_cannot_hide_as_rename(self):
        self.repo.proposal();self.repo.run('mv','src/ForgeLine.Game/Unowned.cs','src/ForgeLine.Client/RenderTelemetry/Renamed.cs');self.repo.commit('rename')
        result,_=self.repo.check();self.assertNotEqual(result.returncode,0)
    def test_accepted_record_from_actual_event_base_admits_measurement_only(self):
        base=self.repo.proposal();self.repo.write('docs/rendering/render-engine-upgrade/Baseline.md','measurements');self.repo.commit('measurement')
        result,report=self.repo.check(base=base);self.assertEqual(result.returncode,0,result.stderr);self.assertFalse(report['proposalOnly']);self.assertEqual(report['manifestSource'],base)
    def test_accepted_measurements_cannot_mix_unowned_production(self):
        base=self.repo.proposal();self.repo.write('docs/rendering/render-engine-upgrade/Baseline.md','measurements');self.repo.write('src/ForgeLine.Client/ClientRenderHost.cs','changed without proposal');self.repo.commit('mixed')
        result,_=self.repo.check(base=base);self.assertNotEqual(result.returncode,0)
    def test_measurements_without_record_in_actual_event_base_rejected(self):
        self.repo.write('docs/rendering/render-engine-upgrade/Baseline.md','measurements');self.repo.commit('no contract')
        result,_=self.repo.check();self.assertNotEqual(result.returncode,0)
    def test_accepted_changed_record_requires_new_proposal_base(self):
        base=self.repo.proposal();record=copy.deepcopy(self.repo.record);record['ciScopeProposal']['baseSha']=self.repo.base
        self.repo.write(scope.RECORD,json.dumps(record)+'\n');self.repo.commit('changed record')
        result,_=self.repo.check(base=base);self.assertNotEqual(result.returncode,0)
    def test_malformed_proposal_never_uses_legacy(self):
        record=copy.deepcopy(self.repo.record);del record['ciScopeProposal']
        result,report=self.repo.check(self.repo.proposal(record));self.assertNotEqual(result.returncode,0);self.assertEqual(report['mode'],'serial-baseline-contract-proposal')
    def test_unrelated_legacy_contract_route_unchanged(self):
        self.repo.write('docs/rendering/qualification/Example.md','legacy');self.repo.commit('legacy')
        result,report=self.repo.check();self.assertEqual(result.returncode,0,result.stderr);self.assertEqual(report['mode'],'legacy-parallel')
class WorkloadScopeTests(unittest.TestCase):
    def setUp(self):
        self.repo=Repository();self.addCleanup(self.repo.temp.cleanup)
        self.repo.record.update(packages=[dict(id=scope.WORKLOAD,wave=1,dependsOn=[scope.DIAGNOSTICS],owns=list(scope.WORKLOAD_OWNS))])
        self.repo.record['ciScopeProposal']['package']=scope.WORKLOAD
    def test_one_row_exact_workload_proposal(self):
        result,report=self.repo.check(self.repo.proposal(extra={'src/ForgeLine.Client/ClientSimulationHost.cs':'facts','src/ForgeLine.Platform.Windows/PlatformWorkloadTelemetry.cs':'facts','docs/Development.md':'wording','Directory.Build.props':'version'}))
        self.assertEqual(result.returncode,0,result.stderr);self.assertTrue(report['proposalOnly'])
    def test_wrong_dependency_extra_row_and_foreign_id_reject(self):
        for mutate in [lambda r:r['packages'][0].update(dependsOn=[]),lambda r:r['packages'].append(copy.deepcopy(r['packages'][0])),lambda r:r['packages'][0].update(id='Foreign')]:
            record=copy.deepcopy(self.repo.record);mutate(record)
            with self.assertRaises(scope.ValidationError):scope.validate_record(record)
    def test_workload_cannot_take_graphics_or_self_authorize_broad_scope(self):
        result,_=self.repo.check(self.repo.proposal(extra={'src/ForgeLine.Graphics/D3D12FrameCapture.cs':'foreign'}));self.assertNotEqual(result.returncode,0)
        record=copy.deepcopy(self.repo.record);record['packages'][0]['owns'].append('src/')
        with self.assertRaises(scope.ValidationError):scope.validate_record(record)
    def test_wrong_workload_baseline_rejects_even_when_ancestor(self):
        self.repo.write('README.md','next base');self.repo.commit('next');eventbase=self.repo.head()
        record=copy.deepcopy(self.repo.record);record['ciScopeProposal']['baseSha']=eventbase
        result,_=self.repo.check(self.repo.proposal(record),base=eventbase);self.assertNotEqual(result.returncode,0)
class StabilityScopeTests(unittest.TestCase):
    def setUp(self):
        self.repo=Repository();self.addCleanup(self.repo.temp.cleanup)
        self.repo.record.update(packages=[dict(id=scope.STABILITY,wave=2,dependsOn=[scope.WORKLOAD],owns=list(scope.STABILITY_OWNS))])
        self.repo.record['ciScopeProposal']['package']=scope.STABILITY
    def test_exact_stability_proposal_accepts_only_declared_files(self):
        self.repo.write('src/ForgeLine.Client/RenderTelemetry/Example.cs','base unrelated helper');self.repo.commit('base helper');self.repo.base=self.repo.head();self.repo.record['baselineSha']=self.repo.base;self.repo.record['ciScopeProposal']['baseSha']=self.repo.base
        self.repo.write(scope.RECORD,json.dumps(self.repo.record));self.repo.write('src/ForgeLine.Client/RenderTelemetry/ReferenceWorldInputDiagnostics.cs','new inspector');self.repo.write('Directory.Build.props','version');self.repo.commit('stability')
        result,report=self.repo.check();self.assertEqual(result.returncode,0,result.stderr);self.assertTrue(report['proposalOnly'])
    def test_stability_cannot_take_foreign_renderer_or_host_capture_path(self):
        result,_=self.repo.check(self.repo.proposal(extra={'src/ForgeLine.Client/RenderTelemetry/ReferenceFixtureHostCapture.cs':'foreign'}));self.assertNotEqual(result.returncode,0)
    def test_stability_wrong_dependency_unknown_row_and_broadpath_reject(self):
        for mutate in [lambda r:r['packages'][0].update(dependsOn=[scope.DIAGNOSTICS]),lambda r:r['packages'].append(copy.deepcopy(r['packages'][0])),lambda r:r['packages'][0]['owns'].append('src/ForgeLine.Client/RenderTelemetry/')]:
            record=copy.deepcopy(self.repo.record);mutate(record)
            with self.assertRaises(scope.ValidationError):scope.validate_record(record)
    def test_stability_stale_base_never_falls_back(self):
        self.repo.record['ciScopeProposal']['baseSha']='a'*40
        result,report=self.repo.check(self.repo.proposal());self.assertNotEqual(result.returncode,0);self.assertEqual(report['mode'],'serial-baseline-contract-proposal')
    def test_initial_inspector_literal_variant_remains_supported(self):
        record=copy.deepcopy(self.repo.record);record['packages'][0]['owns']=list(scope.STABILITY_INSPECTOR_OWNS)
        result,_=self.repo.check(self.repo.proposal(record));self.assertEqual(result.returncode,0,result.stderr)
    def test_journal_amendment_exact_new_paths_are_authorized(self):
        result,_=self.repo.check(self.repo.proposal(extra={'src/ForgeLine.Graphics/D3D12GraphicsCommandContext.cs':'journal','src/ForgeLine.Graphics/RenderTelemetry/GraphicsWorldBindingJournal.cs':'bounded','src/ForgeLine.Client/RenderTelemetry/ReferenceStageDiagnostics.cs':'export'}));self.assertEqual(result.returncode,0,result.stderr)
    def test_old_inspector_cannot_borrow_journal_paths(self):
        record=copy.deepcopy(self.repo.record);record['packages'][0]['owns']=list(scope.STABILITY_INSPECTOR_OWNS)
        result,_=self.repo.check(self.repo.proposal(record,extra={'src/ForgeLine.Graphics/D3D12GraphicsCommandContext.cs':'foreign'}));self.assertNotEqual(result.returncode,0)
    def test_journal_cannot_take_buffer_hook_or_partial_mixed_scope(self):
        result,_=self.repo.check(self.repo.proposal(extra={'src/ForgeLine.Graphics/D3D12GraphicsBuffer.cs':'forbidden'}));self.assertNotEqual(result.returncode,0)
        record=copy.deepcopy(self.repo.record);record['packages'][0]['owns'].pop()
        with self.assertRaises(scope.ValidationError):scope.validate_record(record)
if __name__=='__main__':unittest.main()
