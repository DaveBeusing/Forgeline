"""Additive owner-cycle/workload v1 interpretation. Never a numeric policy or baseline approval."""
from datetime import timedelta
from baseline_contracts import require,utc

def integer(value,name,minimum=0):
    require(type(value) is int and value>=minimum,'Invalid owner-observation '+name);return value

def interval(value,name):
    require(type(value) is dict,'Missing '+name)
    a=integer(value.get('startTick'),name+' start');b=integer(value.get('endTick'),name+' end',a)
    return a,b

def vector(value,name):
    require(type(value) is dict and set(value)=={'slot'+str(i) for i in range(8)},'Invalid '+name+' vector shape')
    return [integer(value['slot'+str(i)],name) for i in range(8)]

def workload(value):
    require(type(value) is dict,'Missing copied workload snapshot')
    require(type(value.get('schemaVersion')) is int,'Malformed workload schema version')
    if value['schemaVersion']==0:return None
    require(type(value.get('schemaVersion')) is int and value['schemaVersion']==1,'Unknown workload facts version')
    require(type(value.get('compositionSupported')) is bool and value['compositionSupported'],'Unsupported compiled producer composition')
    require(value.get('overflowed') is False and value.get('faulted') is False,'Workload facts overflow/fault')
    require(type(value.get('identity')) is str and value['identity']!='00000000-0000-0000-0000-000000000000','Missing owner identity')
    integer(value.get('nativeOwnerThread'),'native owner');integer(value.get('session'),'owner session')
    a=integer(value.get('readStartTick'),'workload read start');integer(value.get('readEndTick'),'workload read end',a)
    s=vector(value.get('started'),'started');c=vector(value.get('completed'),'completed');f=vector(value.get('inFlight'),'in-flight ordinal')
    events=vector(value.get('events'),'events');ts=vector(value.get('transportStarted'),'transport started');tc=vector(value.get('transportCompleted'),'transport completed')
    require(all(c[i]<=s[i] and (f[i]==0 if c[i]==s[i] else s[i]-c[i]==1 and f[i]==s[i]) and tc[i]<=ts[i] for i in range(8)),'Incoherent owner ordinal/transport envelope')
    depths=[integer(value.get(n),n) for n in ('queued','inFlightMessages','completionDepth')]
    require(integer(value.get('highWater'),'queue high water')>=sum(depths),'Queue occupancy exceeds observed high water')
    integer(value.get('capacity'),'collector/transport capacity')
    return None if value['nativeOwnerThread']==0 else (value,s,c,events,ts,tc)

def touching(before,after):
    a=workload(before);b=workload(after)
    if a is None or b is None:return dict(available=False,reason='Producer not yet bound/observed; not evidence of zero work')
    require(all(before[k]==after[k] and type(before[k]) is type(after[k]) for k in ('identity','session','nativeOwnerThread')) and before['readEndTick']<=after['readStartTick'],'Foreign/regressing owner facts')
    require(all(all(y>=x for x,y in zip(a[i],b[i])) for i in (1,2,3,4,5)),'Owner counters regressed')
    return dict(available=True,touching=[b[1][i]-a[2][i] for i in range(8)],transportTouching=[b[4][i]-a[5][i] for i in range(8)],eventDelta=[b[3][i]-a[3][i] for i in range(8)])

def platform_touching(before,after):
    counters=('pumpStarted','pumpCompleted','creationDrains','removedMessages','quitMessages','dispatchStarted','dispatchCompleted','windowEnqueued','windowDequeued','inputEnqueued','inputDequeued','inputDiscarded','windowHighWater','inputHighWater')
    for value in (before,after):
        require(type(value) is dict,'Missing copied platform facts')
        require(type(value.get('schemaVersion')) is int,'Malformed platform schema version')
        if value['schemaVersion']==0:return dict(available=False,reason='Platform provider not observed; not proof of zero pumps/messages')
        require(type(value.get('schemaVersion')) is int and value['schemaVersion']==1 and value.get('overflowed') is False and value.get('faulted') is False,'Platform facts overflow/fault/version')
        require(type(value.get('identity')) is str and value['identity']!='00000000-0000-0000-0000-000000000000','Missing platform identity')
        integer(value.get('nativeOwnerThread'),'platform native owner',1);integer(value.get('nativeWindowHandle'),'platform HWND',1)
        integer(value.get('readEndTick'),'platform read end',integer(value.get('readStartTick'),'platform read start'))
        for n in counters+('windowDepth','inputDepth','initialWindowDepth','initialInputDepth'):integer(value.get(n),'platform '+n)
        require(value['pumpCompleted']<=value['pumpStarted'] and value['dispatchCompleted']<=value['dispatchStarted'] and value['quitMessages']<=value['removedMessages'],'Platform operation counters incoherent')
        require(value['initialWindowDepth']+value['windowEnqueued']-value['windowDequeued']==value['windowDepth'] and value['initialInputDepth']+value['inputEnqueued']-value['inputDequeued']-value['inputDiscarded']==value['inputDepth'],'Platform queue arrivals/removals do not conserve occupancy')
        require(value['windowHighWater']>=value['windowDepth'] and value['inputHighWater']>=value['inputDepth'],'Platform queue high water differs')
    require(all(before[k]==after[k] and type(before[k]) is type(after[k]) for k in ('identity','nativeOwnerThread','nativeWindowHandle','initialWindowDepth','initialInputDepth')) and before['readEndTick']<=after['readStartTick'],'Foreign platform/counter chronology')
    require(all(after[n]>=before[n] for n in counters),'Platform cumulative counters regressed')
    return dict(available=True,pumpsTouching=after['pumpStarted']-before['pumpCompleted'],dispatchesTouching=after['dispatchStarted']-before['dispatchCompleted'],delta={n:after[n]-before[n] for n in counters if n not in ('windowHighWater','inputHighWater')},windowHighWater=after['windowHighWater'],inputHighWater=after['inputHighWater'])

