import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from datetime import datetime,timezone
import baseline_contracts as c
import provider as p
import baseline
ROW=b'GPU-aaaa-bbbb,00000000:30:00.0,GPU,596.53,41,180,11.08,0x0000000000000004\n'
class Contracts(unittest.TestCase):
    def test_provider_real_shape(self):
        row=p.parse_inventory(ROW)[0];self.assertEqual(row['eventReasons']['reasons'],['software-power-cap']);self.assertEqual(row['powerWatts'],11.08)
    def test_provider_rejects_invalid(self):
        for raw in (b'',ROW+ROW,ROW.replace(b'41,',b'N/A,'),ROW.replace(b'11.08',b'NaN'),ROW.replace(b'0x0000000000000004',b'N/A'),ROW.replace(b'00000000:30:00.0',b'30:0')):
            with self.subTest(raw=raw),self.assertRaises(c.ContractError):p.parse_inventory(raw)
    def test_unknown_events_not_clear(self):self.assertIsNone(c.event_reasons('0x40000000')['throttling'])
    def test_no_battery_does_not_imply_ac(self):self.assertEqual(p.decode_power({'ACLineStatus':255,'BatteryFlag':128})['powerSource'],'unknown')
    def test_mapping_does_not_convert_driver_versions(self):
        native={'identity':{'device':{'adapterName':'GPU'},'driver':'32.0.15.9653'}}
        result=c.candidate_mapping(native,p.parse_inventory(ROW));self.assertFalse(result['exactPhysicalMappingAvailable']);self.assertEqual(result['status'],'unique-inventory-candidate');self.assertIn('unavailable',result['driverAssociation'])
    def test_requested_uuid_cannot_select_other_device(self):
        rows=p.parse_inventory(ROW)+[dict(p.parse_inventory(ROW)[0],uuid='GPU-cccc',pciBusId='00000000:31:00.0',name='Other')]
        with self.assertRaises(c.ContractError):c.candidate_mapping({'identity':{'device':{'adapterName':'GPU'}}},rows,'GPU-cccc')
    def test_same_name_is_ambiguous(self):
        rows=p.parse_inventory(ROW)+[dict(p.parse_inventory(ROW)[0],uuid='GPU-cccc',pciBusId='00000000:31:00.0')]
        self.assertEqual(c.candidate_mapping({'identity':{'device':{'adapterName':'GPU'}}},rows)['status'],'ambiguous-inventory-candidates')
    def test_no_accepted_publication(self):
        with self.assertRaises(c.ReadinessBlocked):c.qualified_wave0_snapshot(hardwareIdentitySha256='a'*64)
        with tempfile.TemporaryDirectory() as root:
            with self.assertRaises(c.ContractError):c.atomic_diagnostic_snapshot(Path(root)/'bad.json',{'kind':'physical-gpu-observations','exactPhysicalMappingAvailable':False})
            self.assertFalse((Path(root)/'bad.json').exists())
    def test_capacity_never_discards(self):
        series=c.DiagnosticSeries(1);series.add({'observedAtUtc':'2026-10-30T00:00:00Z'})
        with self.assertRaises(c.ContractError):series.add({'observedAtUtc':'2026-10-30T00:00:01Z'})
        self.assertTrue(series.overflowed);self.assertEqual(len(series.samples),1)
    def test_duplicate_and_nonfinite_json(self):
        for raw in (b'{"a":1,"a":2}',b'{"a":NaN}'):
            with self.assertRaises(c.ContractError):c.parse_json_bytes(raw)
    def test_protocol_is_predeclared(self):
        plan=c.freeze_plan('a'*40,'b'*40,datetime.now(timezone.utc));self.assertEqual(len(plan['pairs']),5);self.assertEqual(plan['warmSamples'],512);self.assertEqual(plan['warmups'],64);self.assertEqual(plan['canonicalMatrix']['rasterDpi'],96);self.assertEqual(plan['regressionPercent'],15);self.assertFalse(plan['wave2Ready'])
    def test_future_review_open(self):
        with tempfile.TemporaryDirectory() as root:
            file=Path(root)/'policy';file.write_text(json.dumps(dict(reviewer='R',rationale='test',reviewedAtUtc='2099-01-01T00:00:00Z')))
            result=c.policy_readiness({'hardware':file},datetime.now(timezone.utc));self.assertEqual(result[0]['status'],'open')
    def test_policy_snapshot_hash_same_bytes(self):
        with tempfile.TemporaryDirectory() as root:
            file=Path(root)/'policy';file.write_text(json.dumps(dict(reviewer='R',rationale='test',reviewedAtUtc='2020-01-01T00:00:00Z')))
            result=c.policy_readiness({'hardware':file},datetime.now(timezone.utc))[0];self.assertEqual(result['sha256'],c.sha256_bytes(__import__('base64').b64decode(result['retainedBase64'])))
    def test_bom_policy_retains_original_bytes(self):
        with tempfile.TemporaryDirectory() as root:
            file=Path(root)/"policy";raw=b"\xef\xbb\xbf"+json.dumps(dict(reviewer="R",rationale="test",reviewedAtUtc="2020-01-01T00:00:00Z")).encode();file.write_bytes(raw)
            result=c.policy_readiness({"hardware":file},datetime.now(timezone.utc))[0];self.assertEqual(__import__("base64").b64decode(result["retainedBase64"]),raw)
    def test_command_failure_retained(self):
        import subprocess
        with tempfile.TemporaryDirectory() as root,patch('provider.subprocess.run',return_value=subprocess.CompletedProcess(['fake'],7,b'out',b'err')):
            with self.assertRaises(c.ContractError):p.run_record(['fake'],root,'attempt')
            self.assertEqual(json.loads((Path(root)/'attempt.command.json').read_text())['exitCode'],7)
            self.assertEqual((Path(root)/'attempt.stdout').read_bytes(),b'out')
    def test_timeout_retained(self):
        import subprocess
        with tempfile.TemporaryDirectory() as root,patch('provider.subprocess.run',side_effect=subprocess.TimeoutExpired(['fake'],1,output=b'partial')):
            with self.assertRaises(c.ContractError):p.run_record(['fake'],root,'attempt')
            self.assertEqual(json.loads((Path(root)/'attempt.command.json').read_text())['failure'],'timeout')
    def test_failed_acquisition_retains_attempt(self):
        with tempfile.TemporaryDirectory() as root,patch('provider.run_record',side_effect=c.ContractError('failed sensor')):
            out=Path(root)/'run'
            with self.assertRaises(c.ContractError):p.acquire(out,'fake')
            self.assertEqual(json.loads((out/'attempt.json').read_text())['status'],'failed')
    def test_admit_fails_with_retained_reason(self):
        with tempfile.TemporaryDirectory() as root:
            out=Path(root)/'admission.json';self.assertEqual(baseline.main(['admit','--output',str(out)]),2)
            self.assertEqual(json.loads(out.read_text())['status'],'blocked')
    def test_atomic_snapshot_keeps_unbound_contract(self):
        with tempfile.TemporaryDirectory() as root:
            path=Path(root)/"latest.json";series=c.DiagnosticSeries(2)
            series.add({"observedAtUtc":"2026-10-30T00:00:00Z"});c.atomic_diagnostic_snapshot(path,series.snapshot([],{}))
            series.add({"observedAtUtc":"2026-10-30T00:00:01Z"});c.atomic_diagnostic_snapshot(path,series.snapshot([],{}))
            result=json.loads(path.read_text());self.assertEqual(len(result["samples"]),2);self.assertEqual(result["qualifiedWave0Publication"],"blocked");self.assertFalse(path.with_name("latest.json.pending").exists())
    def test_successful_acquisition_retains_raw_commands(self):
        import subprocess
        with tempfile.TemporaryDirectory() as root,patch("provider.subprocess.run",return_value=subprocess.CompletedProcess(["fake"],0,ROW,b"")),patch("provider.power_status",return_value={"powerSource":"unknown"}):
            out=Path(root)/"run";result=p.acquire(out,"fake",samples=1)
            self.assertEqual(result["status"],"diagnostic-complete");self.assertEqual((out/"sensor-00000.stdout").read_bytes(),ROW)
            self.assertEqual(json.loads((out/"latest-unbound.json").read_text())["kind"],"unbound-physical-gpu-observations")
    def test_no_pid_means_unavailable_foreground(self):
        self.assertEqual(p.window_facts()["status"],"unavailable")
    def test_window_facts_require_actual_control(self):
        good=dict(pid=7,hwnd=100,candidates=[100],owner_pid=7,foreground=100,foreground_pid=7,visible=True,iconic=False)
        self.assertEqual(p.classify_window_facts(**good)["status"],"controlled")
        for key,value in (("owner_pid",8),("foreground_pid",8),("foreground",200),("visible",False),("iconic",True)):
            with self.subTest(key=key):self.assertEqual(p.classify_window_facts(**dict(good,**{key:value}))["status"],"failed")
        for candidates in ([],[100,101]):
            self.assertEqual(p.classify_window_facts(**dict(good,hwnd=None,candidates=candidates))["status"],"failed")
    def test_foreground_loss_retained_as_failed_attempt(self):
        import subprocess
        with tempfile.TemporaryDirectory() as root,patch("provider.subprocess.run",return_value=subprocess.CompletedProcess(["fake"],0,ROW,b"")),patch("provider.power_status",return_value={"powerSource":"ac"}),patch("provider.window_facts",return_value={"status":"failed","foregroundHwnd":200}):
            out=Path(root)/"run"
            with self.assertRaises(c.ContractError):p.acquire(out,"fake",samples=1,client_pid=7)
            result=json.loads((out/"latest-unbound.json").read_text());self.assertEqual(result["samples"][0]["window"]["foregroundHwnd"],200)
            self.assertEqual(json.loads((out/"attempt.json").read_text())["status"],"failed")
    def test_sensor_window_gap_retains_failed_sample(self):
        times=["2026-10-30T00:00:00Z","2026-10-30T00:00:00Z","2026-10-30T00:00:06Z","2026-10-30T00:00:07Z"]
        with tempfile.TemporaryDirectory() as root,patch("provider.run_record",return_value=(ROW,{"startedAtUtc":times[0]})),patch("provider.power_status",return_value={"powerSource":"ac"}),patch("provider.window_facts",return_value={"status":"controlled"}),patch("provider.now",side_effect=times),patch("provider.time.sleep"):
            out=Path(root)/"run"
            with self.assertRaisesRegex(c.ContractError,"gap exceeds"):p.acquire(out,"fake",samples=2)
            self.assertEqual(len(json.loads((out/"latest-unbound.json").read_text())["samples"]),2)
    def test_unsupported_cpu_input_is_explicit(self):
        with self.assertRaises((ValueError,KeyError)):c.analyze_owner({'kind':'cpu-benchmark'})
if __name__=='__main__':unittest.main()
