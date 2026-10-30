"""Bounded diagnostic baseline contracts. No approval or physical binding is inferred."""
from __future__ import annotations
from datetime import datetime, timezone
import base64
import hashlib
import json
import math
from pathlib import Path
import re
import sys

FROZEN=Path(__file__).resolve().parents[1]/"qualification"
if str(FROZEN) not in sys.path:sys.path.insert(0,str(FROZEN))
from qualification import canonical, distribution, validate_live_owner_telemetry

class ContractError(ValueError):pass
class ReadinessBlocked(ContractError):pass

def require(value,message):
    if not value:raise ContractError(message)

def utc(value):
    require(type(value) is str,"UTC timestamp must be text")
    try:result=datetime.fromisoformat(value.replace("Z","+00:00"))
    except ValueError as error:raise ContractError("Invalid UTC timestamp") from error
    require(result.tzinfo is not None,"Timestamp must carry a timezone")
    return result.astimezone(timezone.utc)

def sha256_bytes(value):return hashlib.sha256(value).hexdigest()

def parse_json_bytes(raw):
    def pairs(values):
        result={}
        for key,value in values:
            require(key not in result,"Duplicate JSON key: "+key);result[key]=value
        return result
    return json.loads(raw,object_pairs_hook=pairs,parse_constant=lambda value:(_ for _ in ()).throw(ContractError("Nonfinite JSON")))

def read_json(path):return parse_json_bytes(Path(path).read_bytes())

def write_new(path,value):
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
    with path.open("x",encoding="utf-8",newline="\n") as stream:json.dump(value,stream,indent=2,allow_nan=False)
    return dict(path=path.name,sha256=sha256_bytes(path.read_bytes()))

def atomic_diagnostic_snapshot(path,value):
    require(value.get("kind")=="unbound-physical-gpu-observations" and value.get("exactPhysicalMappingAvailable") is False,
            "Only explicitly unbound diagnostic snapshots may be published by this provider")
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
    temporary=path.with_name(path.name+".pending")
    with temporary.open("x",encoding="utf-8",newline="\n") as stream:
        json.dump(value,stream,separators=(",",":"),allow_nan=False);stream.flush()
        import os;os.fsync(stream.fileno())
    temporary.replace(path)

def qualified_wave0_snapshot(*_args,**_kwargs):
    raise ReadinessBlocked("Native manifest has no renderer LUID/PCI/UUID attestation; accepted physical-gpu-observations publication is unavailable")

def native_identity(manifest,sdk):
    require(type(manifest) is dict and type(sdk) is str and sdk,"Native manifest and actual source SDK are required")
    captures=manifest.get("captures")
    require(type(captures) is list and captures,"Native captures missing")
    identities=[]
    for capture in captures:
        graphics=capture.get("graphics",{});device=graphics.get("device",{});debug=graphics.get("debug",{})
        require(type(device.get("adapterName")) is str and type(device.get("isSoftwareAdapter")) is bool,"Native device identity missing")
        require(type(debug.get("enabled")) is bool,"Native debug identity missing")
        identity=dict(environment=manifest.get("environment"),runtime=manifest.get("runtime"),driver=manifest.get("driver"),device=device,debugEnabled=debug["enabled"],sdk=sdk)
        require(all(identity[name] is not None for name in ("environment","runtime","driver")),"Incomplete canonical native hardware identity")
        identities.append((sha256_bytes(canonical(identity)),identity))
    require(all(value[0]==identities[0][0] for value in identities),"Actual native devices differ within manifest")
    return dict(hardwareIdentitySha256=identities[0][0],identity=identities[0][1],
                exactPhysicalMappingAvailable=False,sdkSourceBinding="caller-supplied-unverified",reason="Native contract exports no LUID, PCI address or UUID; supplied SDK source is not attested")

