import base64,copy,struct,unittest,json,tempfile
from pathlib import Path
from dataclasses import replace
import allocation_diagnostics as diagnostic
from allocation_diagnostics import correlate,positive_sample
from baseline_contracts import sha256_bytes,canonical

class AllocationClockTests(unittest.TestCase):
    def setUp(self):
        self.trace=dict(status='diagnostic-pass',expectedPid=10,clock=dict(available=True,format='pinned-nettrace-v6-raw-header',tickFrequency=1000000,syncTicks=1000000,utcSystemTimeBytesBase64=base64.b64encode(struct.pack('<8H',2026,10,5,30,12,0,0,0)).decode()),events=[dict(processId=10,nativeThreadId=12,timestampQpc=1010000)])
        self.owner=dict(sourceSha='a'*40,runtimeFingerprint='b'*64,adapterIdentity=dict(sessionId='c'*32,processStartedFileTimeUtc=555),ownerThread=dict(processId=10,nativeThreadId=12,managedThreadId=99),clockAnchor=dict(beforeTick=1000000,afterTick=1000010,frequencyHz=1000000,utc='2026-10-30T12:00:00+00:00'),frames=[dict(index=0,startTick=1005000,endTick=1020000)])
        header=bytearray(64);header[:8]=b'Nettrace';struct.pack_into('<4I',header,8,0,6,0,(1<<24)|40);header[24:40]=base64.b64decode(self.trace['clock']['utcSystemTimeBytesBase64']);struct.pack_into('<qq',header,40,1000000,1000000)
        self.trace['traceLength']=64;self.trace['traceSha256Before']=self.trace['traceSha256After']='d'*64
        self.trace['clock'].update(headerBytesBase64=base64.b64encode(header).decode(),headerSha256=sha256_bytes(header))
        self.trace['producerBinding']=dict(ownerSha256=sha256_bytes(canonical(self.owner)),traceSha256='d'*64,sourceSha='a'*40,runtimeFingerprint='b'*64,sessionId='c'*32,processId=10,processStartedFileTimeUtc=555)
    def check(self):return correlate(self.trace,self.owner,source_sha='a'*40,runtime_fingerprint='b'*64,producer_session='c'*32)
    def test_raw_tick_join_uses_native_thread_not_managed_thread(self):
        self.assertEqual(self.check()['events'][0]['frameIndex'],0)
        self.trace['events'][0]['nativeThreadId']=99
        self.assertEqual(self.check()['events'],[])
    def test_unknown_header_mismatched_frequency_source_process_and_anchor_reject(self):
        for mutation in ('unknown','frequency','bool','source','process','anchor','session'):
            with self.subTest(mutation=mutation):
                originalTrace,originalOwner=copy.deepcopy(self.trace),copy.deepcopy(self.owner)
                if mutation=='unknown':self.trace['clock']['available']=False
                elif mutation=='frequency':self.trace['clock']['tickFrequency']=2000000
                elif mutation=='bool':self.trace['clock']['tickFrequency']=True
                elif mutation=='source':self.owner['sourceSha']='d'*40
                elif mutation=='process':self.trace['events'][0]['processId']=11
                elif mutation=='session':self.owner['adapterIdentity']['sessionId']='e'*32
                else:self.owner['clockAnchor']['utc']='2026-10-30T12:10:00+00:00'
                self.assertEqual(self.check()['status'],'unavailable')
                self.trace,self.owner=originalTrace,originalOwner
    def test_ambiguous_half_open_boundary_is_not_forced_into_frame(self):
        self.trace['events'][0]['timestampQpc']=1020000
        r=self.check()['events'][0];self.assertEqual(r['status'],'ambiguous-boundary');self.assertIsNone(r['frameIndex'])
    def test_missing_original_trace_binding_is_unavailable(self):
        self.trace.pop('producerBinding');self.assertEqual(self.check()['status'],'unavailable')
    def test_legacy_null_extended_amount_uses_preserved_original_amount(self):
        self.assertTrue(positive_sample(dict(family='GCAllocationTick',payload=dict(allocationAmount64=None,AllocationAmount=100))))
        self.assertFalse(positive_sample(dict(family='GCAllocationTick',payload=dict(allocationAmount64=None,AllocationAmount=None))))
    def test_tampered_raw_header_summary_is_unavailable(self):
        self.trace['clock']['syncTicks']+=1;self.assertEqual(self.check()['status'],'unavailable')
    def test_outside_window_retained(self):
        self.trace['events'][0]['timestampQpc']=1030000
        self.assertEqual(self.check()['events'][0]['status'],'outside-window')


