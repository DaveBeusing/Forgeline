import json,subprocess,tempfile,unittest,time
from pathlib import Path
from unittest.mock import patch
import capture_runner as c
from baseline_contracts import ContractError
RAW=b'GPU-aaaa-bbbb,00000000:30:00.0,GPU,596.53,41,180,11.08,0x0000000000000004\n'
class CaptureRunnerTests(unittest.TestCase):
    def fixture(self,root):
        root=Path(root);payload=root/'bin';payload.mkdir();exe=payload/'client.exe';exe.write_bytes(b'client');exe.with_suffix('.dll').write_bytes(b'assembly');sensor=root/'sensor.exe';sensor.write_bytes(b'sensor')
        runtime=root/'runtime';runtime.mkdir();(runtime/'payload').write_bytes(b'sealed')
        request=root/'request.json';request.write_text(json.dumps(dict(commit='a'*40,runtimeRoot=str(runtime))))
        return exe,sensor,request
    def test_exit_zero_without_manifest_is_retained_failure(self):self.run_mock(False)
    def test_real_launcher_path_retains_precommand_and_postcommand_sensors(self):self.run_mock(True)
    def run_mock(self,produce_manifest,hang_after_handshake=False,change_assembly=False,setup_hold=False,trace_control=False,include_provenance=False,provenance_selector=None,selector_key="captureBoundInputProvenanceVersion"):
        with tempfile.TemporaryDirectory() as root:
            exe,sensor,request=self.fixture(root);output=Path(root)/'run';state={}
            if trace_control:
                value=json.loads(request.read_bytes());value['ownerTraceControlDirectory']=str(Path(root)/'external-trace');request.write_text(json.dumps(value))
            if include_provenance:
                value=json.loads(request.read_bytes());value[selector_key]=provenance_selector;request.write_text(json.dumps(value))
            class Child:
                pid=42;returncode=None
                def __init__(self,argv,**kwargs):
                    state['child']=self;state['request']=json.loads(Path(argv[-1]).read_bytes());folder=Path(state['request']['adapterBindingDirectory']);folder.mkdir()
                    self.identity=dict(processId=42,sessionId='b'*32)
                    (folder/'checkpoint.json').write_text(json.dumps(dict(nativeWindowHandle=100,adapterIdentity=self.identity)))
                def poll(self):return self.returncode
                def terminate(self):
                    if not hang_after_handshake:self.returncode=-15
                def kill(self):self.returncode=-9
                def wait(self,timeout):
                    if self.returncode is None:raise subprocess.TimeoutExpired(["fake-child"],timeout)
                    return self.returncode
            def handshake(directory,context,dll,smi,before_publish,**kwargs):
                before_publish({})
                if hang_after_handshake:raise ContractError("simulated child failure")
                if produce_manifest:
                    folder=Path(state['request']['outputRoot']);folder.mkdir()
                    (folder/'manifest.json').write_text(json.dumps(dict(status='structural-pass',commit='a'*40,runtimeFingerprint=context['runtimeFingerprint'],adapterIdentity=state['child'].identity,captures=[{}])))
                if change_assembly:exe.with_suffix('.dll').write_bytes(b'changed managed assembly')
                state['child'].returncode=0
                return {}
            def hold(*args):
                self.assertTrue(args[2]());time.sleep(1.25);state['focused']=True
            def facts(pid=None,hwnd=None):
                return dict(status='failed' if setup_hold and pid and not state.get('focused') else 'controlled' if pid else 'unavailable',ownerPid=pid,hwnd=hwnd)
            with patch('capture_runner.run_record',return_value=(b'provider version',{})),patch('capture_runner.subprocess.run',return_value=subprocess.CompletedProcess([],0,RAW,b'')),patch('capture_runner.subprocess.Popen',Child),patch('capture_runner.power_status',return_value=dict(powerSource='ac')),patch('capture_runner.window_facts',side_effect=facts),patch('capture_runner.complete_checkpoint',side_effect=handshake),patch('diagnostic_control.hold_for_operator',side_effect=hold):
                if produce_manifest and not change_assembly:
                    result=c.run_capture(exe,request,output,sensor,'unused',diagnostic_only=setup_hold,diagnostic_setup_hold=setup_hold);self.assertEqual(result['status'],'structural-pass');self.assertFalse(result['qualified']);self.assertEqual(result['diagnosticOnly'],setup_hold or trace_control or include_provenance and provenance_selector is not None)
                else:
                    with self.assertRaises(ContractError):c.run_capture(exe,request,output,sensor,'unused')
                    record=json.loads((output/'attempt.json').read_bytes());self.assertEqual(record['status'],'failed')
                    self.assertNotIn('producer-still-alive-after-bounded-cleanup',record['cleanup'])
                    self.assertIsNotNone(record['commandCompletedAtUtc'])
                    if hang_after_handshake:self.assertIn('kill-requested',record['cleanup']);self.assertIsNotNone(record['commandCompletedAtUtc'])
            self.assertTrue((output/'sensors'/'final.json').exists())
            if produce_manifest:self.assertGreaterEqual(json.loads((output/'sensors'/'final.json').read_bytes())['rows'],2)
            if setup_hold:
                rows=[json.loads(line)['sample'] for line in (output/'sensors/observations.jsonl').read_text().splitlines()]
                self.assertTrue(any(r.get('controlPhase')=='diagnostic-setup' and r['window']['status']=='failed' and not r.get('failure') for r in rows))
    def test_direct_trace_handoff_request_is_always_marked_diagnostic(self):self.run_mock(True,trace_control=True)
    def test_startup_foreground_loss_is_observed_during_hold_not_discarded_or_qualified(self):self.run_mock(True,setup_hold=True)
    def test_version_preflight_failure_retains_attempt_before_launch(self):
        with tempfile.TemporaryDirectory() as root:
            exe,sensor,request=self.fixture(root);output=Path(root)/"run"
            with patch("capture_runner.run_record",side_effect=ContractError("version query failed")),patch("capture_runner.subprocess.Popen") as launch:
                with self.assertRaisesRegex(ContractError,"version query failed"):c.run_capture(exe,request,output,sensor,"unused")
                launch.assert_not_called()
            record=json.loads((output/"attempt.json").read_bytes());self.assertEqual(record["status"],"failed");self.assertEqual(record["phase"],"preflight")
    def test_changed_managed_client_assembly_fails_sealed_binary_check(self):self.run_mock(True,change_assembly=True)
    def test_nonterminating_child_uses_bounded_kill_fallback(self):self.run_mock(False,True)
    def test_invalid_explicit_paths_retain_preflight_attempt(self):
        with tempfile.TemporaryDirectory() as root:
            exe,sensor,request=self.fixture(root);out=Path(root)/"run"
            with self.assertRaises((ContractError,OSError)):c.run_capture(Path(root)/"missing.exe",request,out,sensor,"unused")
            self.assertEqual(json.loads((out/"attempt.json").read_bytes())["phase"],"preflight")
    def test_sensor_timeout_preserves_partial_bytes_and_actual_command(self):
        import base64
        with tempfile.TemporaryDirectory() as root:
            exe,sensor,request=self.fixture(root);out=Path(root)/"run"
            timeout=subprocess.TimeoutExpired(["sensor"],10,output=b"partial stdout",stderr=b"partial stderr")
            with patch("capture_runner.run_record",return_value=(b"version",{})),patch("capture_runner.subprocess.run",side_effect=timeout),patch("capture_runner.subprocess.Popen") as launch:
                with self.assertRaises(ContractError):c.run_capture(exe,request,out,sensor,"unused")
                launch.assert_not_called()
            row=json.loads((out/"sensors"/"observations.jsonl").read_bytes())
            self.assertEqual(base64.b64decode(row["rawStdoutBase64"]),b"partial stdout");self.assertEqual(base64.b64decode(row["rawStderrBase64"]),b"partial stderr")
            self.assertTrue(row["sample"]["command"]["timedOut"]);self.assertIsNone(row["sample"]["command"]["exitCode"])
    def test_output_inside_payload_is_rejected_without_creating_failure_artifacts(self):
        with tempfile.TemporaryDirectory() as root:
            exe,sensor,request=self.fixture(root);out=exe.parent/'would-mutate-payload'
            before=c.payload_inventory(exe.parent)
            with self.assertRaises(ContractError):c.run_capture(exe,request,out,sensor,'unused')
            self.assertFalse(out.exists());self.assertEqual(before,c.payload_inventory(exe.parent))
    def test_runtime_seal_detects_changed_bytes(self):
        with tempfile.TemporaryDirectory() as root:
            path=Path(root)/'data';path.write_bytes(b'first');before=c.runtime_fingerprint(root);path.write_bytes(b'changed');self.assertNotEqual(c.runtime_fingerprint(root),before)
    def test_every_nonnull_provenance_selector_auto_labels_actual_runner_attempt(self):
        for value in (1,2,0,False,'unsupported',{},[]):
            with self.subTest(value=value):self.run_mock(True,include_provenance=True,provenance_selector=value)
    def test_null_provenance_selector_keeps_actual_runner_nondiagnostic(self):
        self.run_mock(True,include_provenance=True,provenance_selector=None)
    def test_every_nonnull_post_frame_selector_auto_labels_actual_runner(self):
        for value in (1,0,False,2,'unsupported',{},[]):
            with self.subTest(value=value):self.run_mock(True,include_provenance=True,provenance_selector=value,selector_key='capturePostFrameResourceSnapshotVersion')
    def test_null_post_frame_selector_keeps_legacy_runner_mode(self):
        self.run_mock(True,include_provenance=True,provenance_selector=None,selector_key='capturePostFrameResourceSnapshotVersion')
if __name__=='__main__':unittest.main()
