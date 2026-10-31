import copy,unittest
from baseline_contracts import ContractError
from owner_observations import validate_cycles,touching,platform_touching,validate_cycle_budget

def vector(n=0):return {'slot'+str(i):n if i==0 else 0 for i in range(8)}
def owner(t):return dict(schemaVersion=1,identity='11111111-1111-1111-1111-111111111111',session=7,nativeOwnerThread=8,readStartTick=t,readEndTick=t+1,started=vector(),completed=vector(),inFlight=vector(),events=vector(),queued=0,inFlightMessages=0,completionDepth=0,highWater=0,capacity=32,overflowed=False,faulted=False,compositionSupported=True,transportStarted=vector(),transportCompleted=vector())
def facts(t):return dict(readStartTick=t,readEndTick=t+1,simulation=owner(t),uiAudio=dict(owner(t),session=0),worldAudio=dict(owner(t),session=0),platform=dict(schemaVersion=0),simulationAbsentByComposition=False,platformProviderAvailable=False)
def edge(t,n,precise=True):return dict(readStartTick=t,readEndTick=t+9,processAbsolute=n if precise else None,threadAbsolute=n if precise else None,processRead=dict(startTick=t+2,endTick=t+3) if precise else dict(startTick=0,endTick=0),threadRead=dict(startTick=t+4,endTick=t+5) if precise else dict(startTick=0,endTick=0),before=facts(t),after=facts(t+6))
def legacy(t,n,end=False):return dict(processAbsolute=n,threadAbsolute=n,processRead=dict(startTick=t+(4 if end else 2),endTick=t+(5 if end else 3)),threadRead=dict(startTick=t+(2 if end else 4),endTick=t+(3 if end else 5)),before=facts(t),after=facts(t+6))
def fixture(precise=True):
    rows=[];frames=[]
    for i in range(3):
        measured=i!=1;index=len(frames) if measured else None
        row=dict(index=i,startEdge=i,endEdge=i+1,state='gameplay' if measured else 'idle/no-payload',payloadIndex=index,fixtureOrdinal=index if measured else -1,submittedFence=10+index if measured else 0,submission=1+index if measured else 0,generation=1 if measured else 0,resourceGeneration=2 if measured else 0,payloadBegin=legacy(i*100+20,i*100+20) if measured and precise else None,payloadEnd=legacy(i*100+60,i*100+60,True) if measured and precise else None)
        rows.append(row)
        if measured:
            actual=dict(submission=1+index,submittedFence=10+index,generation=1,resourceGeneration=2)
            frame=dict(index=index,startTick=i*100+(25 if precise else 20),endTick=i*100+(61 if precise else 60))
            if precise:frame.update(actual,processAllocatedBytes=40,threadAllocatedBytes=40,serviceStartTick=i*100+21,serviceEndTick=i*100+66,observer=dict(beginProcessCounter=dict(startTick=i*100+22,endTick=i*100+23),endProcessCounter=dict(startTick=i*100+64,endTick=i*100+65)))
            else:frame['submission']=actual
            frames.append(frame)
    return dict(schemaVersion=1,kind='render-owner-cycle-observations',preciseCountersAvailable=precise,capacity=3,overflowed=False,faulted=False,windowSealed=True,terminalClosed=True,firstMeasuredCycle=0,lastMeasuredCycle=2,edges=[edge(i*100,i*100,precise) for i in range(4)],cycles=rows),frames