class SupportedRawQpcTests(unittest.TestCase):
    """Synthetic post-gate identities exercise the rule, never represent a real admitted run."""
    def setUp(self):
        fixture=AllocationClockTests();fixture.setUp();self.trace,self.owner=fixture.trace,fixture.owner
        self.owner['sourceSha']=diagnostic._RAW_PARSER_SOURCE
        header=(b'Nettrace'+struct.pack('<i',20)+b'!FastSerialization.1'+b'\x05\x05\x01'+struct.pack('<iii',4,4,5)+b'Trace\x06'+struct.pack('<8H',2026,10,5,30,12,0,0,0)+struct.pack('<qqiiii',1000000,1000000,8,10,24,0)+b'\x06')
        from trace_clock_header import decode_clock_header
        clock=decode_clock_header(header,len(header),10);clock.update(available=True,headerBytesBase64=base64.b64encode(header).decode(),headerSha256=sha256_bytes(header))
        self.trace.update(clock=clock,traceLength=len(header),parser='TraceEvent4.0.0',parserBuildIdentity=diagnostic._RAW_PARSER_BUILD,parserAssemblySha256=diagnostic._RAW_PARSER_SHA,logEventsLost=0,sourceEventsLost=0,truncatedStacks=0,conversionNotices=[])
        self.trace['producerBinding'].update(sourceSha=diagnostic._RAW_PARSER_SOURCE,ownerSha256=sha256_bytes(canonical(self.owner)))
        self.trace['events'][0].update(timestampQpc=1019999,stack=[dict(name='System.Threading.Thread.StartCallback()',module=r'C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.13\System.Private.CoreLib.dll')])
    def execution(self):
        return diagnostic._VerifiedTraceExecution(diagnostic._report_hash(self.trace),'e'*64,'d'*64,diagnostic._RAW_PARSER_SOURCE,diagnostic._RAW_DEPS_SHA,'f'*64)
    def proof(self):return diagnostic._derive_raw_qpc_proof(self.trace,self.owner,self.execution())
    def check(self,proof=None):return correlate(self.trace,self.owner,source_sha=diagnostic._RAW_PARSER_SOURCE,runtime_fingerprint='b'*64,producer_session='c'*32,raw_qpc_proof=proof)
    def test_generic_uncertainty_retained_until_internal_supported_path_is_proven(self):
        self.assertEqual(self.check()['events'][0]['status'],'ambiguous-boundary')
        proof=self.proof();self.assertIsNotNone(proof)
        value=self.check(proof);self.assertEqual(value['events'][0]['status'],'matched');self.assertEqual(value['uncertaintyTicks'],0);self.assertGreater(value['utcAnchorSanityUncertaintyTicks'],0)
        self.assertFalse(value['rawQpcSourceProof']['historicalRuntimeModuleHashesAvailable']);self.assertFalse(value['exactAllocationBytesAvailable'])
    def test_strict_integer_interior_exact_ties_and_outside_without_site_specific_rules(self):
        ticks=[1004999,1005000,1005001,1019999,1020000,1020001]
        self.trace['events']=[dict(self.trace['events'][0],timestampQpc=t) for t in ticks]
        value=self.check(self.proof())
        self.assertEqual([e['status'] for e in value['events']],['outside-window','ambiguous-boundary','matched','matched','ambiguous-boundary','outside-window'])
        self.assertEqual([e['frameIndex'] for e in value['events']],[None,None,0,0,None,None])
    def test_missing_actual_target_module_or_preflight_label_does_not_supply_proof(self):
        self.trace['preflightRuntime']='.NET 10.0.13';self.trace['events'][0]['stack']=[]
        self.assertIsNone(self.proof());self.assertEqual(self.check()['events'][0]['status'],'ambiguous-boundary')
    def test_unknown_conflicting_runtime_or_installation_roots_cannot_gain_refinement(self):
        original=copy.deepcopy(self.trace)
        for modules in ([r'C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.14\System.Private.CoreLib.dll'],[r'C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.13\System.Private.CoreLib.dll',r'C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.14\System.Text.Json.dll'],[r'C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.13\System.Private.CoreLib.dll',r'D:\dotnet\shared\Microsoft.NETCore.App\10.0.13\System.Private.CoreLib.dll']):
            self.trace=copy.deepcopy(original);self.trace['events'][0]['stack']=[dict(module=m) for m in modules]
            self.assertIsNone(self.proof());self.assertEqual(self.check()['events'][0]['status'],'ambiguous-boundary')
    def test_future_parser_image_build_or_dependencies_retain_generic_fallback(self):
        original=copy.deepcopy(self.trace)
        for name,value in (('parser','TraceEvent5.0.0'),('parserBuildIdentity','future-build'),('parserAssemblySha256','1'*64)):
            self.trace=copy.deepcopy(original);self.trace[name]=value;self.assertIsNone(self.proof());self.assertEqual(self.check()['events'][0]['status'],'ambiguous-boundary')
        self.trace=original
        self.assertIsNone(diagnostic._derive_raw_qpc_proof(self.trace,self.owner,replace(self.execution(),dependencies_sha256='1'*64)))
    def test_caller_boolean_dict_foreign_or_modified_explicit_proof_fails_without_fallback(self):
        proof=self.proof()
        for invalid in (True,dict(sameClock=True),replace(proof,rule='caller-rule'),replace(proof,contract_sha256='1'*64),replace(proof,owner_sha256='1'*64),replace(proof,target_runtime_modules=())):
            with self.subTest(invalid=type(invalid).__name__):self.assertEqual(self.check(invalid)['status'],'unavailable')
        self.trace['events'][0]['timestampQpc']-=1
        self.assertEqual(self.check(proof)['status'],'unavailable')
    def test_header_frequency_utc_hash_and_identity_gates_still_reject_before_refinement(self):
        original=copy.deepcopy(self.trace)
        for change in ('frequency','utc','headerHash','reportHash','source','process'):
            self.trace=copy.deepcopy(original)
            if change=='frequency':self.trace['clock']['tickFrequency']+=1
            elif change=='utc':self.owner['clockAnchor']['utc']='2026-10-30T12:01:00Z';self.trace['producerBinding']['ownerSha256']=sha256_bytes(canonical(self.owner))
            elif change=='headerHash':self.trace['clock']['headerSha256']='1'*64
            elif change=='reportHash':
                with self.assertRaises(ValueError):diagnostic._derive_raw_qpc_proof(self.trace,self.owner,replace(self.execution(),report_sha256='1'*64))
                continue
            elif change=='source':self.trace['producerBinding']['sourceSha']='1'*40
            else:self.trace['events'][0]['processId']=11
            with self.subTest(change=change),self.assertRaises(ValueError):self.proof()
            self.owner['clockAnchor']['utc']='2026-10-30T12:00:00+00:00'
    def test_loss_truncation_and_unverified_execution_never_produce_proof(self):
        with self.assertRaises(ValueError):diagnostic._derive_raw_qpc_proof(self.trace,self.owner,dict(sameClock=True))
        for name in ('logEventsLost','sourceEventsLost','truncatedStacks'):
            self.trace[name]=1
            with self.assertRaises(ValueError):self.proof()
            self.trace[name]=0