def legacy_witness(value,ending,expected_session=None,expected_hwnd=None,producer_kind=None):
    before=value['before'];after=value['after'];facts_bracket(before,expected_session,expected_hwnd,producer_kind);facts_bracket(after,expected_session,expected_hwnd,producer_kind);p=interval(value['processRead'],'legacy process read');t=interval(value['threadRead'],'legacy thread read')
    first,last=(t,p) if ending else (p,t)
    require(before['readStartTick']<=before['readEndTick']<=first[0]<=first[1]<=last[0]<=last[1]<=after['readStartTick']<=after['readEndTick'],'Legacy facts do not enclose original ordered process/thread reads')

def facts_bracket(value,expected_session=None,expected_hwnd=None,producer_kind=None):
    a=integer(value.get('readStartTick'),'copied facts start');b=integer(value.get('readEndTick'),'copied facts end',a)
    require(type(value.get('simulationAbsentByComposition')) is bool and type(value.get('platformProviderAvailable')) is bool,'Malformed producer composition facts')
    require(value['simulationAbsentByComposition'] is (producer_kind=='authored-world-fixture'),'Simulation absence flag differs from actual producer kind')
    for name in ('simulation','platform','uiAudio','worldAudio'):
        nested=value.get(name);require(type(nested) is dict and type(nested.get('schemaVersion')) is int,'Malformed nested owner facts')
        if nested['schemaVersion']==0:continue # Unavailable, never an assumed zero-work vector.
        require(nested['schemaVersion']==1,'Unknown nested owner version')
        na=integer(nested.get('readStartTick'),'nested read start');nb=integer(nested.get('readEndTick'),'nested read end',na)
        require(a<=na<=nb<=b,'Nested owner read is not enclosed by actual copied-facts bracket')
        if name=='simulation' and expected_session is not None:require(type(nested.get('session')) is int and nested['session']==expected_session,'Observed simulation session differs from actual producer')
        if name=='uiAudio':require(type(nested.get('session')) is int and nested['session']==0,'UI audio session differs from compiled sessionless composition')
        if name=='worldAudio' and (expected_session is not None or producer_kind=='authored-world-fixture'):require(type(nested.get('session')) is int and nested['session']==(expected_session or 0),'World audio session differs from actual producer')
        if name=='platform' and expected_hwnd is not None:require(type(nested.get('nativeWindowHandle')) is int and nested['nativeWindowHandle']==expected_hwnd,'Observed Platform HWND differs from actual producer')

def witness(value,precise,expected_session=None,expected_hwnd=None,producer_kind=None):
    a=integer(value.get('readStartTick'),'edge start');b=integer(value.get('readEndTick'),'edge end',a)
    before=value['before'];after=value['after'];facts_bracket(before,expected_session,expected_hwnd,producer_kind);facts_bracket(after,expected_session,expected_hwnd,producer_kind)
    require(a<=integer(before['readStartTick'],'facts start')<=integer(before['readEndTick'],'facts end')<=integer(after['readStartTick'],'facts after start')<=integer(after['readEndTick'],'facts after end')<=b,'Facts are not enclosed by edge observation')
    if precise:
        integer(value.get('processAbsolute'),'exact process absolute');integer(value.get('threadAbsolute'),'exact thread absolute')
        p=interval(value['processRead'],'process read');t=interval(value['threadRead'],'thread read')
        require(before['readEndTick']<=p[0]<=p[1]<=t[0]<=t[1]<=after['readStartTick'],'Facts do not enclose BOTH separate counter read brackets')
    else:require(value.get('processAbsolute') is None and value.get('threadAbsolute') is None and interval(value['processRead'],'off process')==(0,0) and interval(value['threadRead'],'off thread')==(0,0),'CollectorOff cannot invent precise counter reads')
    return a,b

