import copy,json,tempfile,unittest
from datetime import datetime,timezone,timedelta
from pathlib import Path
from unittest.mock import patch
from baseline_contracts import canonical,sha256_bytes,ContractError
from diagnostic_control import atomic_new,hold_for_operator

class OperatorHandoffTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup);self.root=Path(self.tmp.name)
        self.checkpoint=dict(runId='a'*32,createdAtUtc=datetime.now(timezone.utc).isoformat(),adapterIdentity=dict(processId=1),nativeWindowHandle=2)
    def release(self,checkpoint=None):
        raw=canonical(checkpoint or self.checkpoint)
        value=dict(schemaVersion=1,kind='diagnostic-setup-release',checkpointSha256=sha256_bytes(raw),runId='a'*32,processId=1,nativeWindowHandle=2,action='operator-focus-only',completed=True)
        return raw,value
    def test_matching_release_still_requires_actual_controlled_after_facts(self):
        raw,release=self.release();atomic_new(self.root/'diagnostic-setup-release.json',release)
        with patch('diagnostic_control.window_facts',side_effect=[dict(ownerPid=1,hwnd=2,status='failed',reasons=['client-not-foreground']),dict(ownerPid=1,hwnd=2,status='controlled')]):
            value=hold_for_operator(self.root,raw,lambda:True)
        self.assertEqual(value['status'],'released');self.assertFalse(value['qualified']);self.assertEqual(value['before']['status'],'failed')
        self.assertTrue((self.root/'diagnostic-setup-outcome.json').is_file());self.assertFalse((self.root/'diagnostic-setup-hold.json.pending').exists())
    def test_replayed_wrong_pid_bool_schema_and_incomplete_release_are_retained_failures(self):
        for mutate in (lambda r:r.update(checkpointSha256='b'*64),lambda r:r.update(processId=True),lambda r:r.update(nativeWindowHandle=3),lambda r:r.update(schemaVersion=True),lambda r:r.update(completed=False)):
            with tempfile.TemporaryDirectory() as folder:
                root=Path(folder);raw,release=self.release();mutate(release);atomic_new(root/'diagnostic-setup-release.json',release)
                with patch('diagnostic_control.window_facts',return_value=dict(ownerPid=1,hwnd=2,status='controlled')):
                    with self.assertRaises(ContractError):hold_for_operator(root,raw,lambda:True)
                self.assertEqual(json.loads((root/'diagnostic-setup-outcome.json').read_bytes())['status'],'failed')
    def test_focus_claim_does_not_override_actual_foreground_loss(self):
        raw,release=self.release();atomic_new(self.root/'diagnostic-setup-release.json',release)
        with patch('diagnostic_control.window_facts',return_value=dict(ownerPid=1,hwnd=2,status='failed')):
            with self.assertRaises(ContractError):hold_for_operator(self.root,raw,lambda:True)
    def test_expired_original_checkpoint_budget_cannot_be_extended(self):
        self.checkpoint['createdAtUtc']=(datetime.now(timezone.utc)-timedelta(seconds=31)).isoformat();raw,release=self.release();atomic_new(self.root/'diagnostic-setup-release.json',release)
        with patch('diagnostic_control.window_facts',return_value=dict(ownerPid=1,hwnd=2,status='controlled')):
            with self.assertRaises(ContractError):hold_for_operator(self.root,raw,lambda:True)
    def test_foreign_window_never_publishes_actionable_hold(self):
        raw,_=self.release()
        with patch('diagnostic_control.window_facts',return_value=dict(ownerPid=99,hwnd=2,status='failed')):
            with self.assertRaises(ContractError):hold_for_operator(self.root,raw,lambda:True)
        self.assertFalse((self.root/'diagnostic-setup-hold.json').exists());self.assertTrue((self.root/'diagnostic-setup-outcome.json').exists())
    def test_atomic_publication_failure_never_exposes_partial_ready_json(self):
        def interrupted(path,value):path.write_bytes(b'{partial');raise OSError('interrupted publication')
        with patch('diagnostic_control.write_new',side_effect=interrupted):
            with self.assertRaises(OSError):atomic_new(self.root/'ready.json',{})
        self.assertFalse((self.root/'ready.json').exists());self.assertTrue((self.root/'ready.json.pending').exists())
if __name__=='__main__':unittest.main()
