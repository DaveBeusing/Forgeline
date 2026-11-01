"""Selected late resident-resource snapshots. Never shader-fetched contents or admission."""
from __future__ import annotations
import base64
import json
import math
import re
from pathlib import Path
from baseline_contracts import require, parse_json_bytes, sha256_bytes
from stage_diagnostics import bounded_bytes
from world_input_diagnostics import safe
from world_binding_journal import fields, number, digest, same, definitions, single_value

MAX_BYTES=32*1024*1024
RAW_BYTES=128*1024*1024
META='sourceSha producer runtimeFingerprint recipeFingerprint recipeInputBytesBase64 repetition camera profileDomain profileInputBytesBase64 profileFingerprint observedComposition pixelSha256 legacyFingerprint image qualified'
ORIGINAL='schemaVersion fenceDomain deviceIdentity managedOwnerThreadId frameIndex submission completedFence presentedBefore presentedAfter width height resizeGeneration appliedResizeGeneration'
DEVICE='adapterLuidHex nodeCount queueNodeMask effectiveNodeMask generation processId processStartedFileTimeUtc sessionId schemaVersion source'
MIP='mipIndex width height rowCount rowBytes rowPitch packedOffset paddedBytes canonicalBytes format'
DESCRIPTION='localId semanticName kind lifetimeAvailable buffer texture implicit sourceOffset observedBytes rangeMeaning mips'
EXPECTED='sourceSha runtimeFingerprint recipeFingerprint repetition camera profileFingerprint observedComposition pixelSha256 legacyFingerprint deviceIdentity managedOwnerThreadId frameIndex actualSubmission'


def text(v,name):
    require(type(v) is str and 0<len(v.encode('utf-8'))<=4096,'Missing/oversized '+name)
    return v


def array(v,cap,name):
    require(type(v) is list and len(v)<=cap,'Missing/oversized '+name)
    return v


def sibling(root,name):
    require(type(name) is str and name not in ('','.','..') and not any(c in name for c in '/\\:'),'Exclusive sibling filename required')
    return safe(root,name)


def reference(root,ref,cap):
    fields(ref,'file sha256 bytes schemaVersion','snapshot ref')
    require(type(ref['schemaVersion']) is int and ref['schemaVersion']==1,'Unknown snapshot ref version')
    raw=bounded_bytes(sibling(root,ref['file']),cap)
    require(number(ref['bytes'],'raw size',1,cap)==len(raw) and digest(ref['sha256'])==sha256_bytes(raw),'Original length/hash differs')
    return raw


def bounds(raw):
    # Original encoded spans, not reserialized estimates: body=resources+intent, all else=envelope.
    source=raw.decode('utf-8-sig');decoder=json.JSONDecoder();position=0;spans={}
    def ws(i):
        while i<len(source) and source[i].isspace():i+=1
        return i
    position=ws(position);require(source[position:position+1]=='{','Object required');position+=1
    while True:
        position=ws(position)
        if source[position:position+1]=='}':break
        key,position=decoder.raw_decode(source,position);position=ws(position)
        require(source[position:position+1]==':','Malformed wire');start=ws(position+1);_,end=decoder.raw_decode(source,start)
        require(key not in spans,'Duplicate wire key');spans[key]=(start,end);position=ws(end)
        if source[position:position+1]=='}':break
        require(source[position:position+1]==',','Malformed delimiter');position+=1
    body=sum(len(source[a:b].encode('utf-8')) for k,(a,b) in spans.items() if k in ('resources','intent'))
    require(body<=31*1024*1024 and len(raw)-body+8<=1024*1024,'Original body/envelope capacity exceeded')


def camera(value):
    fields(value,'target yawRadians pitchRadians distance hasPointerPosition pointerPosition','camera')
    for key,axes in [('target','x y z'),('pointerPosition','x y')]:
        fields(value[key],axes,key)
        for n in axes.split():require(single_value(value[key][n]) is not None,'Nonfinite camera vector')
    for n in ('yawRadians','pitchRadians','distance'):require(single_value(value[n]) is not None,'Nonfinite camera scalar')
    require(value['distance']>0 and type(value['hasPointerPosition']) is bool,'Invalid camera')