def validate_cycles(value,frames,*,expected_session=None,expected_hwnd=None,producer_kind=None,warmups=0,prefix_frames=None):
    require(type(value) is dict and type(value.get('schemaVersion')) is int and value['schemaVersion']==1 and value.get('kind')=='render-owner-cycle-observations','Unknown owner-cycle contract')
    require(value.get('windowSealed') is True and value.get('overflowed') is False and value.get('faulted') is False,'Owner-cycle evidence is incomplete/overflowed/faulted')
    require(type(value.get('preciseCountersAvailable')) is bool and type(value.get('terminalClosed')) is bool,'Missing explicit counter/lifecycle state')
    precise=value['preciseCountersAvailable'];edges=value['edges'];cycles=value['cycles']
    require(type(edges) is list and type(cycles) is list and 0<len(cycles)<=integer(value.get('capacity'),'cycle storage capacity',1)<=65536 and len(edges)==len(cycles)+1,'Incomplete shared-edge storage')
    brackets=[witness(e,precise,expected_session,expected_hwnd,producer_kind) for e in edges]
    require(all(brackets[i][1]<=brackets[i+1][0] for i in range(len(edges)-1)),'Cycle observations overlap/regress')
    integer(warmups,'declared warmups')
    first_payload=next((i for i,row in enumerate(cycles) if row.get('payloadIndex')==0),None)
    require(first_payload is not None,'No measured cycle transition')
    warm_boundary=None
    if warmups:
        if producer_kind=='authored-world-fixture':
            candidates=[i for i,row in enumerate(cycles[:first_payload]) if row.get('fixtureOrdinal')==warmups-1 and row.get('state')=='gameplay' and type(row.get('submittedFence')) is int and row['submittedFence']>0]
            require(cycles[first_payload].get('fixtureOrdinal')==warmups,'First fixture sample differs from declared warm transition')
        else:
            require(type(prefix_frames) is list and prefix_frames and prefix_frames[-1].get('state')=='gameplay','Actual final warmup provenance unavailable')
            original=interval(prefix_frames[-1]['observer']['endProcessCounter'],'original final warmup process')
            candidates=[i for i,row in enumerate(cycles[:first_payload]) if row.get('payloadIndex') is None and row.get('state')=='gameplay' and type(row.get('payloadEnd')) is dict and interval(row['payloadEnd']['processRead'],'warm cycle process')==original]
        require(len(candidates)==1,'Actual declared warm-transition cycle is unavailable/ambiguous')
        warm_boundary=candidates[0]
    measured=[];previous=-1
    for i,row in enumerate(cycles):
        require(type(row['index']) is int and row['index']==i and type(row['startEdge']) is int and row['startEdge']==i and type(row['endEdge']) is int and row['endEdge']==i+1,'Shared previous-END/next-START edge identity differs')
        require(type(row.get('state')) is str and row['state'] not in ('payload-incomplete','fault/incomplete-cycle'),'Incomplete owner iteration')
        if precise:require(all(edges[i+1][name]>=edges[i][name] for name in ('processAbsolute','threadAbsolute')),'Cumulative allocation counter rollback')
        index=row.get('payloadIndex')
        if index is None:continue
        require(type(index) is int and index==len(measured) and row['state']=='gameplay','Measured payload index/state differs')
        require(index<len(frames),'Owner cycles have extra measured payloads')
        frame=frames[index];require(type(frame.get('index')) is int and frame['index']==index,'Original frame index differs')
        original_start=integer(frame.get('startTick'),'original frame start');original_end=integer(frame.get('endTick'),'original frame end',original_start+1)
        require(edges[i]['readEndTick']<=original_start<original_end<=edges[i+1]['readStartTick'],'Original frame timing is outside its actual owner cycle')
        submission=frame.get('submission') if type(frame.get('submission')) is dict else frame
        for name in ('submission','submittedFence','generation','resourceGeneration'):
            require(integer(row.get(name),'cycle '+name,1)==integer(submission.get(name),'source '+name,1),'Actual source frame/submission tuple differs from sealed cycle')
        allocation=None
        if precise:
            begin=row.get('payloadBegin');end=row.get('payloadEnd');require(type(begin) is dict and type(end) is dict,'Missing reused legacy absolute counters')
            legacy_witness(begin,False,expected_session,expected_hwnd,producer_kind);legacy_witness(end,True,expected_session,expected_hwnd,producer_kind)
            require(interval(begin['processRead'],'reused begin process')==interval(frame['observer']['beginProcessCounter'],'original begin process') and interval(end['processRead'],'reused end process')==interval(frame['observer']['endProcessCounter'],'original end process'),'Reused legacy counter brackets differ from original observer')
            require(begin['threadRead']['endTick']<=original_start<=begin['after']['readStartTick'] and end['before']['readEndTick']<=original_end<=end['threadRead']['startTick'],'Original payload endpoints differ from reused legacy witnesses')
            require(edges[i]['readEndTick']<=integer(frame.get('serviceStartTick'),'original service start')<=begin['processRead']['startTick'] and end['processRead']['endTick']<=integer(frame.get('serviceEndTick'),'original service end')<=end['after']['readStartTick'],'Original service interval differs from cycle/counter brackets')
            allocation={}
            for counter,legacy in [('processAbsolute','processAllocatedBytes'),('threadAbsolute','threadAllocatedBytes')]:
                start=edges[i][counter];finish=edges[i+1][counter];pb=integer(begin.get(counter),'payload begin');pe=integer(end.get(counter),'payload end',pb)
                require(start<=pb<=pe<=finish and pe-pb==integer(frame.get(legacy),'original legacy delta'),'Legacy delta/cycle allocation algebra differs')
                allocation[counter]=dict(cycle=finish-start,lead=pb-start,payload=pe-pb,tail=finish-pe)
        else:require(row.get('payloadBegin') is None and row.get('payloadEnd') is None,'OFF cannot carry fake legacy allocations')
        # Include every idle/no-payload cycle between retained measured successes.
        first=previous+1 if previous>=0 else warm_boundary+1 if warm_boundary is not None else i;before=edges[first]['before'];after=edges[i+1]['after']
        work={}
        for owner in ('simulation','uiAudio','worldAudio'):
            if owner=='simulation' and producer_kind=='authored-world-fixture' and before.get('simulationAbsentByComposition') is True and after.get('simulationAbsentByComposition') is True:work[owner]=dict(available=False,absentByComposition=True)
            else:work[owner]=touching(before[owner],after[owner])
        work['platform']=platform_touching(before['platform'],after['platform'])
        bundle_allocations={counter:edges[i+1][counter]-edges[first][counter] for counter in ('processAbsolute','threadAbsolute')} if precise else None
        measured.append(dict(index=index,bundleAllocations=bundle_allocations,bundleReadEndTicks=edges[i+1]['readEndTick']-edges[first]['readEndTick'],cycle=i,bundleStartCycle=first,bundleEndCycle=i,previousMeasuredSuccessAvailable=previous>=0,allocations=allocation,workload=work))
        previous=i
    require(len(measured)==len(frames)>0 and integer(value.get('firstMeasuredCycle'),'first measured cycle')==measured[0]['cycle'] and integer(value.get('lastMeasuredCycle'),'last measured cycle')==measured[-1]['cycle']==len(cycles)-1,'Missing first/final measured cycle seal')
    last=cycles[-1]
    return dict(schemaVersion=1,kind='verified-owner-cycle-observations',samples=measured,
        producerKind=producer_kind,warmupTransitionCycle=warm_boundary,firstTick=edges[measured[0]['bundleStartCycle']]['readStartTick'],lastTick=edges[-1]['readEndTick'],
        lastSubmission={n:last[n] for n in ('submission','submittedFence','generation','resourceGeneration')},
        preciseCountersAvailable=precise,qualified=False,allocationScope='Cycle overlaps legacy payload; never add cycle and payload totals; first bundle has no preceding measured-success anchor')