def candidate_mapping(native,inventory,expected_uuid=None):
    require(type(inventory) is list,"Provider inventory must be a list")
    seen=set()
    for device in inventory:
        key=(device.get("uuid"),device.get("pciBusId"))
        require(all(type(v) is str and v for v in key) and key not in seen,"Missing or duplicated provider physical identity")
        seen.add(key)
    identity=native.get("identity",{}) if native else {};device=identity.get("device",{})
    candidates=[row for row in inventory if row.get("name")==device.get("adapterName")]
    if expected_uuid is not None:require(any(row["uuid"]==expected_uuid for row in inventory),"Expected UUID is absent from actual inventory")
    if expected_uuid is not None and len(candidates)==1:require(candidates[0]["uuid"]==expected_uuid,"Expected UUID differs from native-name diagnostic candidate")
    status="unique-inventory-candidate" if len(candidates)==1 else "ambiguous-inventory-candidates" if len(candidates)>1 else "no-native-inventory-match"
    return dict(status=status,candidates=candidates,expectedUuid=expected_uuid,
                nativeDriver=identity.get("driver"),providerDrivers=[row.get("driverVersion") for row in candidates],
                driverAssociation="unavailable: Windows and provider driver versions are retained without inferred conversion",
                candidate=candidates[0] if len(candidates)==1 else None,exactPhysicalMappingAvailable=False,
                reason="Name/driver inventory matching cannot attest which physical GPU the D3D12 device uses")

# NVIDIA NVML clocks-event reason bit values. Preserve unknown bits; never infer no throttling.
# https://docs.nvidia.com/deploy/archive/R550/nvml-api/group__nvmlClocksEventReasons.html
EVENT_BITS={1:"gpu-idle",2:"applications-clocks-setting",4:"software-power-cap",8:"hardware-slowdown",
            16:"sync-boost",32:"software-thermal-slowdown",64:"hardware-thermal-slowdown",128:"hardware-power-brake",256:"display-clock-setting"}

def event_reasons(raw):
    require(type(raw) is str and re.fullmatch(r"0x[0-9a-fA-F]+",raw) is not None,"Unavailable or malformed event-reason mask")
    mask=int(raw,16);known=sum(EVENT_BITS);unknown=mask & ~known
    return dict(raw=raw,mask=mask,reasons=[name for bit,name in EVENT_BITS.items() if mask&bit],unknownBits=unknown,
                throttling=None if unknown else bool(mask),interpretation="Conservative any-active-reason flag; idle is retained, not treated as stable hardware")

def numeric(raw,name):
    try:value=float(raw)
    except (TypeError,ValueError) as error:raise ContractError(name+" unavailable") from error
    require(math.isfinite(value) and value>=0,name+" must be finite and nonnegative")
    return value

class DiagnosticSeries:
    def __init__(self,capacity):
        require(type(capacity) is int and 1<=capacity<=43201,"Diagnostic sample capacity outside declared bound")
        self.capacity=capacity;self.samples=[];self.overflowed=False
    def add(self,sample):
        at=utc(sample.get("observedAtUtc"))
        if self.samples:require(utc(self.samples[-1]["observedAtUtc"])<at,"Sensor timestamps must increase")
        if len(self.samples)==self.capacity:self.overflowed=True;raise ContractError("Declared sensor capacity exhausted; no old sample was discarded")
        self.samples.append(sample)
    def snapshot(self,inventory,mapping):
        return dict(schemaVersion=1,kind="unbound-physical-gpu-observations",exactPhysicalMappingAvailable=False,
                    provider="nvidia-smi+GetSystemPowerStatus",samples=list(self.samples),inventory=inventory,mapping=mapping,
                    overflowed=self.overflowed,qualifiedWave0Publication="blocked")

