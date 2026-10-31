import copy,json,tempfile,unittest,os,threading,ctypes
from ctypes import wintypes
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
    def test_permission_read_retries_and_preserves_error_without_relaxing_release(self):
        raw,release=self.release();path=self.root/'diagnostic-setup-release.json';atomic_new(path,release)
        original=Path.read_bytes;attempts=[]
        def transient(candidate):
            if candidate==path:
                attempts.append(candidate)
                if len(attempts)==1:raise PermissionError(13,'controlled sharing read failure')
            return original(candidate)
        with patch.object(Path,'read_bytes',transient),patch('diagnostic_control.window_facts',return_value=dict(ownerPid=1,hwnd=2,status='controlled')):
            value=hold_for_operator(self.root,raw,lambda:True)
        self.assertEqual(value['status'],'released');self.assertEqual(len(attempts),2);self.assertEqual(value['releaseReadErrors'][0]['errno'],13)
        self.assertEqual(value['expiresAtUtc'],(datetime.fromisoformat(self.checkpoint['createdAtUtc'])+timedelta(seconds=30)).isoformat())
    def test_sharing_errors_cannot_reset_deadline_or_retry_after_process_control_loss(self):
        for expired in (True,False):
            with tempfile.TemporaryDirectory() as folder:
                root=Path(folder);raw,release=self.release();path=root/'diagnostic-setup-release.json';atomic_new(path,release)
                original=Path.read_bytes;attempts=[]
                def denied(candidate):
                    if candidate==path:attempts.append(candidate);raise PermissionError(13,'still locked')
                    return original(candidate)
                allowed=iter([True,False])
                with patch.object(Path,'read_bytes',denied),patch('diagnostic_control.window_facts',return_value=dict(ownerPid=1,hwnd=2,status='controlled')),patch('diagnostic_control.time.sleep'),patch('diagnostic_control.time.monotonic',side_effect=[100,100,131] if expired else [100,100,101]):
                    with self.assertRaises(ContractError):hold_for_operator(root,raw,lambda:True if expired else next(allowed))
                self.assertEqual(len(attempts),1);outcome=json.loads((root/'diagnostic-setup-outcome.json').read_bytes());self.assertEqual(len(outcome['releaseReadErrors']),1);self.assertEqual(outcome['status'],'failed')
    def test_successful_read_after_original_deadline_still_rejects(self):
        raw,release=self.release();atomic_new(self.root/'diagnostic-setup-release.json',release)
        with patch('diagnostic_control.window_facts',return_value=dict(ownerPid=1,hwnd=2,status='controlled')),patch('diagnostic_control.time.monotonic',side_effect=[100,100,131]):
            with self.assertRaises(ContractError):hold_for_operator(self.root,raw,lambda:True)
        outcome=json.loads((self.root/'diagnostic-setup-outcome.json').read_bytes());self.assertEqual(outcome['status'],'failed');self.assertNotIn('after',outcome)
    def test_malformed_or_oversized_release_is_not_retried(self):
        for data in (b'{malformed',b'x'*65537):
            with tempfile.TemporaryDirectory() as folder:
                root=Path(folder);raw,_=self.release();path=root/'diagnostic-setup-release.json';path.write_bytes(data)
                original=Path.read_bytes;reads=[]
                def read(candidate):
                    if candidate==path:reads.append(candidate)
                    return original(candidate)
                with patch.object(Path,'read_bytes',read),patch('diagnostic_control.window_facts',return_value=dict(ownerPid=1,hwnd=2,status='controlled')),patch('diagnostic_control.time.sleep') as sleep:
                    with self.assertRaises(ValueError):hold_for_operator(root,raw,lambda:True)
                self.assertEqual(len(reads),1 if len(data)<=65536 else 0);sleep.assert_not_called()
                self.assertNotIn('releaseReadErrors',json.loads((root/'diagnostic-setup-outcome.json').read_bytes()))
    @unittest.skipUnless(os.name=='nt','Actual Windows nonsharing file-handle regression')
    def test_actual_windows_nonsharing_handle_release_recovers_within_original_budget(self):
        raw,release=self.release();path=self.root/'diagnostic-setup-release.json';atomic_new(path,release)
        kernel=ctypes.WinDLL('kernel32',use_last_error=True)
        kernel.CreateFileW.argtypes=[wintypes.LPCWSTR,wintypes.DWORD,wintypes.DWORD,ctypes.c_void_p,wintypes.DWORD,wintypes.DWORD,wintypes.HANDLE];kernel.CreateFileW.restype=wintypes.HANDLE
        kernel.CloseHandle.argtypes=[wintypes.HANDLE];kernel.CloseHandle.restype=wintypes.BOOL
        handle=kernel.CreateFileW(str(path),0x80000000,0,None,3,128,None)
        self.assertNotEqual(handle,ctypes.c_void_p(-1).value)
        denied=threading.Event();closed=threading.Event();original=Path.read_bytes
        def read(candidate):
            try:return original(candidate)
            except PermissionError:
                if candidate==path:denied.set()
                raise
        def unlock():
            denied.wait(5);kernel.CloseHandle(handle);closed.set()
        worker=threading.Thread(target=unlock);worker.start()
        try:
            with patch.object(Path,'read_bytes',read),patch('diagnostic_control.window_facts',return_value=dict(ownerPid=1,hwnd=2,status='controlled')):
                value=hold_for_operator(self.root,raw,lambda:True)
            self.assertTrue(denied.is_set());self.assertEqual(value['status'],'released');self.assertTrue(value['releaseReadErrors']);self.assertEqual(value['releaseReadErrors'][0]['errno'],13)
        finally:
            denied.set();worker.join(timeout=6);self.assertFalse(worker.is_alive());self.assertTrue(closed.is_set())
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