def receipt(value):
    fields(value,ORIGINAL,'original receipt');d=value['deviceIdentity'];fields(d,DEVICE,'device identity')
    require(type(value['schemaVersion']) is int and value['schemaVersion']==1 and value['fenceDomain']=='original-device-frame-fence','Unknown original domain/version')
    for key in ('generation','processId','processStartedFileTimeUtc','nodeCount'):number(d[key],key,1)
    for key in ('queueNodeMask','effectiveNodeMask'):number(d[key],key,0,2**32-1)
    require(type(d['schemaVersion']) is int and d['schemaVersion']==1 and re.fullmatch('[0-9a-fA-F]{16}',text(d['adapterLuidHex'],'adapter')) and re.fullmatch('[0-9a-fA-F]{32}',text(d['sessionId'],'session')),'Invalid device identity')
    text(d['source'],'device source');number(value['managedOwnerThreadId'],'managed owner',1,2**31-1);number(value['frameIndex'],'captured slot',0,2)
    for key in ('width','height'):number(value[key],key,1,2**31-1)
    s=value['submission'];fields(s,'submission submittedFence generation resourceGeneration','original submission')
    for key in s:number(s[key],key,1,2**64-2)
    completed=number(value['completedFence'],'original completed',1,2**64-2)
    for key in ('presentedBefore','presentedAfter','resizeGeneration','appliedResizeGeneration'):number(value[key],key,0,2**64-2)
    require(s['generation']==d['generation'] and s['resourceGeneration']==value['appliedResizeGeneration']+1 and completed>=s['submittedFence'] and value['presentedAfter']==value['presentedBefore']+1,'Original receipt progression differs')
    require(value['resizeGeneration']==value['appliedResizeGeneration'],'Pending resize not allowed')


def copy_receipt(value,original):
    fields(value,'schemaVersion fenceDomain queueRelationship submittedFence completedFence mayHaveExecuted completionProved','copy receipt')
    require(type(value['schemaVersion']) is int and value['schemaVersion']==1 and type(value['fenceDomain']) is str and value['fenceDomain'].startswith('late-copy-fence-') and value['fenceDomain']!=original['fenceDomain'],'Copy fence domain differs')
    text(value['fenceDomain'],'copy fence domain')
    require(re.fullmatch('late-copy-fence-[0-9a-f]{32}',value['fenceDomain']),'Copy fence domain is not the actual fresh GUID format')
    text(value['queueRelationship'],'queue relationship')
    number(value['submittedFence'],'copy signal',1,2**64-2);number(value['completedFence'],'copy completion',1,2**64-2)
    require(value['mayHaveExecuted'] is True and value['completionProved'] is True and value['completedFence']>=value['submittedFence'],'Copy completion unproved')
    # No numeric comparison to the original device-frame fence domain.