def policy_readiness(paths,freeze_time):
    result=[]
    for name in ("hardware","allocation","gpuMemory"):
        path=paths.get(name)
        if not path:result.append(dict(gate=name,status="open",reason="No previously reviewed common policy supplied"));continue
        try:
            raw=Path(path).read_bytes();policy=parse_json_bytes(raw)
            require(type(policy) is dict and policy.get("reviewer") and policy.get("rationale"),"Policy lacks actual review metadata")
            require(utc(policy.get("reviewedAtUtc"))<=freeze_time,"Policy review postdates protocol freeze")
            result.append(dict(gate=name,status="review-record-present",path=str(path),sha256=sha256_bytes(raw),
                               retainedBase64=base64.b64encode(raw).decode("ascii"),
                               reason="Metadata presence is not approval verification or physical acceptance"))
        except (OSError,ValueError,TypeError) as error:result.append(dict(gate=name,status="open",reason=str(error)))
    return result

def freeze_plan(execution_source,planning_source,freeze_time,policies=None):
    for sha in (execution_source,planning_source):require(type(sha) is str and re.fullmatch(r"[0-9a-f]{40}",sha),"Exact full source SHAs required")
    pairs=[dict(pair=i,order=["baseline","candidate"] if i%2 else ["candidate","baseline"]) for i in range(1,6)]
    return dict(schemaVersion=1,kind="render-baseline-protocol-proposal",protocolCompleteness="incomplete: binary inventories and precise additional cohorts unbound",executionSource=execution_source,planningSource=planning_source,
                frozenAtUtc=freeze_time.isoformat(),comparison="identical-instrumented-source-A/A",pairs=pairs,warmups=64,warmSamples=512,coldSamples=128,
                regressionPercent=15,canonicalMatrix=dict(rasterDpi=96,scenes=["IndustrialBase","BattlefieldCombat","CombatSupportRepeated"],
                profiles=["Medium","High"],zooms=["CloseTactical","NormalGameplay","Strategic"],displays=["1920x1080","2560x1440","3840x2160"],
                variants=[dict(runProfile=a,cameraPath=b) for a,b in (("Warm","Fixed"),("Warm","Sweep"),("Warm","Traverse"),("RendererCold","Fixed"))]),
                scalingScenes=["Scaling1000High","Scaling1000Low","Scaling10000High","Scaling10000Low","Scaling100000High","Scaling100000Low","ScalingTerrainOnly"],
                additionalCoverage=["real-mixed-material-skin-mechanics-effects","actual-live-authorized-gameplay","separate-observer-on/off","separate-renderer-cold"],
                policyReadiness=policy_readiness(policies or {},freeze_time),physicalAcceptance="open",wave2Ready=False,
                stopConditions=["missing-exact-physical-mapping","missing-reviewed-policies","missing-exact-source-required-CI","unavailable-native-live-fixture-binding","unstable-A/A-control","capacity-or-sensor-coverage-failure"],
                preserveAllAttempts=True)

def analyze_owner(value):
    report=validate_live_owner_telemetry(value)
    phases={name:distribution([(frame["phases"][name]["endTick"]-frame["phases"][name]["startTick"])*1000/value["frequencyHz"] for frame in value["frames"]]) for name in ("prepare","host","renderFrame","present","fenceWait")}
    gpu_passes={}
    for name,start,end in (("worldIncludingShadow","startTick","worldEndTick"),("overlay","overlayStartTick","endTick"),("shadow","shadowStartTick","shadowEndTick"),("particles","particleStartTick","particleEndTick")):
        samples=[(row[end]-row[start])*1000/row["frequencyHz"] for row in value["gpuCompletions"] if row.get(start) is not None and row.get(end) is not None]
        gpu_passes[name]=distribution(samples)
    memory={name:distribution([frame["memory"][name] for frame in value["frames"]]) for name in ("liveResourceBytes","retiringResourceBytes","textureMipBytes","stagingBytes","pendingUploadBytes","descriptors")}
    return dict(kind="diagnostic-baseline-analysis",sourceSha=value.get("sourceSha"),streamId=value.get("streamId"),report=report,phases=phases,gpuPasses=gpu_passes,logicalMemory=memory,
                physicalMemory="unavailable: logical bytes do not establish physical allocations",allocationSites="unavailable: counters are totals, not stack attribution",
                phaseSemantics="Inclusive/overlapping intervals are not summed; world includes shadow",physicalAcceptance="open",wave2Ready=False)
