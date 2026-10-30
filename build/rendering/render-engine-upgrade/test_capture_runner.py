import json,subprocess,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import capture_runner as c
from baseline_contracts import ContractError
RAW=b'GPU-aaaa-bbbb,00000000:30:00.0,GPU,596.53,41,180,11.08,0x0000000000000004\n'
class CaptureRunnerTests(unittest.TestCase):
    def fixture(self,root):
        root=Path(root);exe=root/'client.exe';exe.write_bytes(b'client');exe.with_suffix('.dll').write_bytes(b'assembly');sensor=root/'sensor.exe';sensor.write_bytes(b'sensor')
        runtime=root/'runtime';runtime.mkdir();(runtime/'payload').write_bytes(b'sealed')
        request=root/'request.json';request.write_text(json.dumps(dict(commit='a'*40,runtimeRoot=str(runtime))))
        return exe,sensor,request
    def test_exit_zero_without_manifest_is_retained_failure(self):self.run_mock(False)
    def test_real_launcher_path_retains_precommand_and_postcommand_sensors(self):self.run_mock(True)
    def run_mock(self,produce_manifest,hang_after_handshake=False,change_assembly=False):
        with tempfile.TemporaryDirectory() as root:
            exe,sensor,request=self.fixture(root);output=Path(root)/'run';state={}
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
            def handshake(directory,context,dll,smi,before_publish):
                before_publish({})
                if hang_after_handshake:raise ContractError("simulated child failure")
                if produce_manifest:
                    folder=Path(state['request']['outputRoot']);folder.mkdir()
                    (folder/'manifest.json').write_text(json.dumps(dict(status='structural-pass',commit='a'*40,runtimeFingerprint=context['runtimeFingerprint'],adapterIdentity=state['child'].identity,captures=[{}])))
                if change_assembly:exe.with_suffix('.dll').write_bytes(b'changed managed assembly')
                state['child'].returncode=0
                return {}
            with patch('capture_runner.run_record',return_value=(b'provider version',{})),patch('capture_runner.subprocess.run',return_value=subprocess.CompletedProcess([],0,RAW,b'')),patch('capture_runner.subprocess.Popen',Child),patch('capture_runner.power_status',return_value=dict(powerSource='ac')),patch('capture_runner.window_facts',side_effect=lambda pid=None,hwnd=None:dict(status='controlled' if pid else 'unavailable')),patch('capture_runner.complete_checkpoint',side_effect=handshake):
                if produce_manifest and not change_assembly:
                    result=c.run_capture(exe,request,output,sensor,'unused');self.assertEqual(result['status'],'structural-pass');self.assertFalse(result['qualified'])
                else:
                    with self.assertRaises(ContractError):c.run_capture(exe,request,output,sensor,'unused')
                    record=json.loads((output/'attempt.json').read_bytes());self.assertEqual(record['status'],'failed')
                    self.assertNotIn('producer-still-alive-after-bounded-cleanup',record['cleanup'])
                    self.assertIsNotNone(record['commandCompletedAtUtc'])
                    if hang_after_handshake:self.assertIn('kill-requested',record['cleanup']);self.assertIsNotNone(record['commandCompletedAtUtc'])
            self.assertTrue((output/'sensors'/'final.json').exists())
            if produce_manifest:self.assertGreaterEqual(json.loads((output/'sensors'/'final.json').read_bytes())['rows'],2)
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
    def test_runtime_seal_detects_changed_bytes(self):
        with tempfile.TemporaryDirectory() as root:
            path=Path(root)/'data';path.write_bytes(b'first');before=c.runtime_fingerprint(root);path.write_bytes(b'changed');self.assertNotEqual(c.runtime_fingerprint(root),before)
if __name__=='__main__':unittest.main()