def intent(value,resources):
    fields(value,'terrainChunks mainBatches decorationBatches','late intent');occurrences=0
    require(type(resources) is dict,'Validated resource descriptions required')
    def refs(rows,required):
        nonlocal occurrences
        result={}
        for r in array(rows,131072,'resource references'):
            fields(r,'resourceId role','intent reference');rid=number(r['resourceId'],'resource ID',1,4096);role=text(r['role'],'role')
            require(rid in resources and role not in result,'Dangling resource or duplicate role')
            require(role in required and resources[rid]['kind']==required[role],'Unexpected role or resource kind')
            result[role]=resources[rid];occurrences+=1
        require(set(result)==set(required),'Missing required retained-resource role')
        require(occurrences<=131072,'Intent total references exceed capacity')
        return result
    def buffer_range(resource,offset,length,name):
        number(offset,name+' offset');number(length,name+' bytes',1)
        require(resource['sourceOffset']==0 and offset+length<=resource['observedBytes'],'Range outside snapshotted '+name)
    for index,row in enumerate(array(value['terrainChunks'],4096,'terrain chunks')):
        fields(row,'order chunkIndex chunkX chunkZ boundsMinimum boundsMaximum storedMainLod lodMeaning paletteKey materialKeys indexRanges resources','terrain intent')
        require(number(row['order'],'terrain order',0,4095)==index,'Terrain order differs');number(row['chunkIndex'],'chunk index',0,2**31-1)
        for n in ('chunkX','chunkZ'):number(row[n],n,-2**31,2**31-1)
        number(row['paletteKey'],'palette key',0,2**32-1);text(row['lodMeaning'],'LOD meaning')
        for k in ('boundsMinimum','boundsMaximum'):
            fields(row[k],'x y z',k)
            for n in ('x','y','z'):require(single_value(row[k][n]) is not None,'Invalid bounds')
        require(all(row['boundsMinimum'][k]<=row['boundsMaximum'][k] for k in ('x','y','z')),'Reversed terrain bounds')
        materials=array(row['materialKeys'],32,'material keys')
        for k in materials:text(k,'material key')
        required={'vertex':'buffer','shared-indices':'buffer','control':'texture'}
        for layer in range(len(materials)):
            for suffix in ('base-color','normal','orm','retained-emissive'):required['layer-'+str(layer)+'-'+suffix]='texture'
        bound=refs(row['resources'],required)
        ranges=array(row['indexRanges'],32,'index ranges');require(ranges,'Missing terrain ranges')
        number(row['storedMainLod'],'stored LOD',0,len(ranges)-1)
        for r in ranges:
            fields(r,'firstIndex indexCount step','index range')
            first=number(r['firstIndex'],'first index',0,2**31-1);count=number(r['indexCount'],'index count',1,2**31-1);number(r['step'],'step',1,2**31-1)
            # TerrainRenderer binds the shared IB as ThirtyTwoBit. No index-value decode/fetch inference.
            buffer_range(bound['shared-indices'],first*4,count*4,'terrain shared indices')
    for group in ('mainBatches','decorationBatches'):
        for index,row in enumerate(array(value[group],4096,group)):
            fields(row,'order meshKey materialKey partIndex instanceCount instanceOffset instanceStride vertexStride firstIndex indexCount indexFormat passMeaning resources','batch intent')
            require(number(row['order'],'batch order',0,4095)==index,'Batch order differs')
            for k in ('meshKey','materialKey'):
                if row[k] is not None:text(row[k],k)
            number(row['partIndex'],'partIndex',-2**31,2**31-1)
            offset=number(row['instanceOffset'],'instance offset',0,2**31-1);first=number(row['firstIndex'],'first index',0,2**31-1)
            count=number(row['instanceCount'],'instance count',1,2**31-1);stride=number(row['instanceStride'],'instance stride',1,2**31-1)
            vertex_stride=number(row['vertexStride'],'vertex stride',1,2**31-1);indices=number(row['indexCount'],'index count',1,2**31-1)
            require(stride==112 and row['indexFormat'] in ('SixteenBit','ThirtyTwoBit'),'Actual instance/index format differs')
            text(row['passMeaning'],'pass meaning')
            required={'captured-slot-instance-prefix':'buffer','mesh-vertex':'buffer','mesh-index':'buffer'}
            if row['materialKey'] is not None:
                for role in ('base-color','normal','orm','emissive'):required[role]='texture'
            bound=refs(row['resources'],required)
            buffer_range(bound['captured-slot-instance-prefix'],offset*stride,count*stride,'instance prefix')
            buffer_range(bound['mesh-index'],first*(2 if row['indexFormat']=='SixteenBit' else 4),indices*(2 if row['indexFormat']=='SixteenBit' else 4),'mesh indices')
            buffer_range(bound['mesh-vertex'],0,vertex_stride,'at least one retained vertex')


