"""Git-backed serial CI scope regressions; no product or integration acceptance."""
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import ci_scope as scope

ROOT=Path(__file__).resolve().parents[3]

class ScopeRepository:
    def __init__(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)/"repo";self.root.mkdir()
        self.run("init","-q")
        self.write(scope.HISTORICAL,(ROOT/scope.HISTORICAL).read_text())
        self.write("src/ForgeLine.Game/Unowned.cs","original")
        self.commit("base");self.base=self.run("rev-parse","HEAD").strip()
        self.record=json.loads((ROOT/scope.CANONICAL).read_text())
        self.record["baselineSha"]=self.base
        self.record["ciScopeProposal"]["baseSha"]=self.base
    def close(self):self.temp.cleanup()
    def run(self,*args):
        result=subprocess.run(["git","-C",str(self.root),*args],capture_output=True,text=True,check=False)
        if result.returncode:raise RuntimeError(result.stderr)
        return result.stdout
    def write(self,path,text):
        target=self.root/path;target.parent.mkdir(parents=True,exist_ok=True);target.write_text(text,encoding="utf-8")
    def commit(self,message):
        self.run("add",".");self.run("-c","user.name=ScopeFixture","-c","user.email=fixture@example.invalid","commit","-qm",message)
    def candidate(self,record=None,extra=None):
        self.write(scope.CANONICAL,json.dumps(self.record if record is None else record,indent=2)+"\n")
        self.write("src/ForgeLine.Client/RenderTelemetry/Example.cs","owned")
        for path,text in (extra or {}).items():self.write(path,text)
        self.commit("candidate");return self.run("rev-parse","HEAD").strip()
    def check(self,head,base=None):
        base=base or self.base
        paths=scope.changed_paths(self.root,base,head)
        diff=Path(self.temp.name)/"diff.txt";diff.write_text("\n".join(p for _,p in paths if p!="Directory.Build.props")+"\n")
        report=Path(self.temp.name)/"scope.json"
        result=subprocess.run([sys.executable,str(Path(scope.__file__)),"--base",base,"--head",head,"--diff-file",str(diff),"--report",str(report)],cwd=self.root,capture_output=True,text=True)
        return result,json.loads(report.read_text())

class CiScopeTests(unittest.TestCase):
    def setUp(self):self.repo=ScopeRepository();self.addCleanup(self.repo.close)
    def test_serial_bootstrap_validates_complete_diff_and_reports_only_a_proposal(self):
        head=self.repo.candidate(extra={scope.WORKFLOW:"owned workflow proposal"})
        result,report=self.repo.check(head)
        self.assertEqual(0,result.returncode,result.stderr);self.assertEqual("serial-foundation-proposal",report["mode"])
        self.assertTrue(report["proposalOnly"]);self.assertEqual(self.repo.base,report["baseSha"]);self.assertEqual(head,report["headSha"])
        self.assertEqual(hashlib.sha256(scope.git(self.repo.root,"show",head+":"+scope.CANONICAL)).hexdigest(),report["manifestSha256"])
    def test_stale_base_is_rejected_without_legacy_fallback(self):
        record=copy.deepcopy(self.repo.record);record["ciScopeProposal"]["baseSha"]="a"*40
        result,report=self.repo.check(self.repo.candidate(record));self.assertNotEqual(0,result.returncode);self.assertEqual("serial-foundation-proposal",report["mode"])
    def test_unknown_package_is_rejected(self):
        record=copy.deepcopy(self.repo.record);record["ciScopeProposal"]["package"]="2_QualifyGpuCompositionCost"
        result,_=self.repo.check(self.repo.candidate(record));self.assertNotEqual(0,result.returncode)
    def test_broad_or_other_workflow_self_authorization_is_rejected(self):
        record=copy.deepcopy(self.repo.record);record["packages"][0]["owns"]=["src/"]
        result,report=self.repo.check(self.repo.candidate(record));self.assertNotEqual(0,result.returncode);self.assertIn("bounded reviewed bootstrap",report["reason"])
    def test_unowned_production_change_is_rejected(self):
        result,_=self.repo.check(self.repo.candidate(extra={"src/ForgeLine.Game/Unowned.cs":"modified"}));self.assertNotEqual(0,result.returncode)
    def test_mixed_package_write_is_rejected(self):
        result,_=self.repo.check(self.repo.candidate(extra={"src/ForgeLine.Graphics/SceneCompositeShader.cs":"other owner"}));self.assertNotEqual(0,result.returncode)
    def test_historical_manifest_tampering_is_rejected(self):
        historical=(ROOT/scope.HISTORICAL).read_text()+"\n"
        result,report=self.repo.check(self.repo.candidate(extra={scope.HISTORICAL:historical}));self.assertNotEqual(0,result.returncode);self.assertIn("historical accepted ownership",report["reason"])
    def test_malformed_proposal_never_falls_back_to_legacy(self):
        record=copy.deepcopy(self.repo.record);del record["ciScopeProposal"]
        result,report=self.repo.check(self.repo.candidate(record));self.assertNotEqual(0,result.returncode);self.assertEqual("serial-foundation-proposal",report["mode"])
    def test_renamed_unowned_source_is_still_a_rejected_deletion(self):
        head=self.repo.candidate()
        self.repo.run("mv","src/ForgeLine.Game/Unowned.cs","src/ForgeLine.Client/RenderTelemetry/Renamed.cs")
        self.repo.commit("rename");head=self.repo.run("rev-parse","HEAD").strip()
        result,report=self.repo.check(head);self.assertNotEqual(0,result.returncode)
        self.assertIn(dict(status="D",path="src/ForgeLine.Game/Unowned.cs"),report["changedPaths"])
    def test_unchanged_record_route_keeps_legacy_positive_check(self):
        self.repo.write("docs/rendering/qualification/NewEvidence.md","legacy owned evidence")
        self.repo.commit("legacy");head=self.repo.run("rev-parse","HEAD").strip()
        result,report=self.repo.check(head);self.assertEqual(0,result.returncode,result.stderr);self.assertEqual("legacy-parallel",report["mode"]);self.assertEqual(hashlib.sha256((self.repo.root/scope.HISTORICAL).read_bytes()).hexdigest(),report["manifestSha256"])
    def test_legacy_frozen_production_change_stays_rejected(self):
        self.repo.write("src/ForgeLine.Client/ClientRenderHost.cs","frozen")
        self.repo.commit("legacy invalid");head=self.repo.run("rev-parse","HEAD").strip()
        result,report=self.repo.check(head);self.assertNotEqual(0,result.returncode);self.assertEqual("legacy-parallel",report["mode"])

if __name__=="__main__":unittest.main()
