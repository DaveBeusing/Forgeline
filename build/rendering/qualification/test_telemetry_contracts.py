"""Synthetic counterexamples for the new producer/validator seams, not product evidence."""
import copy
import base64
import hashlib
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import unittest
import qualification as q
import telemetry_contracts as t

class TelemetryContractsTests(unittest.TestCase):
    def physical(self):
        now=datetime.now(timezone.utc)
        identity="a"*64
        policy=dict(schemaVersion=1,kind="hardware-stability-policy",hardwareIdentitySha256=identity,
                    reviewer="test-fixture",rationale="Synthetic positive/negative contract",reviewedAtUtc=(now-timedelta(minutes=1)).isoformat(),
                    temperatureCelsius=dict(minimum=20,maximum=90),clockMHz=dict(minimum=500,maximum=3000),powerWatts=dict(minimum=10,maximum=300))
        rows=[dict(observedAtUtc=(now-timedelta(seconds=i)).isoformat(),temperatureCelsius=50,clockMHz=1000,powerWatts=60,throttling=False,powerSource="ac") for i in (2,1,0)]
        observations=dict(schemaVersion=1,kind="physical-gpu-observations",hardwareIdentitySha256=identity,provider="synthetic-not-hardware",samples=rows)
        return observations,policy,identity,now
    def test_physical_conditions_require_reviewed_identity_and_complete_recent_trace(self):
        values=self.physical()
        self.assertEqual("stable", t.hardware_conditions(*values)["thermalState"])
        for defect in ("identity","stale","gap","future","throttle","nan"):
            observations,policy,identity,now=copy.deepcopy(values)
            with self.subTest(defect=defect):
                if defect=="identity": identity="b"*64
                if defect=="stale": now+=timedelta(seconds=6)
                if defect=="gap": observations["samples"][0]["observedAtUtc"]=(now-timedelta(seconds=20)).isoformat()
                if defect=="future": observations["samples"][-1]["observedAtUtc"]=(now+timedelta(seconds=1)).isoformat()
                if defect=="nan": observations["samples"][-1]["clockMHz"]=float("nan")
                if defect=="throttle":
                    observations["samples"][-1]["throttling"]=True
                    self.assertEqual("unstable",t.hardware_conditions(observations,policy,identity,now)["thermalState"])
                else:
                    with self.assertRaises(q.ValidationError): t.hardware_conditions(observations,policy,identity,now)
        with self.assertRaises(q.ValidationError):
            t.hardware_conditions(*values, required_since=values[-1]-timedelta(seconds=10))
    def raw(self):
        return dict(overflowed=False,scope="native-reference-direct-queue",worldIntervalIncludesShadow=True,
                    completions=[dict(queue="direct",generation=1,resourceGeneration=1,submittedFence=4,completedFence=6,frequencyHz=1000,startTick=10,endTick=30,worldEndTick=20,overlayStartTick=25,shadowStartTick=12,shadowEndTick=15,particleStartTick=None,particleEndTick=None)])
    def test_raw_timestamps_reject_corrupt_generations_fences_passes_and_durations(self):
        raw=self.raw(); self.assertEqual(1,q.validate_raw_gpu_timing(raw,[dict(fence=4,frameMilliseconds=20)])["samples"])
        for field,value in (("resourceGeneration",0),("completedFence",3),("completedFence",2**64-1),("shadowEndTick",None),("particleStartTick",12),("endTick",10)):
            bad=copy.deepcopy(raw);bad["completions"][0][field]=value
            with self.subTest(field=field,value=value), self.assertRaises(q.ValidationError): q.validate_raw_gpu_timing(bad)
        bad=copy.deepcopy(raw);bad["completions"]*=2
        with self.assertRaises(q.ValidationError):q.validate_raw_gpu_timing(bad)
        bad=copy.deepcopy(raw);bad["overflowed"]=True
        with self.assertRaises(q.ValidationError):q.validate_raw_gpu_timing(bad)
        with self.assertRaises(q.ValidationError):q.validate_raw_gpu_timing(raw,[dict(fence=4,frameMilliseconds=21)])
    def test_physical_trace_is_bound_to_actual_native_identity_interval_and_exact_bytes(self):
        observations,policy,identity,now=self.physical()
        sample_bytes,policy_bytes=json.dumps(observations).encode(),json.dumps(policy).encode()
        conditions=dict(t.hardware_conditions(observations,policy,identity,now),observedAtUtc=now.isoformat(),
                        hardwareObservationSha256=hashlib.sha256(sample_bytes).hexdigest(),hardwarePolicySha256=hashlib.sha256(policy_bytes).hexdigest(),
                        rawHardwareObservationBase64=base64.b64encode(sample_bytes).decode(),rawHardwarePolicyBase64=base64.b64encode(policy_bytes).decode())
        q.validate_physical_run_conditions(conditions,identity,now.timestamp()-1,now.timestamp()-.1,after=True)
        with self.assertRaises(q.ValidationError):q.validate_physical_run_conditions(conditions,"b"*64,now.timestamp()-1,now.timestamp()-.1,after=True)
        changed=copy.deepcopy(conditions);changed["hardwareObservations"]["samples"][0]["clockMHz"]=999
        with self.assertRaises(q.ValidationError):q.validate_physical_run_conditions(changed,identity,now.timestamp()-1,now.timestamp()-.1,after=True)
        with self.assertRaises(q.ValidationError):q.validate_physical_run_conditions(conditions,identity,now.timestamp()-10,now.timestamp()-.1,after=True)
    def test_actual_owner_stream_is_valid_structural_evidence_without_native_fixture_promotion(self):
        sample=dict(index=0,startTick=10,endTick=20,processAllocatedBytes=16,threadAllocatedBytes=8,state="gameplay",
                    phases={name:dict(startTick=10,endTick=20) for name in q.PHASES},gpu=None)
        value=dict(schemaVersion=1,kind="live-render-owner-telemetry",scope="live-client",gpuStreamScope="independent-live-client",
                   nativeFixtureBindingAvailable=False,streamId="synthetic-owner",frequencyHz=1000,overflowed=False,
                   includes=list(q.PHASES),window=dict(completed=True,contiguous=True,arm="first-gameplay-iteration",warmups=64,samples=1),
                   gpuCompletions=[],frames=[sample])
        self.assertEqual("open",q.validate_live_owner_telemetry(value)["nativeFixtureAcceptance"])
        with self.assertRaises(q.ValidationError):q.validate_frame_envelope(value)
        for key in ("state","overflow","window"):
            bad=copy.deepcopy(value)
            if key=="state":bad["frames"][0]["state"]="terrain-preparing"
            if key=="overflow":bad["overflowed"]=True
            if key=="window":bad["window"]["completed"]=False
            with self.assertRaises(q.ValidationError):q.validate_live_owner_telemetry(bad)

    def test_initiative_ownership_rejects_overlap_forward_dependencies_and_unauthorized_diff(self):
        path=Path(__file__).parents[3]/"docs/rendering/render-engine-upgrade/Ownership.json"
        value=json.loads(path.read_text())
        t.initiative_ownership(value,"0_CompleteRenderTelemetry",["src/ForgeLine.Graphics/RenderTelemetry/New.cs","Directory.Build.props"])
        with self.assertRaises(q.ValidationError):t.initiative_ownership(value,"0_CompleteRenderTelemetry",["src/ForgeLine.Game/Forged.cs"])
        duplicate=copy.deepcopy(value);duplicate["packages"].append(duplicate["packages"][0])
        with self.assertRaises(q.ValidationError):t.initiative_ownership(duplicate)
        future=copy.deepcopy(value);future["packages"][0]["dependsOn"]=[future["packages"][-1]["id"]]
        with self.assertRaises(q.ValidationError):t.initiative_ownership(future)
        overlap=copy.deepcopy(value);overlap["packages"][3]["owns"].append(overlap["packages"][2]["owns"][0])
        with self.assertRaises(q.ValidationError):t.initiative_ownership(overlap)

if __name__=="__main__":unittest.main()