class OwnerObservationsTests(unittest.TestCase):
    def test_actual_shared_edges_algebra_and_idle_inclusive_bundles(self):
        value,frames=fixture();result=validate_cycles(value,frames)
        self.assertEqual(result['samples'][0]['allocations']['processAbsolute'],dict(cycle=100,lead=20,payload=40,tail=40))
        sample=result['samples'][1];self.assertEqual((sample['bundleStartCycle'],sample['bundleEndCycle']),(1,2));self.assertTrue(sample['previousMeasuredSuccessAvailable'])
        self.assertFalse(result['samples'][0]['previousMeasuredSuccessAvailable']);self.assertFalse(sample['workload']['platform']['available'])
    def test_off_has_common_cycles_without_fake_allocation_or_legacy(self):
        value,frames=fixture(False);result=validate_cycles(value,frames);self.assertIsNone(result['samples'][0]['allocations'])
        value['edges'][0]['processAbsolute']=0
        with self.assertRaisesRegex(ContractError,'OFF|Off'):validate_cycles(value,frames)
    def test_foreign_tuple_missing_or_reused_edge_fault_and_overflow_rejected(self):
        mutations=[lambda v:v['cycles'][2].update(submittedFence=99),lambda v:v['cycles'][1].update(startEdge=0),lambda v:v['cycles'][2].update(payloadIndex=0),lambda v:v.update(overflowed=True),lambda v:v.update(faulted=True),lambda v:v.update(windowSealed=False),lambda v:v['edges'][2].update(processAbsolute=0),lambda v:v['cycles'][0]['payloadEnd'].update(threadAbsolute=61),lambda v:v['edges'][0]['processRead'].update(startTick=0)]
        for mutation in mutations:
            value,frames=fixture();mutation(value)
            with self.subTest(mutation=mutation),self.assertRaises(ContractError):validate_cycles(value,frames)
    def test_crossing_operation_and_concurrent_transport_use_conservative_envelopes(self):
        before=owner(1);before.update(started=vector(1),inFlight=vector(1),transportStarted=vector(2))
        after=owner(10);after.update(started=vector(2),completed=vector(2),transportStarted=vector(3),transportCompleted=vector(3))
        result=touching(before,after);self.assertEqual(result['touching'][0],2);self.assertEqual(result['transportTouching'][0],3)
        for mutation in [dict(nativeOwnerThread=9),dict(events=vector(-1)),dict(compositionSupported=False),dict(started=vector(0)),dict(faulted=True)]:
            with self.assertRaises(ContractError):touching(before,dict(after,**mutation))
    def test_original_frame_and_observer_timing_cannot_be_shifted_with_same_tuple_delta(self):
        for mutation in [lambda f:f.update(startTick=1),lambda f:f.update(endTick=999),lambda f:f['observer']['beginProcessCounter'].update(startTick=21),lambda f:f.update(serviceEndTick=999)]:
            value,frames=fixture();mutation(frames[0])
            with self.assertRaises(ContractError):validate_cycles(value,frames)
        value,frames=fixture(False);frames[1]['startTick']=101
        with self.assertRaisesRegex(ContractError,'outside'):validate_cycles(value,frames)
    def test_nested_reads_and_actual_producer_identity_are_required(self):
        for name in ('simulation','uiAudio','worldAudio'):
            value,frames=fixture();value['edges'][-1]['after'][name]['readStartTick']=1;value['edges'][-1]['after'][name]['readEndTick']=2
            with self.subTest(name=name),self.assertRaisesRegex(ContractError,'enclosed'):validate_cycles(value,frames)
        value,frames=fixture()
        with self.assertRaisesRegex(ContractError,'session'):validate_cycles(value,frames,expected_session=99)
        value['edges'][0]['before']['platform']=dict(schemaVersion=1,readStartTick=0,readEndTick=1,nativeWindowHandle=999)
        with self.assertRaisesRegex(ContractError,'HWND'):validate_cycles(value,frames,expected_hwnd=43)
    def test_authored_absence_and_world_session_follow_actual_producer(self):
        value,frames=fixture();value['edges'][0]['before']['simulationAbsentByComposition']=True
        with self.assertRaisesRegex(ContractError,'producer kind'):validate_cycles(value,frames,producer_kind='actual-live-session',expected_session=7)
        value,frames=fixture()
        with self.assertRaisesRegex(ContractError,'World audio session'):validate_cycles(value,frames,producer_kind='actual-live-session',expected_session=7)
    def test_idle_allocations_are_in_selected_bundle_budget_and_common_edge_time(self):
        value,frames=fixture();value['edges'][2].update(processAbsolute=100000100,threadAbsolute=100000100);value['edges'][3].update(processAbsolute=100000200,threadAbsolute=100000200)
        # Keep the current payload-bearing cycle small while idle1 owns the large increment.
        value['cycles'][2]['payloadBegin'].update(processAbsolute=100000120,threadAbsolute=100000120)
        value['cycles'][2]['payloadEnd'].update(processAbsolute=100000160,threadAbsolute=100000160)
        result=validate_cycles(value,frames);sample=result['samples'][1]
        self.assertEqual(sample['allocations']['processAbsolute']['cycle'],100)
        self.assertEqual(sample['bundleAllocations']['processAbsolute'],100000100)
        self.assertEqual(sample['bundleReadEndTicks'],200) # One shared-edge observation per cycle; no overlapping sums.
        for item in result['samples']:item['workload']['platform']=dict(available=True)
        with self.assertRaisesRegex(ContractError,'idle-inclusive'):validate_cycle_budget(result,dict(allocationInterval='owner-cycle-v1',maximumPerSample=dict(processAllocatedBytes=1000,threadAllocatedBytes=1000)))
    def test_first_warm_bundle_uses_actual_transition_and_preserves_intervening_idle(self):
        value,frames=fixture();value['cycles'][0].update(payloadIndex=None,fixtureOrdinal=0);value['cycles'][2].update(payloadIndex=0,fixtureOrdinal=1)
        frames=[dict(frames[1],index=0)];value.update(firstMeasuredCycle=2,lastMeasuredCycle=2)
        original=dict(state='gameplay',observer=dict(endProcessCounter=copy.deepcopy(value['cycles'][0]['payloadEnd']['processRead'])))
        result=validate_cycles(value,frames,warmups=1,prefix_frames=[original],producer_kind='actual-live-session')
        self.assertEqual(result['warmupTransitionCycle'],0);self.assertEqual(result['samples'][0]['bundleStartCycle'],1);self.assertEqual(result['samples'][0]['bundleAllocations']['processAbsolute'],200)
        original['observer']['endProcessCounter']['startTick']+=1
        with self.assertRaisesRegex(ContractError,'warm-transition'):validate_cycles(value,frames,warmups=1,prefix_frames=[original],producer_kind='actual-live-session')
    def test_unbound_is_unavailable_not_proven_absent(self):
        self.assertFalse(touching(dict(schemaVersion=0),owner(10))['available'])
        self.assertFalse(touching(dict(owner(1),nativeOwnerThread=0),owner(10))['available'])
    def test_platform_conserves_actual_arrivals_vs_dispatch_and_queue_high_water(self):
        before=dict(schemaVersion=1,identity='p',nativeOwnerThread=10,nativeWindowHandle=11,readStartTick=0,readEndTick=1,pumpStarted=1,pumpCompleted=1,creationDrains=0,removedMessages=0,quitMessages=0,dispatchStarted=0,dispatchCompleted=0,windowEnqueued=0,windowDequeued=0,inputEnqueued=0,inputDequeued=0,inputDiscarded=0,windowDepth=2,inputDepth=1,windowHighWater=2,inputHighWater=1,initialWindowDepth=2,initialInputDepth=1,overflowed=False,faulted=False)
        after=dict(before,readStartTick=2,readEndTick=3,pumpStarted=2,pumpCompleted=2,removedMessages=2,quitMessages=1,dispatchStarted=1,dispatchCompleted=1,windowEnqueued=3,windowDepth=5,windowHighWater=5)
        result=platform_touching(before,after);self.assertEqual(result['delta']['removedMessages'],2);self.assertEqual(result['dispatchesTouching'],1);self.assertEqual(result['windowHighWater'],5)
        with self.assertRaises(ContractError):platform_touching(before,dict(after,windowDepth=4))
    def test_legacy_payload_policy_cannot_be_relabelled_as_cycle_budget(self):
        value,frames=fixture();result=validate_cycles(value,frames)
        legacy=dict(maximumPerSample=dict(processAllocatedBytes=1000,threadAllocatedBytes=1000))
        with self.assertRaisesRegex(ContractError,'explicitly select'):validate_cycle_budget(result,legacy)
        explicit=dict(legacy,allocationInterval='owner-cycle-v1')
        with self.assertRaisesRegex(ContractError,'unavailable'):validate_cycle_budget(result,explicit) # Platform was absent, never zero.
        synthetic=copy.deepcopy(result)
        for sample in synthetic['samples']:sample['workload']['platform']=dict(available=True)
        validate_cycle_budget(synthetic,explicit) # Schema-only numeric exercise; no real policy review/qualification.
        with self.assertRaisesRegex(ContractError,'exceeds'):validate_cycle_budget(synthetic,dict(allocationInterval='owner-cycle-v1',maximumPerSample=dict(processAllocatedBytes=99,threadAllocatedBytes=1000)))
if __name__=='__main__':unittest.main()
