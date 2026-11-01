"""Native-only retained CPU-source/copy-intent diagnostics. Never GPU content or admission."""
from __future__ import annotations
import base64
import copy
import json
import re
from pathlib import Path
from baseline_contracts import require, parse_json_bytes, sha256_bytes
from stage_diagnostics import bounded_bytes
from world_input_diagnostics import safe
from world_binding_journal import fields, number, digest, same, definitions, reduce_events, implicit

MAX_BYTES=32*1024*1024
BODY_BYTES=31*1024*1024
ENVELOPE_BYTES=1024*1024
RESOURCE_FIELDS='id kind lifetimeAvailable buffer texture implicit descriptor initializationPath staticUsage committed segments'
WRITE_FIELDS='order resourceId kind destinationOffset byteCount elementSize sourceSha256 sourceByteCount transferId textureSource'
TRANSFER_FIELDS='id batchSlot batchGeneration stagingOffset byteCount state signaledFence failureStage'
PROVENANCE_FIELDS='schemaVersion kind complete failure scopeId ownerThreadId deviceGeneration deviceSession resourceGeneration frameIndex baselineWriteWatermark finalWriteWatermark submission completedFence presentedBefore presentedAfter baseline baselineTransfers events'
METADATA_FIELDS='sourceSha producer runtimeFingerprint recipeFingerprint recipeInputBytesBase64 repetition camera profileDomain profileInputBytesBase64 profileFingerprint deviceIdentity bufferCount submission completedFence frameIndex presentedBefore presentedAfter finalColorLayout pixelSha256 legacyFingerprint'


def array(value, cap, name):
    require(type(value) is list and len(value)<=cap,'Missing/oversized '+name)
    return value


def wire_bounds(raw, section):
    require(len(raw)<=MAX_BYTES,'Actual CPU-source sidecar exceeds32MiB')
    text=raw.decode('utf-8-sig'); decoder=json.JSONDecoder(); pos=0; span=None
    def ws(i):
        while i<len(text) and text[i].isspace():i+=1
        return i
    pos=ws(pos);require(text[pos:pos+1]=='{','Sidecar envelope must be object');pos+=1
    while True:
        pos=ws(pos)
        if text[pos:pos+1]=='}':break
        key,pos=decoder.raw_decode(text,pos);pos=ws(pos)
        require(text[pos:pos+1]==':','Malformed original envelope');start=ws(pos+1)
        _,end=decoder.raw_decode(text,start)
        if key==section:
            require(span is None,'Duplicate original body');span=(start,end)
        pos=ws(end)
        if text[pos:pos+1]=='}':break
        require(text[pos:pos+1]==',','Malformed original delimiter');pos+=1
    require(span is not None,'Original body missing')
    body=len(text[span[0]:span[1]].encode('utf-8'))
    require(body<=BODY_BYTES and len(raw)-body+4<=ENVELOPE_BYTES,'Actual body/envelope exceeds reserved31MiB/1MiB')


def load_reference(root, ref):
    fields(ref,'file sha256 bytes schemaVersion','original companion reference')
    require(type(ref['schemaVersion']) is int and ref['schemaVersion']==1,'Unknown selected reference version')
    name=ref['file'];require(type(name) is str and name not in ('','.','..') and not any(c in name for c in '/\\:'),'Original sibling sidecar required')
    raw=bounded_bytes(safe(root,name),MAX_BYTES)
    require(number(ref['bytes'],'sidecar bytes',1,MAX_BYTES)==len(raw) and digest(ref['sha256'])==sha256_bytes(raw),'Original sidecar length/hash differs')
    return parse_json_bytes(raw),raw


def transfer(value):
    fields(value,TRANSFER_FIELDS,'transfer')
    number(value['id'],'transfer identity',1);number(value['batchSlot'],'upload slot',0,3)
    number(value['batchGeneration'],'batch generation',1);number(value['stagingOffset'],'staging offset')
    number(value['byteCount'],'transfer byte count',1,128*1024*1024)
    require(value['state'] in ('queued','execute-returned','signaled','failed'),'Unknown upload state')
    if value['state']=='signaled':number(value['signaledFence'],'actual signaled fence',1,2**64-2)
    else:require(value['signaledFence'] is None,'Unsignaled transfer cannot invent fence')
    require(value['failureStage'] is None or type(value['failureStage']) is str and 0<len(value['failureStage'])<=128,'Invalid failure stage')
    require(value['state']!='failed' and value['failureStage'] is None,'Failed transfer cannot produce complete evidence')


