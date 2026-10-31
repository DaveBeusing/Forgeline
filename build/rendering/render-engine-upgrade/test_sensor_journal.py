import base64,json,tempfile,unittest
from pathlib import Path
from baseline_contracts import ContractError
from provider import parse_inventory
from sensor_journal import SensorJournal,read_journal
RAW=b'GPU-aaaa-bbbb,00000000:30:00.0,GPU,596.53,41,180,11.08,0x0000000000000004\n'
def sample(second):return dict(observedAtUtc=f'2026-10-30T00:00:{second:02d}Z',devices=parse_inventory(RAW))
class SensorJournalTests(unittest.TestCase):
    def test_raw_append_and_constant_progress_no_accumulated_snapshot(self):
        with tempfile.TemporaryDirectory() as root:
            folder=Path(root)/'run'
            with SensorJournal(folder,capacity=3) as journal:
                journal.append(sample(0),RAW);first=(folder/'latest.json').stat().st_size
                journal.append(sample(1),RAW);second=(folder/'latest.json').stat().st_size
                self.assertLess(abs(second-first),16)
                self.assertNotIn('samples',json.loads((folder/'latest.json').read_bytes()))
            rows=read_journal(folder);self.assertEqual(len(rows),2);self.assertEqual(base64.b64decode(rows[0]['rawStdoutBase64']),RAW)
    def test_gap_retained_and_cannot_select_new_tail(self):
        with tempfile.TemporaryDirectory() as root:
            folder=Path(root)/'run'
            with SensorJournal(folder) as journal:
                journal.append(sample(0),RAW)
                with self.assertRaisesRegex(ContractError,'gap'):journal.append(sample(6),RAW)
                self.assertEqual(journal.count,2)
                with self.assertRaises(ContractError):journal.append(sample(7),RAW)
            with self.assertRaises(ContractError):read_journal(folder)
    def test_capacity_and_byte_budget_fail_without_overwriting_history(self):
        for capacity,budget in ((1,64000),(10,10)):
            with tempfile.TemporaryDirectory() as root:
                folder=Path(root)/'run'
                with SensorJournal(folder,capacity=capacity,byte_budget=budget) as journal:
                    if capacity==1:journal.append(sample(0),RAW)
                    before=(folder/'observations.jsonl').read_bytes()
                    with self.assertRaises(ContractError):journal.append(sample(1),RAW)
                    self.assertEqual((folder/'observations.jsonl').read_bytes(),before)
    def test_changed_or_truncated_raw_journal_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            folder=Path(root)/'run'
            with SensorJournal(folder) as journal:journal.append(sample(0),RAW)
            (folder/'observations.jsonl').write_bytes(b'changed')
            with self.assertRaises(ContractError):read_journal(folder)
if __name__=='__main__':unittest.main()
