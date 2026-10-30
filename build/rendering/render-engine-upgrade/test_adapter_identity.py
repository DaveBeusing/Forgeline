import copy,json,unittest,tempfile
from pathlib import Path
from unittest.mock import patch
from adapter_identity import bind_adapter,normalized_pci,luid_hex,query_cuda_identity_bounded
from baseline_contracts import ContractError
class AdapterBinding(unittest.TestCase):
    def setUp(self):
        self.identity=dict(schemaVersion=1,adapterLuidHex='0000000000015520',nodeCount=1,queueNodeMask=0,effectiveNodeMask=1,generation=2,processId=42,sessionId='a'*32)
        self.native=dict(adapterIdentity=self.identity,commit='a'*40)
        self.cuda=dict(kind='cuda-physical-device-inventory',schemaVersion=1,calls=[dict(function=name,result=0) for name in ('cuInit','cuDriverGetVersion','cuDeviceGetCount','cuDeviceGet','cuDeviceGetLuid','cuDeviceGetUuid_v2','cuDeviceGetPCIBusId')],status='available',dllSha256Before='a'*64,dllSha256After='a'*64,devices=[dict(adapterLuidHex=self.identity['adapterLuidHex'],nodeMask=1,luidBytesHex='2055010000000000',uuid='GPU-1234',pciBusId='0000:30:00.0')])
        self.sensors=[dict(uuid='GPU-1234',pciBusId='00000000:30:00.0',name='Name is not identity')]
    def bind(self):return bind_adapter(json.dumps(self.native).encode(),self.cuda,self.sensors)
    def test_exact_producer_luid_node_and_provider_uuid_pci(self):
        result=self.bind();self.assertFalse(result['exactPhysicalMappingAvailable']);self.assertTrue(result['structuralIdentityMatch']);self.assertEqual(result['pciBusId'],'00000000:30:00.0')
        self.assertEqual(result['rendererIdentity']['processId'],42)
    def test_missing_or_malformed_actual_producer_rejected(self):
        for name,value in (('adapterLuidHex','0'*16),('adapterLuidHex','15520'),('nodeCount',0),('nodeCount',True),('nodeCount',2),('queueNodeMask',2),('effectiveNodeMask',0),('effectiveNodeMask',2),('generation',0),('processId',0)):
            with self.subTest(name=name,value=value):
                self.native['adapterIdentity']=dict(self.identity,**{name:value})
                with self.assertRaises(ContractError):self.bind()
        self.native['adapterIdentity']=None
        with self.assertRaises(ContractError):self.bind()
    def test_luid_or_node_mismatch_cannot_use_same_adapter_name(self):
        for key,value in (('adapterLuidHex','0000000000025520'),('nodeMask',2)):
            altered=copy.deepcopy(self.cuda);altered['devices'][0][key]=value
            with self.assertRaises(ContractError):bind_adapter(json.dumps(self.native).encode(),altered,self.sensors)
    def test_duplicate_cuda_identity_rejected(self):
        self.cuda['devices']*=2
        with self.assertRaises(ContractError):self.bind()
    def test_uuid_and_pci_both_required(self):
        for key,value in (('uuid','GPU-other'),('pciBusId','0000:31:00.0')):
            altered=[dict(self.sensors[0],**{key:value})]
            with self.assertRaises(ContractError):bind_adapter(json.dumps(self.native).encode(),self.cuda,altered)
    def test_provider_uuid_and_pci_must_be_unique(self):
        for extra in (dict(self.sensors[0],pciBusId='0000:31:00.0'),dict(self.sensors[0],uuid='GPU-other')):
            with self.assertRaises(ContractError):bind_adapter(json.dumps(self.native).encode(),self.cuda,self.sensors+[extra])
    def test_changed_or_missing_driver_dll_hash_rejected(self):
        for after in ('b'*64,None):
            self.cuda['dllSha256After']=after
            with self.assertRaises(ContractError):self.bind()
    def test_raw_luid_byte_order_and_high_bit(self):
        self.assertEqual(luid_hex(bytes.fromhex("78563412ffffffff")),"ffffffff12345678")
        self.cuda["devices"][0]["luidBytesHex"]="0000000000015520"
        with self.assertRaises(ContractError):self.bind()
    def test_live_run_binding_requires_real_process_lifetime_and_command(self):
        self.native.update(kind="actual-renderer-identity-checkpoint",runId="b"*32,sourceSha="a"*40,runtimeFingerprint="b"*64,clientAssemblySha256="c"*64,createdAtUtc="2026-10-30T00:00:01Z")
        self.identity["processStartedFileTimeUtc"]=123
        self.cuda.update(startedAtUtc="2026-10-30T00:00:02Z",completedAtUtc="2026-10-30T00:00:03Z")
        before=dict(alive=True,processId=42,creationFileTimeUtc=123,imageSha256="d"*64,observedAtUtc="2026-10-30T00:00:01.5Z")
        after=dict(before,observedAtUtc="2026-10-30T00:00:04Z")
        context=dict(processId=42,sourceSha="a"*40,runtimeFingerprint="b"*64,clientAssemblySha256="c"*64,startedAtUtc="2026-10-30T00:00:00Z")
        self.assertTrue(bind_adapter(json.dumps(self.native).encode(),self.cuda,self.sensors,before,after,context)["exactPhysicalMappingAvailable"])
        for key,value in (("alive",False),("processId",43),("creationFileTimeUtc",124),("imageSha256","e"*64),("observedAtUtc","2026-10-30T00:00:02.5Z")):
            with self.subTest(key=key),self.assertRaises(ContractError):bind_adapter(json.dumps(self.native).encode(),self.cuda,self.sensors,before,dict(after,**{key:value}),context)
        for key,value in (("sourceSha","e"*40),("runtimeFingerprint","e"*64),("clientAssemblySha256","e"*64),("processId",43),("startedAtUtc","2026-10-30T00:00:05Z")):
            with self.subTest(key=key),self.assertRaises(ContractError):bind_adapter(json.dumps(self.native).encode(),self.cuda,self.sensors,before,after,dict(context,**{key:value}))
    def test_boolean_schema_and_masks_rejected(self):
        self.identity["schemaVersion"]=True
        with self.assertRaises(ContractError):self.bind()
        self.identity["schemaVersion"]=1;self.cuda["devices"][0]["nodeMask"]=True
        with self.assertRaises(ContractError):self.bind()
    def test_missing_or_failed_cuda_call_provenance_rejected(self):
        for calls in ([],[dict(function="cuInit",result=0)], [dict(function="cuInit",result=True)]*7):
            altered=dict(self.cuda,calls=calls)
            with self.assertRaises(ContractError):bind_adapter(json.dumps(self.native).encode(),altered,self.sensors)
    def test_cuda_calls_run_only_in_bounded_retained_helper_process(self):
        with tempfile.TemporaryDirectory() as root,patch("provider.run_record",side_effect=ContractError("helper timed out")) as runner,patch("adapter_identity.query_cuda_identity") as inline:
            with self.assertRaisesRegex(ContractError,"helper timed out"):query_cuda_identity_bounded("unused",root)
            inline.assert_not_called();self.assertEqual(runner.call_args.kwargs["timeout"],10)
            self.assertTrue((Path(root)/"cuda-probe-source.json").exists())
    def test_full_pci_preserves_domain(self):
        self.assertEqual(normalized_pci('0001:30:00.0'),'00000001:30:00.0')
        for value in ('30:00.0','0000:30:20.0','0000:30:00.8'):
            with self.assertRaises(ContractError):normalized_pci(value)
if __name__=='__main__':unittest.main()