class OriginalExecutionGateTests(unittest.TestCase):
    """Original-file schema fixtures; no executable or trace is launched."""
    def fixture(self,root,*,dependency='4.0.0',wrong_parse_command=False):
        source='a'*40;actual='c'*64;tool=str(root/'tools/dotnet-trace.exe');parser=str(root/'parser/AllocationTrace.dll')
        def raw(name,data):
            p=root/name;p.write_bytes(data);return dict(path=name,sha256=sha256_bytes(data))
        def value(name,data):return raw(name,canonical(data))
        def command(name,argv,start,end,stdout=b''):
            outputs=dict(stdout=raw(name+'.stdout',stdout),stderr=raw(name+'.stderr',b''))
            return value(name+'.command.json',dict(argv=argv,startedAtUtc=start,completedAtUtc=end,exitCode=0,failure=None,outputs=outputs))
        start='2026-10-30T12:00:00Z';end='2026-10-30T12:00:01Z';parsed_start='2026-10-30T12:00:02Z';parsed_end='2026-10-30T12:00:03Z'
        report=dict(parserBuildIdentity='synthetic+'+source,parserAssemblySha256='b'*64,expectedPid=10)
        report_ref=value('report.json',report);deps_ref=value('parser-dependencies.json',dict(libraries={'Microsoft.Diagnostics.Tracing.TraceEvent/'+dependency:{}}))
        tools=[dict(path='dotnet-trace.exe',bytes=1,sha256='d'*64),dict(path='dotnet-trace.dll',bytes=1,sha256='e'*64)]
        parsers=[dict(path='AllocationTrace.dll',bytes=1,sha256='b'*64),dict(path='AllocationTrace.deps.json',bytes=1,sha256=deps_ref['sha256'])]
        tools_ref=value('tools.json',tools);parsers_ref=value('parsers.json',parsers)
        version=command('version',[tool,'--version'],start,end,b'10.0.750501+synthetic')
        collect=command('collect',[tool,'collect','--process-id','10','--profile','gc-verbose','--duration','00:00:30','--buffersize','256','--output','original.nettrace'],start,end)
        parsed=command('parse',['dotnet',parser,'original.nettrace','original-output','11' if wrong_parse_command else '10'],parsed_start,parsed_end)
        provenance=dict(sourceSha=source,sourceTreeClean=True,inputTraceSha256=actual,outputReportSha256=report_ref['sha256'],toolInventoryBefore=tools_ref,toolInventoryAfter=tools_ref,parserInventoryBefore=parsers_ref,parserInventoryAfter=parsers_ref,toolExecutable=tool,parserAssembly=parser,toolExecutableInventoryPath='dotnet-trace.exe',parserAssemblyInventoryPath='AllocationTrace.dll',versionCommand=version,duration='00:00:30',originalTraceOutputPath='original.nettrace',originalParserOutputDirectory='original-output',parserCommand=parsed,parserDependencies=deps_ref,parserDependenciesInventoryPath='AllocationTrace.deps.json')
        return dict(report=report_ref,command=collect,executionProvenance=value('execution.json',provenance)),report,source,actual
    def verify(self,root,**kwargs):
        import baseline_admission
        wrapper,report,source,actual=self.fixture(root,**kwargs)
        return baseline_admission.verify_trace_execution(root,wrapper,report,source,actual)
    def test_only_passed_original_execution_gates_return_internal_typed_context(self):
        with tempfile.TemporaryDirectory() as directory:
            context=self.verify(Path(directory));self.assertIs(type(context),diagnostic._VerifiedTraceExecution)
            self.assertEqual(context.source_sha,'a'*40);self.assertEqual(context.trace_sha256,'c'*64)
    def test_unknown_dependency_or_changed_parse_pid_reject_before_proof_context(self):
        for change in (dict(dependency='5.0.0'),dict(wrong_parse_command=True)):
            with tempfile.TemporaryDirectory() as directory,self.assertRaises(ValueError):self.verify(Path(directory),**change)
    def test_original_raw_output_and_report_identity_hash_gates_still_reject(self):
        import baseline_admission
        for change in ('stdout','report','source','trace'):
            with tempfile.TemporaryDirectory() as directory:
                root=Path(directory);wrapper,report,source,actual=self.fixture(root)
                if change=='stdout':(root/'version.stdout').write_bytes(b'changed')
                elif change=='report':report['parserAssemblySha256']='1'*64
                elif change=='source':source='1'*40
                else:actual='1'*64
                with self.subTest(change=change),self.assertRaises(ValueError):baseline_admission.verify_trace_execution(root,wrapper,report,source,actual)
if __name__=='__main__':unittest.main()