def read_capture(root,ref,expected,*,snapshot_version=1):
    require(type(snapshot_version) is int,'Selected/historical version must be an exact integer')
    if snapshot_version==0:
        require(ref is None,'Historical mode cannot consume selected snapshot')
        return {'status':'unavailable-historical','qualified':False}
    require(type(snapshot_version) is int and snapshot_version==1,'Unknown selected late snapshot version')
    raw=reference(root,ref,MAX_BYTES);bounds(raw);value=parse_json_bytes(raw)
    fields(value,'schemaVersion kind complete metadata originalFrameReceipt copyReceipt coverage resources intent','snapshot')
    require(type(value['schemaVersion']) is int and value['schemaVersion']==1 and value['kind']=='reference-post-final-color-resident-resources' and value['complete'] is True,'Selected snapshot incomplete/unknown')
    m=value['metadata'];fields(m,META,'snapshot metadata');fields(expected,EXPECTED,'independent original expectations')
    require(m['producer']=='native-reference' and m['qualified'] is False and re.fullmatch('[0-9a-fA-F]{40}',m['sourceSha']),'Foreign producer/source')
    for k in ('runtimeFingerprint','recipeFingerprint','profileFingerprint','pixelSha256','legacyFingerprint'):digest(m[k])
    for k in expected:
        if k not in ('deviceIdentity','managedOwnerThreadId','frameIndex','actualSubmission'):require(same(m[k],expected[k]),'Original metadata differs: '+k)
    number(m['repetition'],'repetition',0,7);camera(m['camera'])
    require(m['profileDomain']=='effective-quality-profile','Quality/full composition hash domains differ')
    try:recipe=base64.b64decode(m['recipeInputBytesBase64'],validate=True);profile=base64.b64decode(m['profileInputBytesBase64'],validate=True)
    except Exception as error:raise ValueError('Malformed original serialized inputs') from error
    require(sha256_bytes(recipe)==digest(m['recipeFingerprint']) and sha256_bytes(profile)==digest(m['profileFingerprint']),'Original input hash differs')
    parsed=parse_json_bytes(recipe);quality=parse_json_bytes(profile)
    require(same(parsed['runtimeFingerprint'],m['runtimeFingerprint']) and same(parsed['settings'],quality['requestedSettings']),'Actual recipe/runtime/quality differs')
    original=value['originalFrameReceipt'];receipt(original);copy_receipt(value['copyReceipt'],original)
    require(same(original['deviceIdentity'],expected['deviceIdentity']) and same(original['managedOwnerThreadId'],expected['managedOwnerThreadId']) and same(original['frameIndex'],expected['frameIndex']) and same(original['submission'],expected['actualSubmission']),'Original source/device/captured slot/submission differs')
    require(parsed['display']['width']==original['width'] and parsed['display']['height']==original['height'],'Recipe raster differs')
    image=m['image'];fields(image,'file sha256 bytes','closed primary image')
    image_raw=bounded_bytes(sibling(root,image['file']),max(1024*1024,original['width']*original['height']*8+1024*1024))
    require(number(image['bytes'],'image bytes',1)==len(image_raw) and digest(image['sha256'])==sha256_bytes(image_raw),'Primary image hash differs')
    from baseline_admission import verify_png
    verify_png(Path(root),{'image':image['file'],'imageSha256':image['sha256'],'pixelSha256':m['pixelSha256']},{'width':original['width'],'height':original['height']})
    scope=value['coverage'];fields(scope,'meaning included excluded unknown','coverage')
    for k in ('meaning','unknown'):text(scope[k],k)
    for k in ('included','excluded'):
        for item in array(scope[k],32,k):text(item,k)
    require(scope['included']==['resident-terrain','final-retained-main-scratch','final-retained-decoration-scratch','captured-slot-constant-arena-used-prefix-if-present'],'Coverage whitelist differs')
    require(scope['excluded']==['render-targets','depth-shadow-targets','root-constants','particles','HUD','skin-caches','inactive-private-caches','earlier-pass-scratch','entity-root-associations'],'Coverage exclusions differ')
    rows=array(value['resources'],4096,'resource roster');require(rows,'Empty declared resource subset');total=0;end=0;mip_count=0;names={};signatures=[];implicit_count=0
    for index,row in enumerate(rows):
        fields(row,'description rawMips','resource row');d=row['description'];fields(d,DESCRIPTION,'resource description')
        require(number(d['localId'],'local ID',1,4096)==index+1,'Resource order/identity differs');text(d['semanticName'],'semantic name');text(d['rangeMeaning'],'range meaning')
        kind=d['kind'];require(kind in ('buffer','texture','implicit-buffer') and type(d['lifetimeAvailable']) is bool and d['lifetimeAvailable']==(kind!='implicit-buffer'),'Resource/lifetime kind differs')
        texture=d['texture'];buffer=d['buffer'];offset=number(d['sourceOffset'],'source offset');observed=number(d['observedBytes'],'observed bytes',1,RAW_BYTES)
        if kind=='texture':
            definitions([{'id':1,'kind':'texture','buffer':None,'texture':texture,'descriptor':0,'pipeline':None}]);require(buffer is None and d['implicit'] is None and offset==0,'Texture payload differs')
        else:
            definitions([{'id':1,'kind':'buffer','buffer':buffer,'texture':None,'descriptor':None,'pipeline':None}]);require(texture is None and offset+observed<=buffer['sizeInBytes'],'Buffer range differs')
            if kind=='buffer':require(d['implicit'] is None,'Explicit buffer borrowed implicit identity')
            else:
                implicit_count+=1;require(implicit_count==1,'Multiple captured-slot implicit arenas')
                require(buffer['sizeInBytes']==4096*256 and observed%256==0 and 256<=observed<=4096*256,'Actual implicit arena capacity/prefix alignment differs')
                x=d['implicit'];fields(x,'kind resourceGeneration frameIndex descriptor rootParameter textureRegister constantByteOffset textureRootParameter','implicit arena')
                require(x['kind']=='captured-slot-shadow-vertex-constant-prefix' and same(x['resourceGeneration'],original['submission']['resourceGeneration']) and same(x['frameIndex'],original['frameIndex']) and buffer['memory']=='Upload' and offset==0 and observed>=256,'Implicit arena epoch/slot/range differs')
                require(all(x[k] is None for k in ('descriptor','rootParameter','textureRegister','textureRootParameter')) and type(x['constantByteOffset']) is int and x['constantByteOffset']==0,'Implicit arena invented bindings')
        layouts=array(d['mips'],32,'mip layouts');raws=array(row['rawMips'],32,'raw mip refs');require(len(layouts)==len(raws)==(texture['mipCount'] if texture else 1),'Mip count differs')
        resource_total=0;hashes=[]
        for mip,(layout,r) in enumerate(zip(layouts,raws)):
            fields(layout,MIP,'canonical layout');fields(r,'file sha256 bytes mipIndex','raw ref');mip_count+=1;require(mip_count<=131072,'Total mip entries exceeded')
            expected_mip=mip if texture else -1;require(type(layout['mipIndex']) is int and layout['mipIndex']==expected_mip and same(r['mipIndex'],expected_mip),'Mip ordinal differs')
            if texture:
                w=max(1,texture['width']//2**mip);h=max(1,texture['height']//2**mip);rows_expected=(h+3)//4 if texture['format']=='Bc7Unorm' else h
                row_bytes=((w+3)//4)*16 if texture['format']=='Bc7Unorm' else w*(1 if texture['format']=='R8Unorm' else 4)
                fmt={'Rgba8Unorm':'R8G8B8A8_UNorm','Bc7Unorm':'BC7_UNorm','R8Unorm':'R8_UNorm'}[texture['format']]+('_SRgb' if texture['colorSpace']=='Srgb' else '')
                require(layout['format']==fmt and number(layout['rowPitch'],'row pitch',row_bytes)%256==0,'Encoded format/pitch differs')
            else:w=h=0;rows_expected=1;row_bytes=observed;require(layout['format']=='Buffer' and same(layout['rowPitch'],observed),'Buffer layout differs')
            for k,v in [('width',w),('height',h),('rowCount',rows_expected),('rowBytes',row_bytes)]:require(type(layout[k]) is int and layout[k]==v,'Logical layout differs: '+k)
            start=number(layout['packedOffset'],'packed offset',0,RAW_BYTES);padded=number(layout['paddedBytes'],'padded bytes',1,RAW_BYTES)
            require(start%512==0 and start>=end and padded==layout['rowPitch']*rows_expected,'Packed footprint overlaps/shape differs');end=start+padded;require(end<=RAW_BYTES,'Total staging capacity exceeded')
            raw_size=row_bytes*rows_expected;require(number(layout['canonicalBytes'],'canonical bytes',1,RAW_BYTES)==raw_size,'Canonical size differs')
            raw_bytes=bounded_bytes(sibling(root,r['file']),RAW_BYTES);require(r['file'].casefold() not in names,'Aliased raw filename');names[r['file'].casefold()]=True
            require(number(r['bytes'],'raw bytes',1,RAW_BYTES)==len(raw_bytes)==raw_size and digest(r['sha256'])==sha256_bytes(raw_bytes),'Canonical raw hash/length differs')
            total+=raw_size;resource_total+=raw_size;require(total<=RAW_BYTES,'Canonical aggregate capacity exceeded');hashes.append(digest(r['sha256']))
        require(resource_total==observed,'Observed/canonical resource total differs');signatures.append({'semanticName':d['semanticName'],'kind':kind,'buffer':buffer,'texture':texture,'sourceOffset':offset,'observedBytes':observed,'rawSha256':hashes})
    intent(value['intent'],{row['description']['localId']:row['description'] for row in rows})
    return {'status':'complete-late-resident-subset','qualified':False,'original':value,'reference':ref,'semanticResources':signatures,'canonicalBytes':total,'paddedStagingBytes':end}


def validate_series(records):
    require(type(records) is list and len(records)==8,'All eight selected snapshots required')
    first=records[0];require(first['status']=='complete-late-resident-subset','Selected snapshot unavailable')
    prior=None;comparisons=[];copy_domains=set()
    for i,r in enumerate(records):
        require(r['status']=='complete-late-resident-subset','Incomplete selected series');v=r['original'];m=v['metadata'];o=v['originalFrameReceipt']
        require(type(m['repetition']) is int and m['repetition']==i,'Repetition order differs')
        for key in ('sourceSha','runtimeFingerprint','recipeFingerprint','recipeInputBytesBase64','camera','profileFingerprint','observedComposition'):require(same(m[key],first['original']['metadata'][key]),'Immutable series metadata differs: '+key)
        require(same(o['deviceIdentity'],first['original']['originalFrameReceipt']['deviceIdentity']),'Series device/session differs')
        require(same(o['managedOwnerThreadId'],first['original']['originalFrameReceipt']['managedOwnerThreadId']),'Series managed owner differs')
        domain=v['copyReceipt']['fenceDomain'];require(domain not in copy_domains,'Late copy fence domain reused');copy_domains.add(domain)
        if prior:
            require(o['completedFence']>=prior['completedFence'],'Original completed fence regressed')
            require(o['submission']['submission']==prior['submission']['submission']+1 and o['submission']['submittedFence']>prior['submission']['submittedFence'] and o['presentedBefore']==prior['presentedAfter'],'Original frame chronology differs')
            require(o['submission']['resourceGeneration']==prior['submission']['resourceGeneration'] and o['width']==prior['width'] and o['height']==prior['height'],'Series resource/raster epoch differs')
        prior=o
        comparisons.append({'repetition':i,'frameIndex':o['frameIndex'],'actualSubmission':o['submission'],'semanticResidentBytesEqualToFirst':same(r['semanticResources'],first['semanticResources']),
            'retainedIntentEqualToFirst':same(v['intent'],first['original']['intent'])})
    return {'status':'retained-diagnostic-comparison','qualified':False,'wave2Ready':False,'comparisons':comparisons,
        'scope':'Late bytes after PNG, process-local roster/slot identities and retained intent only; not shader contents, cause or qualification.'}
