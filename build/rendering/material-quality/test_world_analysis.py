"""Independent boundary cases for the component timing gate."""
import copy
import contextlib
import io
import json
import sys
import tempfile
from unittest.mock import patch
import importlib.util
from pathlib import Path
import unittest

spec=importlib.util.spec_from_file_location("world_analysis",Path(__file__).with_name("Analyze-WorldPairs.py"))
analysis=importlib.util.module_from_spec(spec)
spec.loader.exec_module(analysis)

class DistributionTests(unittest.TestCase):
    def test_nearest_rank_keeps_lower_median_and_tails(self):
        result=analysis.stats(list(range(1,129)))
        self.assertEqual((128,64.5,64,122,127),tuple(result[k] for k in ("count","mean","p50","p95","p99")))

    def test_order_does_not_change_distribution(self):
        self.assertEqual(analysis.stats(list(range(1,129))),analysis.stats(list(range(128,0,-1))))

    def test_unavailable_or_invalid_samples_cannot_pass_a_budget(self):
        for values in ([1.0]*127,[0.0]*128,[float("nan")]+[1.0]*127,[float("inf")]+[1.0]*127,[-1.0]+[1.0]*127):
            with self.subTest(first=values[0],count=len(values)):
                self.assertIsNone(analysis.stats(values))

class PairContractTests(unittest.TestCase):
    def reports(self):
        report={"schemaVersion":2,"windowEventsPumped":True,"powerRequirement":"scoped",
                "catalogSignature":"fixed","manifestSha256":"fixed","Device":{"adapter":"same"},"driver":"same",
                "source":"fixed","Surface":{"Width":1920,"Height":1080,"BufferCount":3,"PresentMode":"Immediate",
                    "IsSuspended":False,"IsOccluded":False,"ResizePending":False},"rows":[]}
        for index in range(8):
            report["rows"].append(dict(quality=index//2,scene=index%2,zoom=1,Fingerprint=str(index),
                profile={"Shadows":{"Enabled":False}},cpuMilliseconds=[1.0]*128,
                worldGpuMilliseconds=[1.0]*128,totalGpuMilliseconds=[1.0]*128,shadowGpuMilliseconds=[]))
        return {name:copy.deepcopy(report) for name in ("before-1","after-1","after-2","before-2")}

    def run_reports(self,reports):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            for name,report in reports.items(): (root/(name+".json")).write_text(json.dumps(report),encoding="utf-8")
            with patch.object(sys,"argv",["analysis",directory]),contextlib.redirect_stdout(io.StringIO()): analysis.main()
            return json.loads((root/"analysis.json").read_text())

    def test_identical_positive_independent_cohorts_pass(self):
        result=self.run_reports(self.reports())
        self.assertTrue(result["performancePassed"])
        self.assertEqual(16,len(result["cases"]))
        self.assertEqual([],result["unqualified"])

    def test_mismatched_method_or_unsettled_surface_is_rejected(self):
        for name,value in (("windowEventsPumped",False),("schemaVersion",1)):
            reports=self.reports();reports["after-1"][name]=value
            with self.subTest(name=name),self.assertRaises(AssertionError): self.run_reports(reports)
        for name,value in (("Width",1280),("IsOccluded",True)):
            reports=self.reports();reports["after-1"]["Surface"][name]=value
            with self.subTest(name=name),self.assertRaises(AssertionError): self.run_reports(reports)

    def test_zero_enabled_series_remains_unqualified(self):
        reports=self.reports();reports["after-1"]["rows"][0]["cpuMilliseconds"]=[0.0]*128
        result=self.run_reports(reports)
        self.assertFalse(result["performancePassed"])
        self.assertEqual(1,len(result["unqualified"]))

if __name__=="__main__": unittest.main()