def write(value, resource, transfers):
    fields(value,WRITE_FIELDS,'successful source write')
    number(value['order'],'write ordinal',1);require(type(value['resourceId']) is int and value['resourceId']==resource['id'],'Write resource identity differs')
    require(value['kind'] in ('upload-set-data','initialized-static','texture-mip','implicit-shadow-frame','implicit-buffered-constants'),'Unknown source-write kind')
    offset=number(value['destinationOffset'],'destination offset');length=number(value['byteCount'],'write bytes',1,128*1024*1024)
    source=number(value['sourceByteCount'],'private source bytes',length,128*1024*1024)
    element=number(value['elementSize'],'element size',1,2**31-1)
    require(length%element==0 and source%element==0,'Source element shape differs');digest(value['sourceSha256'])
    if value['transferId'] is not None:
        tid=number(value['transferId'],'source transfer',1)
        require(tid in transfers,'Source transfer unavailable')
    if resource['buffer'] is not None:require(offset+length<=resource['buffer']['sizeInBytes'],'Source write escapes actual buffer')
    require(source==length,'Whole successful source length differs; slicing is represented separately')
    kind=value['kind']
    if kind=='initialized-static':
        require(resource['kind']=='buffer' and resource['buffer']['memory']=='GpuLocal' and value['transferId'] is not None and element==1 and offset==0 and length==resource['buffer']['sizeInBytes'],
            'Initialized static source lost actual full GPU-local copy association')
        require(transfers[value['transferId']]['byteCount']==length,'Static transfer size differs from initializer')
    elif kind=='upload-set-data':
        require(resource['kind']=='buffer' and resource['buffer']['memory']=='Upload' and value['transferId'] is None,'Upload SetData cannot borrow a GPU-local/copy source')
    elif kind in ('implicit-shadow-frame','implicit-buffered-constants'):
        require(resource['kind']=='implicit-constant-arena' and value['transferId'] is None and element==4,'Implicit CPU arena source kind differs')
        if kind=='implicit-shadow-frame':require(offset==0 and length==96,'Shadow-frame native write shape differs')
        else:require(offset>=256 and offset%256==0 and length<=256,'Buffered vertex constant native write shape differs')

    texture=value['textureSource']
    if value['kind']=='texture-mip':
        require(resource['kind']=='texture' and texture is not None and value['transferId'] is not None,'Texture write lacks real source/transfer')
        fields(texture,'mipIndex width height sourceRowPitch logicalRowBytes rowCount stagingLayout','frozen mip source')
        desc=resource['texture'];mip=number(texture['mipIndex'],'mip index',0,desc['mipCount']-1)
        width=max(1,desc['width']//2**mip);height=max(1,desc['height']//2**mip)
        require(type(texture['width']) is int and texture['width']==width and type(texture['height']) is int and texture['height']==height,'Frozen mip dimensions differ from immutable description')
        row=max(1,(width+3)//4)*16 if desc['format']=='Bc7Unorm' else width*(1 if desc['format']=='R8Unorm' else 4)
        rows=max(1,(height+3)//4) if desc['format']=='Bc7Unorm' else height
        require(type(texture['logicalRowBytes']) is int and texture['logicalRowBytes']==row and type(texture['rowCount']) is int and texture['rowCount']==rows,'Canonical logical mip shape differs')
        number(texture['logicalRowBytes'],'logical row bytes',1,2**31-1);number(texture['rowCount'],'logical row count',1,2**31-1)
        number(texture['sourceRowPitch'],'original pitch',row,2**31-1)
        require(offset==0 and length==source==row*rows and element==1,'Canonical mip source size differs')
        layout=texture['stagingLayout'];fields(layout,'width height rowPitch offset format','native staging footprint')
        require(type(layout['width']) is int and layout['width']==width and type(layout['height']) is int and layout['height']==height,'Native footprint dimensions differ')
        pitch=number(layout['rowPitch'],'native pitch',row,2**31-1);number(layout['offset'],'native source offset')
        require(pitch%256==0 and layout['offset']%512==0,'Native footprint alignment differs')
        expected_format={'Rgba8Unorm':'R8G8B8A8_UNorm','R8Unorm':'R8_UNorm','Bc7Unorm':'BC7_UNorm'}[desc['format']] + ('_SRgb' if desc['colorSpace']=='Srgb' else '')
        require(layout['format']==expected_format,'Native mip format differs from actual immutable texture')
        t=transfers[value['transferId']]
        require(t['stagingOffset']<=layout['offset'] and layout['offset']+(rows-1)*pitch+row<=t['stagingOffset']+t['byteCount'],'Transfer does not enclose original native mip footprint')
    else:require(texture is None and resource['kind']!='texture','Nontexture write has texture metadata')


def resource(value, transfers):
    fields(value,RESOURCE_FIELDS,'tracked source resource')
    key=number(value['id'],'source resource identity',1)
    kind=value['kind'];require(kind in ('buffer','texture','implicit-constant-arena'),'Unknown source resource kind')
    require(type(value['lifetimeAvailable']) is bool and value['lifetimeAvailable']==(kind!='implicit-constant-arena'),'Do not invent implicit Lifetime')
    require(type(value['committed']) is bool,'Invalid commit state')
    require(type(value['initializationPath']) is str and 0<len(value['initializationPath'])<=128,'Initialization path unavailable')
    require(value['staticUsage'] in (None,'Vertex','Index'),'Unknown initialized usage')
    if kind=='implicit-constant-arena':
        require(same(value['buffer'],dict(sizeInBytes=4096*256,memory='Upload')) and value['texture'] is None and value['descriptor'] is None,'Implicit arena shape differs')
        i=value['implicit'];fields(i,'kind resourceGeneration frameIndex descriptor rootParameter textureRegister constantByteOffset textureRootParameter','implicit arena witness')
        require(i['kind']=='shadow-and-vertex-constant-arena' and all(i[k] is None for k in ('descriptor','rootParameter','textureRegister','textureRootParameter')) and type(i['constantByteOffset']) is int and i['constantByteOffset']==0,'Implicit witness invented fields')
        number(i['resourceGeneration'],'implicit epoch',1);number(i['frameIndex'],'implicit slot',0,15)
    else:
        require(value['implicit'] is None,'Explicit resource cannot borrow implicit identity')
        definition=dict(id=key,kind=kind,buffer=value['buffer'],texture=value['texture'],descriptor=value['descriptor'] if kind=='texture' else None,pipeline=None)
        # Uncommitted textures have no descriptor until the real SRV publication.
        if kind=='texture' and definition['descriptor'] is None:
            require(value['committed'] is False,'Committed texture lacks descriptor');definition['descriptor']=0
        definitions([definition])
        require(kind=='texture' or value['descriptor'] is None,'Buffer cannot borrow descriptor')
    prior={};segments=array(value['segments'],64,'source segments')
    for segment in segments:
        fields(segment,'start length sourceOffset write','source segment')
        start=number(segment['start'],'segment start');length=number(segment['length'],'segment bytes',1)
        source_offset=number(segment['sourceOffset'],'whole-source slice offset')
        w=segment['write'];write(w,value,transfers)
        require(start>=w['destinationOffset'] and start+length<=w['destinationOffset']+w['byteCount'] and source_offset==start-w['destinationOffset'] and source_offset+length<=w['sourceByteCount'],'Segment is not a slice of actual successful whole source')
        mip=w['textureSource']['mipIndex'] if w['textureSource'] is not None else None
        require(start>=prior.get(mip,0),'Latest source segments overlap/regress');prior[mip]=start+length
    if kind=='texture' and value['committed']:
        desc=value['texture']
        for mip in range(desc['mipCount']):
            width=max(1,desc['width']//2**mip);height=max(1,desc['height']//2**mip)
            row=max(1,(width+3)//4)*16 if desc['format']=='Bc7Unorm' else width*(1 if desc['format']=='R8Unorm' else 4)
            rows=max(1,(height+3)//4) if desc['format']=='Bc7Unorm' else height
            pieces=[s for s in segments if s['write']['textureSource'] is not None and s['write']['textureSource']['mipIndex']==mip]
            require(len(pieces)==1 and pieces[0]['start']==0 and pieces[0]['length']==row*rows,'Committed immutable texture lacks its full successful canonical mip source')
    return key


def apply_write(state,w,transfers):
    rid=w['resourceId'];require(rid in state,'Write names missing resource');r=state[rid];write(w,r,transfers)
    start=w['destinationOffset'];end=start+w['byteCount'];mip=w['textureSource']['mipIndex'] if w['textureSource'] is not None else None
    rows=[]
    for old in r['segments']:
        old_mip=old['write']['textureSource']['mipIndex'] if old['write']['textureSource'] is not None else None
        a,b=old['start'],old['start']+old['length']
        if old_mip!=mip or b<=start or a>=end:rows.append(old);continue
        if a<start:rows.append(dict(old,length=start-a))
        if b>end:rows.append(dict(old,start=end,length=b-end,sourceOffset=old['sourceOffset']+end-a))
    rows.append(dict(start=start,length=w['byteCount'],sourceOffset=0,write=w))
    r['segments']=sorted(rows,key=lambda x:((x['write']['textureSource']['mipIndex'] if x['write']['textureSource'] else -1),x['start']))
    resource(r,transfers)


def association(value, state, watermark):
    fields(value,'role slot journalIdentity resourceId offset length mipIndex sourceWriteOrders unknownRanges','actual draw association')
    role=value['role'];require(role in ('vertex','index','texture','buffered-constants','shadow-frame'),'Unknown source association')
    rid=number(value['resourceId'],'draw source identity',1);require(rid in state and state[rid]['committed'] is True,'Uncommitted/retired/missing draw source')
    r=state[rid];offset=number(value['offset'],'draw view offset');length=number(value['length'],'draw view bytes')
    mip=value['mipIndex']
    require(r['kind']==('texture' if role=='texture' else 'implicit-constant-arena' if role in ('buffered-constants','shadow-frame') else 'buffer'),'Association resource kind differs')
    if role=='texture':number(mip,'draw mip',0,r['texture']['mipCount']-1)
    else:require(mip is None and r['buffer'] is not None and offset+length<=r['buffer']['sizeInBytes'],'Buffer view escapes source')
    require(r['kind']==('texture' if role=='texture' else 'implicit-constant-arena' if role in ('buffered-constants','shadow-frame') else 'buffer'),'Association resource kind differs')
    if role in ('vertex','texture'):number(value['slot'],'binding slot',0,31 if role=='vertex' else 15)
    else:require(value['slot'] is None,'Unexpected source slot')
    if role in ('vertex','index','texture'):number(value['journalIdentity'],'journal identity',1)
    else:require(value['journalIdentity'] is None,'Implicit arena has no fake journal Lifetime')
    segments=[s for s in r['segments'] if (s['write']['textureSource']['mipIndex'] if s['write']['textureSource'] else None)==mip]
    if role=='texture':require(offset==0 and length==max((s['start']+s['length'] for s in segments),default=0),'Texture association differs from tracked logical extent')
    cursor=offset;orders=[];holes=[]
    for s in segments:
        if s['start']>=offset+length or s['start']+s['length']<=offset:continue
        if cursor<s['start']:holes.append(dict(start=cursor,length=s['start']-cursor))
        cursor=max(cursor,min(offset+length,s['start']+s['length']))
        if s['write']['order'] not in orders:orders.append(s['write']['order'])
    if cursor<offset+length:holes.append(dict(start=cursor,length=offset+length-cursor))
    require(all(o<=watermark for o in orders) and same(orders,value['sourceWriteOrders']) and same(holes,value['unknownRanges']),'Source association omits/relabels known writes or unknown holes')
    return r


def validate_provenance(value,journal):
    fields(value,PROVENANCE_FIELDS,'CPU source provenance')
    require(type(value['schemaVersion']) is int and value['schemaVersion']==1 and value['kind']=='bound-input-cpu-source-provenance' and value['complete'] is True and value['failure'] is None,'Incomplete/unknown CPU-source record')
    require(type(value['scopeId']) is str and re.fullmatch('[0-9a-f]{32}',value['scopeId']),'Actual scope identity missing')
    number(value['ownerThreadId'],'managed owner',1,2**31-1)
    for k in ('deviceGeneration','resourceGeneration','deviceSession','frameIndex','submission','completedFence','presentedBefore','presentedAfter'):
        require(same(value[k],journal[k]),'CPU-source/journal completion tuple differs: '+k)
    defs,draws=reduce_events(journal)
    first=number(value['baselineWriteWatermark'],'baseline write ordinal');last=number(value['finalWriteWatermark'],'final write ordinal',first)
    transfers={}
    for t in array(value['baselineTransfers'],32768,'baseline transfers'):
        transfer(t);require(t['id'] not in transfers,'Duplicate transfer identity');transfers[t['id']]=copy.deepcopy(t)
    resources={};known_writes={}
    def remember_write(w):
        order=w['order'];require(order not in known_writes or same(known_writes[order],w),'Same write ordinal changed source/destination')
        known_writes[order]=copy.deepcopy(w)
    for r in array(value['baseline'],4096,'baseline source map'):
        rid=resource(r,transfers);require(rid not in resources,'Duplicate source identity');resources[rid]=copy.deepcopy(r)
        for s in r['segments']:
            require(s['write']['order']<=first,'Future write in baseline');remember_write(s['write'])
    def map_bounds():
        require(len(resources)<=4096 and sum(len(r['segments']) for r in resources.values())<=32768,'Logical source map structural bound exceeded')
        # The original sidecar raw-byte limit is checked separately; this is logical metadata accounting.
        require(len(json.dumps(list(resources.values()),separators=(',',':'),ensure_ascii=True).encode())<=32*1024*1024,'Logical source metadata bound exceeded')
    map_bounds();ordinal=first;observed=[];semantic_draws=[];identities={};seen_resources=set(resources);referenced_transfers=set();later_overlaps=[]
    for seq,event in enumerate(array(value['events'],131072,'capture source events')):
        fields(event,'sequence kind write transfer draw resourceId resource','CPU source event')
        require(type(event['sequence']) is int and event['sequence']==seq,'CPU-source event gap/replay')
        kind=event['kind'];active={'sequence','kind'}
        if kind in ('resource-created','resource-committed'):
            active|={'resourceId','resource'};r=event['resource'];rid=resource(r,transfers)
            require(type(event['resourceId']) is int and event['resourceId']==rid,'Resource event identity mismatch')
            if kind=='resource-created':
                require(rid not in seen_resources and not r['segments'],'Reused resource or invented initial source');seen_resources.add(rid)
            else:
                require(rid in resources and same(resources[rid]['segments'],r['segments']),'Commit changed original source bytes')
                require(all(same(resources[rid][k],r[k]) for k in ('id','kind','lifetimeAvailable','buffer','texture','implicit')),'Commit changed resource description')
            resources[rid]=copy.deepcopy(r)
        elif kind=='resource-retired':
            active.add('resourceId');rid=number(event['resourceId'],'retired source',1);require(rid in resources,'Retired source missing');del resources[rid]
        elif kind=='upload-state':
            active.add('transfer');t=event['transfer'];transfer(t);old=transfers.get(t['id'])
            if old is None:require(t['state']=='queued','New transfer was not queued')
            else:
                require(all(same(old[k],t[k]) for k in ('id','batchSlot','batchGeneration','stagingOffset','byteCount')),'Transfer identity/placement changed')
                allowed={'queued':('execute-returned',),'execute-returned':('signaled',),'signaled':()}
                require(t['state'] in allowed[old['state']],'Upload state skipped/replayed/regressed')
            transfers[t['id']]=copy.deepcopy(t)
        elif kind=='source-write':
            active.add('write');w=event['write'];require(type(w) is dict and type(w.get('order')) is int and w['order']==ordinal+1,'Source write ordinal gap/replay')
            write(w,resources[w['resourceId']],transfers)
            mip=w['textureSource']['mipIndex'] if w['textureSource'] is not None else None
            for previous_draw in observed:
                for binding in previous_draw['bindings']:
                    if binding['resourceId']!=w['resourceId'] or binding['mipIndex']!=mip:continue
                    start=max(binding['offset'],w['destinationOffset']);end=min(binding['offset']+binding['length'],w['destinationOffset']+w['byteCount'])
                    if start>=end:continue
                    require(len(later_overlaps)<131072,'Derived later-write overlap report exceeds bounded diagnostic capacity')
                    later_overlaps.append(dict(drawOrdinal=previous_draw['journalDrawOrdinal'],role=binding['role'],slot=binding['slot'],
                        journalIdentity=binding['journalIdentity'],resourceId=w['resourceId'],mipIndex=mip,writeOrder=w['order'],
                        destinationOffset=w['destinationOffset'],byteCount=w['byteCount'],overlapStart=start,overlapLength=end-start,
                        sourceOffset=start-w['destinationOffset'],sourceSha256=digest(w['sourceSha256']),sourceByteCount=w['sourceByteCount'],
                        transferId=w['transferId']))
            apply_write(resources,w,transfers);ordinal+=1;remember_write(w)
        elif kind=='world-draw':
            active.add('draw');d=event['draw'];fields(d,'journalDrawOrdinal writeWatermark rootCommandSha256 bindings','draw source snapshot')
            require(type(d['journalDrawOrdinal']) is int and d['journalDrawOrdinal']==len(observed)<len(draws) and type(d['writeWatermark']) is int and d['writeWatermark']==ordinal,'Draw/source watermark differs')
            original=draws[len(observed)];s=original['state'];expected=[]
            for slot,v in sorted(s['vertices'].items(),key=lambda p:int(p[0])):expected.append(('vertex',int(slot),v['identityId'],v['offset'],v['rangeBytes'],None))
            if s['index'] is not None:
                v=s['index'];expected.append(('index',None,v['identityId'],v['offset'],v['rangeBytes'],None))
            for slot,jid in sorted(s['textures'].items(),key=lambda p:int(p[0])):
                for mip in range(defs[jid]['texture']['mipCount']):expected.append(('texture',int(slot),jid,0,None,mip))
            if s['constants'] is not None and s['constants']['mode']=='buffered':
                c=s['constants'];expected.append(('buffered-constants',None,None,c['implicit']['constantByteOffset'],len(base64.b64decode(c['bytes'],validate=True)),None))
            if s['shadow'] is not None:expected.append(('shadow-frame',None,None,0,96,None))
            require(type(d['bindings']) is list and len(d['bindings'])==len(expected)<=32+1+16*32+2,'Draw source binding count differs')
            root=s['constants']['sha256'] if s['constants'] is not None and s['constants']['mode']=='root' else None
            require((d['rootCommandSha256'] is None and root is None) or digest(d['rootCommandSha256'])==root,'Private root command hash differs')
            semantic_bindings=[]
            for a,e in zip(d['bindings'],expected):
                r=association(a,resources,ordinal)
                require(same([a['role'],a['slot'],a['journalIdentity'],a['offset'],a['mipIndex']],[e[0],e[1],e[2],e[3],e[5]]) and (e[4] is None or type(a['length']) is int and a['length']==e[4]),'Actual binding/source destination join differs')
                if e[2] is not None:
                    jid=e[2];rid=r['id'];require(jid not in identities or identities[jid]==rid,'Journal Lifetime changed CPU resource association');identities[jid]=rid
                    require(same(r['texture'] if e[0]=='texture' else r['buffer'],defs[jid]['texture'] if e[0]=='texture' else defs[jid]['buffer']),'Journal resource description differs')
                    if e[0]=='texture':require(r['descriptor']==defs[jid]['descriptor'],'Actual texture descriptor differs')
                else:
                    i=r['implicit'];require(i['resourceGeneration']==value['resourceGeneration'] and i['frameIndex']==value['frameIndex'],'Implicit arena actual epoch/slot differs')
                    sha=s['constants']['sha256'] if e[0]=='buffered-constants' else s['shadowFrame']
                    require(len(a['sourceWriteOrders'])==1 and not a['unknownRanges'],'Actual implicit command bytes unavailable')
                    w=known_writes[a['sourceWriteOrders'][0]];require(digest(w['sourceSha256'])==sha and w['destinationOffset']==a['offset'] and w['byteCount']==a['length'],'Implicit source bytes differ from actual journal command')
                for order in a['sourceWriteOrders']:
                    w=known_writes[order]
                    if w['transferId'] is not None:
                        require(transfers[w['transferId']]['state']=='signaled','UseResource must flush referenced source transfer before actual binding/draw')
                        referenced_transfers.add(w['transferId'])
                pieces=[]
                for segment in r['segments']:
                    w=segment['write'];mip=w['textureSource']['mipIndex'] if w['textureSource'] else None
                    if mip!=a['mipIndex'] or segment['start']>=a['offset']+a['length'] or segment['start']+segment['length']<=a['offset']:continue
                    pieces.append(dict(start=segment['start'],length=segment['length'],sourceOffset=segment['sourceOffset'],
                        sourceSha256=digest(w['sourceSha256']),sourceByteCount=w['sourceByteCount'],kind=w['kind'],elementSize=w['elementSize']))
                semantic_bindings.append(dict(role=a['role'],slot=a['slot'],offset=a['offset'],length=a['length'],mipIndex=a['mipIndex'],
                    buffer=r['buffer'],texture=r['texture'],pieces=pieces,unknownRanges=a['unknownRanges']))
            semantic_draws.append(dict(journalDrawOrdinal=d['journalDrawOrdinal'],rootCommandSha256=d['rootCommandSha256'],bindings=semantic_bindings))
            observed.append(copy.deepcopy(d))
        else:require(False,'Unknown CPU-source event')
        require(all(event[k] is None for k in set(event)-active),'Unexpected CPU-source event payload')
        map_bounds()
    require(ordinal==last and len(observed)==len(draws),'Final source watermark/draw count differs')
    for tid in referenced_transfers:
        t=transfers[tid]
        require(t['state']=='signaled' and t['signaledFence']<journal['submission']['submittedFence'] and t['signaledFence']<=journal['completedFence'],
            'World source transfer did not signal before the final completed frame fence')
    return dict(resources=resources,transfers=transfers,draws=observed,semanticDraws=semantic_draws,laterWriteOverlaps=later_overlaps,journalDraws=draws,definitions=defs,writes=known_writes,identityAssociations=identities)


def read_capture(root, producer_reference, original_row=None, required_version=1):
    require(type(required_version) is int and required_version==1,'Selected CPU-source version must be explicit1')
    value,raw=load_reference(root,producer_reference);wire_bounds(raw,'provenance')
    fields(value,'schemaVersion kind qualified metadata journalReference provenance scope','CPU-source sidecar')
    require(type(value['schemaVersion']) is int and value['schemaVersion']==1 and value['kind']=='reference-bound-input-cpu-provenance' and value['qualified'] is False,'Wrong selected CPU-source sidecar')
    require(type(value['scope']) is str and 0<len(value['scope'])<=1024,'CPU-intent limitation missing')
    m=value['metadata'];fields(m,METADATA_FIELDS,'original native capture metadata')
    require(type(m['sourceSha']) is str and re.fullmatch('[0-9a-f]{40}',m['sourceSha']) and m['producer']=='native-reference','Unknown original producer source')
    digest(m['runtimeFingerprint']);digest(m['pixelSha256']);digest(m['legacyFingerprint']);number(m['repetition'],'repetition',0,127)
    require(m['profileDomain']=='effective-quality-profile','Quality and full composition hashes are distinct domains')
    for prefix in ('recipe','profile'):
        b=base64.b64decode(m[prefix+'InputBytesBase64'],validate=True)
        require(sha256_bytes(b)==digest(m[prefix+'Fingerprint']),'Original '+prefix+' bytes/hash differ');parse_json_bytes(b)
    require(type(m['bufferCount']) is int and m['bufferCount']==3,'Actual buffer count differs')
    number(m['frameIndex'],'captured slot',0,2)
    s=m['submission'];fields(s,'submission submittedFence generation resourceGeneration','actual capture submission')
    for k in s:number(s[k],k,1,2**64-2)
    require(number(m['completedFence'],'completed fence',1,2**64-2)>=s['submittedFence'],'Capture GPU fence incomplete')
    require(number(m['presentedAfter'],'present count',1)==number(m['presentedBefore'],'prior present')+1,'Capture Present did not advance exactly once')
    device=m['deviceIdentity'];require(type(device) is dict and type(device.get('generation')) is int and device['generation']==s['generation'] and type(device.get('sessionId')) is str and re.fullmatch('[0-9a-f]{32}',device['sessionId']),'Capture device identity differs')
    fields(device,'adapterLuidHex nodeCount queueNodeMask effectiveNodeMask generation processId processStartedFileTimeUtc sessionId schemaVersion source','actual native device identity')
    require(type(device['schemaVersion']) is int and device['schemaVersion']==1 and type(device['source']) is str and 0<len(device['source'])<=512,'Actual native identity contract unavailable')
    require(type(device['adapterLuidHex']) is str and re.fullmatch('[0-9a-fA-F]{16}',device['adapterLuidHex']),'Actual adapter LUID unavailable')
    number(device['nodeCount'],'node count',1,2**32-1);number(device['queueNodeMask'],'raw queue mask',0,2**32-1);number(device['effectiveNodeMask'],'effective queue mask',1,2**32-1)
    number(device.get('processId'),'actual process',1,2**31-1);number(device.get('processStartedFileTimeUtc'),'actual process start',1)
    from baseline_proposals import camera
    camera(m['camera'])
    layout=m['finalColorLayout'];fields(layout,'width height rowPitch offset format','FinalColor layout')
    width=number(layout['width'],'output width',1,8192);height=number(layout['height'],'output height',1,8192)
    pitch=number(layout['rowPitch'],'readback row pitch',width*4,2**31-1);number(layout['offset'],'readback placed offset')
    require(layout['format']=='R8G8B8A8_UNorm' and pitch%256==0 and layout['offset']%512==0,'Actual FinalColor footprint differs')
    recipe=parse_json_bytes(base64.b64decode(m['recipeInputBytesBase64'],validate=True));profile=parse_json_bytes(base64.b64decode(m['profileInputBytesBase64'],validate=True))
    require(same(recipe.get('display'),dict(width=width,height=height)) and same(recipe.get('settings'),profile.get('requestedSettings')) and digest(recipe.get('runtimeFingerprint'))==digest(m['runtimeFingerprint']),'Original recipe/output/profile/runtime content differs')
    journal,jraw=load_reference(root,value['journalReference']);wire_bounds(jraw,'journal')
    fields(journal,'schemaVersion kind qualified metadata journal','FinalColor journal sidecar')
    require(type(journal['schemaVersion']) is int and journal['schemaVersion']==1 and journal['kind']=='final-color-world-binding-journal' and journal['qualified'] is False and same(journal['metadata'],m),'Original separate journal metadata differs')
    j=journal['journal']
    for k in ('submission','completedFence','frameIndex','presentedBefore','presentedAfter'):require(same(j[k],m[k]),'Journal original capture tuple differs')
    require(same(j['deviceGeneration'],s['generation']) and same(j['resourceGeneration'],s['resourceGeneration']) and same(j['deviceSession'],device['sessionId']),'Journal original device/resource identity differs')
    if original_row is not None:
        require(type(original_row) is dict and same(original_row.get('boundInputProvenance'),producer_reference),'Original producer reference differs')
        for a,b in (('repetition','repetition'),('frameIndex','frameIndex'),('actualSubmission','submission'),('pixelSha256','pixelSha256'),('fingerprint','legacyFingerprint')):
            require(same(original_row.get(a),m[b]),'Original producer capture row differs: '+a)
        require(all(original_row.get(k) is None for k in ('stageDiagnostics','worldInputDiagnostics','shadowFile','shadowLayout','shadowFingerprint')),'FinalColor CPU-source route cannot borrow stage/inspector/shadow exports')
    result=validate_provenance(value['provenance'],j)
    return dict(available=True,status='complete-cpu-source-intent',qualified=False,record=value,journal=journal,sha256=sha256_bytes(raw),**result)


def validate_series(root, references, original_rows):
    require(type(references) is list and type(original_rows) is list and len(references)==len(original_rows)==8,'All eight original CPU-source references required')
    loaded=[read_capture(root,r,row) for r,row in zip(references,original_rows)]
    previous=None;comparisons=[];by_slot={};immutable_resources={};immutable_writes={};immutable_journal_definitions={};identity_associations={}
    for i,item in enumerate(loaded):
        m=item['record']['metadata'];p=item['record']['provenance']
        require(type(m['repetition']) is int and m['repetition']==i,'Capture repetition missing/replayed')
        if previous is not None:
            pm=previous['record']['metadata'];pp=previous['record']['provenance']
            require(all(same(m[k],pm[k]) for k in ('sourceSha','producer','runtimeFingerprint','recipeFingerprint','recipeInputBytesBase64','camera','profileInputBytesBase64','deviceIdentity')),'Own-lane immutable capture inputs changed')
            require(p['scopeId']==pp['scopeId'] and p['ownerThreadId']==pp['ownerThreadId'] and p['baselineWriteWatermark']>=pp['finalWriteWatermark'],'Own source scope/owner/write chronology changed')
            require(m['submission']['submission']==pm['submission']['submission']+1 and m['submission']['submittedFence']>pm['submission']['submittedFence'] and m['submission']['resourceGeneration']==pm['submission']['resourceGeneration'] and m['presentedBefore']==pm['presentedAfter'],'Original capture submission/fence/Present chronology differs')
        for order,write_value in item['writes'].items():
            require(order not in immutable_writes or same(immutable_writes[order],write_value),'Same global source write ordinal changed immutable source/destination')
            immutable_writes[order]=write_value
        for jid,definition in item['definitions'].items():
            require(jid not in immutable_journal_definitions or same(immutable_journal_definitions[jid],definition),'Same journal Lifetime identity changed immutable definition')
            immutable_journal_definitions[jid]=definition
        for jid,rid in item['identityAssociations'].items():
            require(jid not in identity_associations or identity_associations[jid]==rid,'Same journal Lifetime identity changed source resource association')
            identity_associations[jid]=rid
        records=p['baseline']+[event['resource'] for event in p['events'] if event['kind']=='resource-created']
        for resource_value in records:
            rid=resource_value['id'];description={k:resource_value[k] for k in ('id','kind','lifetimeAvailable','buffer','texture','implicit')}
            require(rid not in immutable_resources or same(immutable_resources[rid],description),'Same process-local source identity changed immutable description')
            immutable_resources[rid]=description
        prior_slot=by_slot.get(m['frameIndex'])
        comparisons.append(dict(repetition=i,frameIndex=m['frameIndex'],submission=m['submission'],
            semanticSourceIntentEqualToFirst=same(item['semanticDraws'],loaded[0]['semanticDraws']),
            laterWriteOverlapIntentEqualToFirst=same(
                [{k:v for k,v in overlap.items() if k not in ('journalIdentity','resourceId','writeOrder','transferId')} for overlap in item['laterWriteOverlaps']],
                [{k:v for k,v in overlap.items() if k not in ('journalIdentity','resourceId','writeOrder','transferId')} for overlap in loaded[0]['laterWriteOverlaps']]),
            sameSlotLocalAssociationsEqualToPrevious=None if prior_slot is None else same(
                [[a['resourceId'] for a in d['bindings']] for d in item['draws']],
                [[a['resourceId'] for a in d['bindings']] for d in prior_slot['draws']])))
        by_slot[m['frameIndex']]=item;previous=item
    return dict(available=True,status='complete-eight-cpu-source-intent',qualified=False,wave2Ready=False,captures=loaded,comparisons=comparisons,
        scope='Diagnostic source/destination intent differences only. Slot rotation and local identity differences are retained; no cause or pixel acceptance inferred.')