def cycle_times(clock,frequency,result):
    integer(frequency,'frequency',1);before=integer(clock['beforeTick'],'clock before');after=integer(clock['afterTick'],'clock after',before)
    origin=utc(clock['utc']);return origin+timedelta(seconds=(result['firstTick']-after)/frequency),origin+timedelta(seconds=(result['lastTick']-before)/frequency)


def validate_cycle_budget(result,policy):
    require(policy.get('allocationInterval')=='owner-cycle-v1','Full-cycle budget admission unavailable: a genuinely reviewed allocation policy must explicitly select owner-cycle-v1; legacy payload limits retain their meaning')
    require(result.get('preciseCountersAvailable') is True,'Exact full-cycle process/thread counters unavailable')
    for sample in result['samples']:
        require(all(v.get('available') is True or name=='simulation' and result.get('producerKind')=='authored-world-fixture' and v.get('absentByComposition') is True for name,v in sample['workload'].items()),'Independent owner workload/activation facts unavailable for full-cycle budget evidence')
        for counter,name in [('processAbsolute','processAllocatedBytes'),('threadAbsolute','threadAllocatedBytes')]:
            maximum=integer(policy['maximumPerSample'].get(name),'reviewed cycle '+name)
            require(integer(sample['bundleAllocations'].get(counter),'exact idle-inclusive bundle allocation')<=maximum,'Exact idle-inclusive owner-cycle bundle allocation exceeds explicitly reviewed cycle maximum')
