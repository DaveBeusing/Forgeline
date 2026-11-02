"""Selected late resources/retained attachments and source-closed CPU opaque state.
Never GPU execution, shader-fetched contents, causal repair or baseline admission.
"""
from __future__ import annotations
import base64
import json
import math
import re
import struct
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
MODES=('RetainedAttachmentsOnly','OpaqueStateAndRetainedAttachments')
ATTACHMENTS=('retained-scene-linear','retained-shadow-depth','retained-final-depth')
EXPECTED_V2=EXPECTED+' coverageMode rasterWidth rasterHeight'


def full_mip_levels(width,height):
    number(width,'backing width',1,2**31-1);number(height,'backing height',1,2**31-1)
    return max(width,height).bit_length()


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
    body=sum(len(source[a:b].encode('utf-8')) for k,(a,b) in spans.items() if k in ('resources','intent','opaqueWitness'))
    require(body<=31*1024*1024 and len(raw)-body+8<=1024*1024,'Original body/envelope capacity exceeded')


def camera(value):
    fields(value,'target yawRadians pitchRadians distance hasPointerPosition pointerPosition','camera')
    for key,axes in [('target','x y z'),('pointerPosition','x y')]:
        fields(value[key],axes,key)
        for n in axes.split():require(single_value(value[key][n]) is not None,'Nonfinite camera vector')
    for n in ('yawRadians','pitchRadians','distance'):require(single_value(value[n]) is not None,'Nonfinite camera scalar')
    require(value['distance']>0 and type(value['hasPointerPosition']) is bool,'Invalid camera')


def scaled_dimension(value,scale):
    """GraphicsFramePlan.ScaledDimension: ceil(int * double(float32)), then max1."""
    number(value,'independent raster dimension',1,2**31-1);rounded=single_value(scale)
    require(rounded is not None and rounded>0,'Invalid native float32 resolution scale')
    result=math.ceil(value*rounded);require(result<=2**31-1,'Native checked scaled dimension overflow')
    return max(1,result)


def attachment_dimensions(quality,expected,original):
    require(type(quality) is dict and quality.get('linearScene') is True,'Retained SceneLinear requires independently retained effective linear-scene quality')
    scale=single_value(quality.get('resolutionScale'));require(scale is not None and .5<=scale<=1,'Unsupported effective float32 resolution scale')
    width=number(expected['rasterWidth'],'independent raster width',1,2**31-1);height=number(expected['rasterHeight'],'independent raster height',1,2**31-1)
    require(same(original['width'],width) and same(original['height'],height),'Original receipt raster differs from independently requested raster')
    shadows=quality.get('shadows');require(type(shadows) is dict and shadows.get('enabled') is True and type(shadows.get('quality')) is str and shadows['quality'] in ('Low','Medium','High'),'Required shadow target lacks effective enabled shadow quality')
    resolution={'Low':1024,'Medium':2048,'High':4096}[shadows['quality']]
    require(number(shadows.get('resolution'),'effective shadow resolution',1,4096)==resolution,'Effective shadow quality/resolution differs from source')
    world=(scaled_dimension(width,scale),scaled_dimension(height,scale));scaled=scale<1
    require(original['worldDepthAliasesRetainedFinalDepth'] is (not scaled),'World/final-depth alias differs from actual scaled-scene allocation rule')
    return {ATTACHMENTS[0]:world,ATTACHMENTS[1]:(resolution,resolution),ATTACHMENTS[2]:(width,height) if scaled else world}


def attachment_formats(role,original,composition):
    """Actual source format branches, not a common depth-format whitelist.

    Shadow resources are always R32_Typeless. A scaled scene has a separate final
    overlay depth resource, always D32_Float. Unscaled atmosphere Enabled=true
    independently proves the shared World/final depth is R32_Typeless. Atmosphere
    false/unknown does NOT prove D32_Float: actual particle-depth enablement is not
    independently closed by this consumer, so both source-supported formats remain
    possible in that branch. This limit never supplies World/final object identity.
    """
    require(type(composition) is dict and type(composition.get('atmosphere')) is dict,'Missing independent observed atmosphere object')
    atmosphere=composition['atmosphere']
    if 'enabled' in atmosphere:require(type(atmosphere['enabled']) is bool,'Observed atmosphere enabled must be a strict boolean')
    if role==ATTACHMENTS[0]:return ('R16G16B16A16_Float',)
    if role==ATTACHMENTS[1]:return ('R32_Typeless',)
    require(role==ATTACHMENTS[2],'Unknown retained attachment role')
    if original['worldDepthAliasesRetainedFinalDepth'] is False:return ('D32_Float',)
    if atmosphere.get('enabled') is True:return ('R32_Typeless',)
    return ('R32_Typeless','D32_Float')


def receipt(value,*,snapshot_version=1,mode=None):
    fields(value,ORIGINAL+(' coverageMode invocation worldDepthAliasesRetainedFinalDepth' if snapshot_version==2 else ''),'original receipt');d=value['deviceIdentity'];fields(d,DEVICE,'device identity')
    require(type(value['schemaVersion']) is int and value['schemaVersion']==snapshot_version and value['fenceDomain']=='original-device-frame-fence','Unknown original domain/version')
    if snapshot_version==2:
        require(type(mode) is str and mode in MODES and value['coverageMode']==mode,'Original receipt mode differs from independently requested mode')
        number(value['invocation'],'current invocation',1,2**64-2)
        require(type(value['worldDepthAliasesRetainedFinalDepth']) is bool,'Missing actual World/final-depth alias witness')
        require(mode!=MODES[1] or value['worldDepthAliasesRetainedFinalDepth'] is True,'Opaque World depth is not covered by retained-final-depth')
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
    require(type(snapshot_version) is int and snapshot_version in (1,2),'Unknown selected late snapshot version')
    mode=None
    if snapshot_version==2:
        fields(expected,EXPECTED_V2,'independent original v2 expectations')
        mode=expected['coverageMode'];require(type(mode) is str and mode in MODES,'Missing exact independently requested snapshot mode')
    raw=reference(root,ref,MAX_BYTES);bounds(raw);value=parse_json_bytes(raw)
    fields(value,'schemaVersion kind complete metadata originalFrameReceipt copyReceipt coverage resources intent'+(' coverageMode opaqueCoverage opaqueWitness' if snapshot_version==2 else ''),'snapshot')
    require(type(value['schemaVersion']) is int and value['schemaVersion']==snapshot_version and value['kind']==('reference-post-final-color-resident-resources' if snapshot_version==1 else 'reference-post-final-color-causal-witness') and value['complete'] is True,'Selected snapshot incomplete/unknown')
    if snapshot_version==2:
        require(value['coverageMode']==mode,'Snapshot mode differs from independently requested mode')
        require(value['opaqueCoverage']==('not-selected' if mode==MODES[0] else 'source-closed-effective-opaque-state'),'Opaque selected coverage differs')
        require((value['opaqueWitness'] is None) == (mode==MODES[0]),'Intentionally absent attachment-only witness differs from mandatory opaque evidence')
    m=value['metadata'];fields(m,META,'snapshot metadata');fields(expected,EXPECTED_V2 if snapshot_version==2 else EXPECTED,'independent original expectations')
    require(m['producer']=='native-reference' and m['qualified'] is False and re.fullmatch('[0-9a-fA-F]{40}',m['sourceSha']),'Foreign producer/source')
    for k in ('runtimeFingerprint','recipeFingerprint','profileFingerprint','pixelSha256','legacyFingerprint'):digest(m[k])
    for k in expected:
        if k not in ('deviceIdentity','managedOwnerThreadId','frameIndex','actualSubmission','coverageMode','rasterWidth','rasterHeight'):require(same(m[k],expected[k]),'Original metadata differs: '+k)
    number(m['repetition'],'repetition',0,7);camera(m['camera'])
    require(m['profileDomain']=='effective-quality-profile','Quality/full composition hash domains differ')
    try:recipe=base64.b64decode(m['recipeInputBytesBase64'],validate=True);profile=base64.b64decode(m['profileInputBytesBase64'],validate=True)
    except Exception as error:raise ValueError('Malformed original serialized inputs') from error
    require(sha256_bytes(recipe)==digest(m['recipeFingerprint']) and sha256_bytes(profile)==digest(m['profileFingerprint']),'Original input hash differs')
    parsed=parse_json_bytes(recipe);quality=parse_json_bytes(profile)
    require(same(parsed['runtimeFingerprint'],m['runtimeFingerprint']) and same(parsed['settings'],quality['requestedSettings']),'Actual recipe/runtime/quality differs')
    original=value['originalFrameReceipt'];receipt(original,snapshot_version=snapshot_version,mode=mode);copy_receipt(value['copyReceipt'],original)
    if snapshot_version==2:require(original['invocation']==m['repetition']+1,'Original invocation does not match declared current repetition')
    dimensions=attachment_dimensions(quality,expected,original) if snapshot_version==2 else None
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
    included=['resident-terrain','final-retained-main-scratch','final-retained-decoration-scratch','captured-slot-constant-arena-used-prefix-if-present']
    excluded=['render-targets','depth-shadow-targets','root-constants','particles','HUD','skin-caches','inactive-private-caches','earlier-pass-scratch','entity-root-associations']
    if snapshot_version==2:
        included+=list(ATTACHMENTS)
        excluded=['opaque-state-not-selected','particles','HUD','skin-caches','inactive-private-caches','earlier-pass-scratch','entity-root-associations']
        if mode==MODES[1]:
            included+=['source-closed-effective-opaque-state']
            excluded=['shadow-draw-state','particle-draw-state','composition-draw-state','HUD','skin-caches','inactive-private-caches','earlier-pass-scratch','entity-root-associations']
    require(scope['included']==included,'Coverage whitelist differs');require(scope['excluded']==excluded,'Coverage exclusions differ')
    rows=array(value['resources'],4096,'resource roster');require(rows,'Empty declared resource subset');total=0;end=0;mip_count=0;names={};signatures=[];implicit_count=0;attachments={};causal={}
    for index,row in enumerate(rows):
        fields(row,'description rawMips','resource row');d=row['description']
        extra=''
        if snapshot_version==2:
            require(type(d) is dict,'Missing v2 resource description')
            extra=''.join(' '+key for key in ('attachment','causalIdentityId') if key in d)
        fields(d,DESCRIPTION+extra,'resource description')
        require(number(d['localId'],'local ID',1,4096)==index+1,'Resource order/identity differs');text(d['semanticName'],'semantic name');text(d['rangeMeaning'],'range meaning')
        kind=d['kind'];attachment=d.get('attachment')
        require(kind in (('buffer','texture','implicit-buffer','retained-attachment') if snapshot_version==2 else ('buffer','texture','implicit-buffer')) and type(d['lifetimeAvailable']) is bool and d['lifetimeAvailable']==(kind in ('buffer','texture')),'Resource/lifetime kind differs')
        if snapshot_version==2:
            require(('attachment' in d)==(kind=='retained-attachment'),'Intentional attachment shape differs')
            if 'causalIdentityId' in d:
                require(mode==MODES[1],'Attachment-only cannot acquire tape/catalog identities')
                cid=number(d['causalIdentityId'],'causal resource identity',1,4096);require(cid not in causal,'Causal resource identity aliased');causal[cid]=row
        texture=d['texture'];buffer=d['buffer'];offset=number(d['sourceOffset'],'source offset');observed=number(d['observedBytes'],'observed bytes',1,RAW_BYTES)
        if kind=='retained-attachment':
            fields(attachment,'role nativeFormat canonicalFormat depthViewFormat shaderViewFormat originalState width height capturedSlot resourceGeneration meaning nativeMipLevels observedMipSlice observedMipLevels observedSubresource','retained attachment')
            role=attachment['role'];require(type(role) is str and role in ATTACHMENTS and role not in attachments and d['semanticName']==role,'Unknown/duplicate retained attachment role')
            require(texture is None and buffer is None and d['implicit'] is None and offset==0,'Retained target invented an asset/Lifetime/offset')
            for key in ('width','height'):number(attachment[key],key,1,2**31-1)
            require((attachment['width'],attachment['height'])==dimensions[role],'Actual retained attachment dimensions differ from independent quality/raster/source sizing')
            backing=full_mip_levels(attachment['width'],attachment['height']) if role==ATTACHMENTS[2] else 1
            require(number(attachment['nativeMipLevels'],'actual native mip levels',1,32)==backing,'Native backing mip count differs from source allocation')
            for key,wanted in (('observedMipSlice',0),('observedMipLevels',1),('observedSubresource',0)):
                require(type(attachment[key]) is int and attachment[key]==wanted,'Only mip0 array0 plane0 subresource0 is observed; other backing mips are unobserved')
            require(same(attachment['capturedSlot'],original['frameIndex']) and same(attachment['resourceGeneration'],original['submission']['resourceGeneration']),'Retained attachment slot/generation differs')
            text(attachment['meaning'],'attachment meaning');scene=role==ATTACHMENTS[0]
            require(attachment['nativeFormat'] in attachment_formats(role,original,expected['observedComposition']),'Retained target native format differs from independently disambiguated source role/branch')
            require(attachment['canonicalFormat']==('R16G16B16A16_Float' if scene else 'R32_Float') and attachment['depthViewFormat']==(None if scene else 'D32_Float') and attachment['shaderViewFormat']==('R16G16B16A16_Float' if scene else 'R32_Float' if role==ATTACHMENTS[1] else None),'Retained target view/encoding differs')
            require(attachment['originalState']==('DepthWrite' if role==ATTACHMENTS[2] else 'PixelShaderResource'),'Retained attachment restoration state differs')
            attachments[role]=row
        elif kind=='texture':
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
            expected_mip=mip if texture or attachment else -1;require(type(layout['mipIndex']) is int and layout['mipIndex']==expected_mip and same(r['mipIndex'],expected_mip),'Mip ordinal differs')
            if attachment:
                w=attachment['width'];h=attachment['height'];rows_expected=h;row_bytes=w*(8 if attachment['role']==ATTACHMENTS[0] else 4)
                require(layout['format']==attachment['nativeFormat'] and number(layout['rowPitch'],'attachment row pitch',row_bytes)%256==0,'Retained target format/pitch differs')
            elif texture:
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
        require(resource_total==observed,'Observed/canonical resource total differs')
        signature={'semanticName':d['semanticName'],'kind':kind,'buffer':buffer,'texture':texture,'sourceOffset':offset,'observedBytes':observed,'rawSha256':hashes}
        if attachment:signature['attachment']={k:v for k,v in attachment.items() if k not in ('capturedSlot','resourceGeneration','meaning')}
        signatures.append(signature)
    intent(value['intent'],{row['description']['localId']:row['description'] for row in rows})
    result={'status':'complete-late-resident-subset','qualified':False,'original':value,'reference':ref,'semanticResources':signatures,'canonicalBytes':total,'paddedStagingBytes':end}
    if snapshot_version==2:
        require(set(attachments)==set(ATTACHMENTS),'All three retained attachments are mandatory')
        result.update(status='complete-retained-attachments' if mode==MODES[0] else 'complete-opaque-state-and-retained-attachments',coverageMode=mode,
            semanticResources=[x for x in signatures if x['kind']!='retained-attachment'],semanticAttachments={x['semanticName']:x for x in signatures if x['kind']=='retained-attachment'},
            opaqueState=None if mode==MODES[0] else opaque_witness(root,value,causal))
    return result


def opaque_catalog(creations,identities):
    """Decode the closed source encoder, deriving masks from actual native rows."""
    catalog={};storage=0
    widths={'RootFlags':1,'RootParameter':2,'RootConstants':3,'RootDescriptor':3,'DescriptorRange':7,
        'InputElement':8,'Sample':2,'PrimitiveTopology':1,'RenderTargetFormat':1,'DepthFormat':1,
        'PipelineFlags':1,'SampleMask':1,'PipelineHeader':4,'OptionalShaderStages':0,'StreamOutput':0,'CachedPipelineState':0}
    mandatory=('RootFlags','PipelineHeader','OptionalShaderStages','StreamOutput','CachedPipelineState','PrimitiveTopology','PipelineFlags','SampleMask','DepthFormat','Sample')
    for ordinal,c in enumerate(array(creations,4096,'native creation catalog')):
        fields(c,'id metadata semanticNamesUtf8Base64 pipelineIdentity rootIdentity requiredState vertexBytes vertexSha256 pixelBytes pixelSha256 creationRole','native creation')
        require(number(c['id'],'catalog ID',1,4096)==ordinal+1,'Catalog order differs')
        pair=(number(c['pipelineIdentity'],'pipeline identity',1,4096),number(c['rootIdentity'],'root identity',1,4096))
        require(pair not in catalog and identities.get(pair[0],{}).get('kind')=='Pipeline' and identities.get(pair[1],{}).get('kind')=='RootSignature','Unregistered/duplicate native catalog identity')
        require(c['creationRole'] in ('Ordinary','DirectionalShadowReceiver','SceneDepthReceiver'),'Unknown source creation role')
        metadata=array(c['metadata'],16*1024*1024//40,'native creation rows');require(metadata,'Empty native creation metadata');rows={}
        for row in metadata:
            fields(row,'field index a b c d','native creation row');field=row['field'];index=number(row['index'],'native field index',0,2**32-1)
            require(type(field) is str and field in set(widths)|{'StaticSampler','BlendTarget','Rasterizer','DepthStencil'},'Unknown native creation field')
            require((field,index) not in rows,'Duplicate native creation field/index')
            words=[]
            for key in ('a','b','c','d'):
                value=number(row[key],'native field operand',0,2**64-1);words.extend((value&0xffffffff,value>>32))
            used=(8 if index%2==0 else 5) if field=='StaticSampler' else (2 if index==16 or index%2 else 8) if field=='BlendTarget' else (8 if index==0 else 3) if field=='Rasterizer' else (8 if index==0 else 6) if field=='DepthStencil' else widths[field]
            require(all(x==0 for x in words[used:]),'Nonzero native reserved operands');rows[field,index]=words
        for field in mandatory:require((field,0) in rows and sum(k[0]==field for k in rows)==1,'Missing/duplicate fixed native field: '+field)
        for field in ('OptionalShaderStages','StreamOutput','CachedPipelineState'):require(rows[field,0]==[0]*8,'Unsupported optional native stage/stream/cache')
        def indexed(field,maximum):
            values={i:v for (f,i),v in rows.items() if f==field}
            require(len(values)<=maximum and set(values)==set(range(len(values))),'Noncontiguous native indices: '+field)
            return values
        parameters=indexed('RootParameter',23);ranges=indexed('DescriptorRange',16);elements=indexed('InputElement',32);samplers=indexed('StaticSampler',32)
        require(len(samplers)%2==0,'Unpaired static sampler rows')
        blend=indexed('BlendTarget',17);raster=indexed('Rasterizer',2);depth=indexed('DepthStencil',2)
        require(len(blend)==17 and len(raster)==2 and len(depth)==2,'Missing final blend/raster/depth fields')
        require(all(blend[i][0] in (0,1) and blend[i][1] in (0,1) for i in range(0,16,2)) and all(x in (0,1) for x in blend[16][:2]),'Invalid native blend booleans')
        require(all(depth[0][i] in (0,1) for i in (0,3)) and all(raster[0][i] in (0,1) for i in (2,6,7)) and raster[1][0] in (0,1),'Invalid native depth/raster booleans')
        formats=indexed('RenderTargetFormat',8);header=rows['PipelineHeader',0]
        require(header[0]==len(formats) and rows['PipelineFlags',0][0]==header[3] and rows['Sample',0][0]>=1,'Final PSO header/sample/format count differs')
        roots={};tables={};root_mask=texture_mask=retained_mask=vertex_mask=0
        for index,p in parameters.items():
            kind,visibility=p[:2];require(kind in (0,1,2) and visibility in (0,1,5),'Unsupported source root parameter')
            if kind in (1,2):
                field='RootConstants' if kind==1 else 'RootDescriptor';require((field,index) in rows,'Missing native root declaration')
                declaration=rows[field,index];require(declaration[1]==0,'Unsupported native root register space')
                if kind==1:require(1<=declaration[2]<=64,'Invalid declared native root DWORD count')
                roots[index]=dict(kind=kind,words=declaration[2] if kind==1 else None,register=declaration[0],visibility=visibility);root_mask|=1<<index
            else:require(not any((f,index) in rows for f in ('RootConstants','RootDescriptor')),'Table has conflicting root declaration')
        require(all(i in parameters and parameters[i][0]==(1 if f=='RootConstants' else 2) for f,i in rows if f in ('RootConstants','RootDescriptor')),'Orphan/conflicting root declaration')
        for r in ranges.values():
            root,kind,count,slot,space=r[:5]
            require(root in parameters and parameters[root][0]==0 and parameters[root][1]==5 and kind==0 and count==1 and 0<=slot<16 and space==0 and slot not in tables,'Unsupported/duplicate native table mapping')
            tables[slot]=root
            if slot==15 and c['creationRole']!='Ordinary':retained_mask|=1<<slot
            else:texture_mask|=1<<slot
        require(set(tables.values())=={i for i,p in parameters.items() if p[0]==0},'Unmapped root descriptor table')
        if c['creationRole']=='DirectionalShadowReceiver':
            require(15 in tables and tables[15]-1 in roots and roots[tables[15]-1]['kind']==2 and roots[tables[15]-1]['register']==1 and roots[tables[15]-1]['visibility']==5,'Shadow receiver lacks its actual implicit CBV/table graph')
        elif c['creationRole']=='SceneDepthReceiver':require(15 in tables,'Scene-depth receiver lacks its actual retained target table')
        try:semantics=base64.b64decode(c['semanticNamesUtf8Base64'],validate=True)
        except Exception as error:raise ValueError('Malformed frozen native semantic bytes') from error
        semantic_end=0
        for e in elements.values():
            offset,length,_,fmt,_,slot,classification,step=e
            require(offset==semantic_end and length>0 and offset+length<=len(semantics) and slot<32 and fmt>0 and classification in (0,1),'Invalid native input element')
            require(classification!=0 or step==0,'Per-vertex input has instance step rate')
            require(all(0<b<128 for b in semantics[offset:offset+length]),'Native semantic bytes outside supported non-NUL ASCII domain')
            semantic_end=offset+length;vertex_mask|=1<<slot
        require(semantic_end==len(semantics),'Unclaimed frozen semantic bytes')
        required=c['requiredState'];fields(required,'vertexSlots textureSlots rootParameters depthTarget retainedTargetTextureSlots','derived required native state')
        for key,derived,maximum in [('vertexSlots',vertex_mask,2**32-1),('textureSlots',texture_mask,65535),('rootParameters',root_mask,2**23-1),('retainedTargetTextureSlots',retained_mask,65535)]:
            require(number(required[key],key,0,maximum)==derived,'Required mask differs from actual frozen native inputs: '+key)
        require(type(required['depthTarget']) is bool and required['depthTarget']==bool(depth[0][0] or depth[0][3]),'Required depth differs from final depth/stencil state')
        for stage in ('vertex','pixel'):
            storage+=number(c[stage+'Bytes'],stage+' bytecode bytes',1,16*1024*1024);digest(c[stage+'Sha256'])
        storage+=len(metadata)*40+len(semantics);require(storage<=16*1024*1024,'Frozen creation arena capacity exceeded')
        catalog[pair]=dict(original=c,roots=roots,tables=tables,required=required,
            semantic={k:v for k,v in c.items() if k not in ('id','pipelineIdentity','rootIdentity')})
    require(catalog,'Missing completed native creation catalog')
    return catalog


def opaque_witness(root,value,causal):
    """Replay source-proved CPU effects independently of requests and production caches."""
    w=value['opaqueWitness'];o=value['originalFrameReceipt'];m=value['metadata']
    fields(w,'schemaVersion kind complete evidenceScope deviceSession managedOwnerThreadId repetition invocation refusal originalFrame events rootWordCount rootByteCount rootLittleEndianBase64 rootSha256 creations identities','opaque witness')
    require(type(w['schemaVersion']) is int and w['schemaVersion']==2 and w['kind']=='post-final-color-causal-witness' and w['complete'] is True and w['refusal']=='None','Incomplete/refused opaque witness')
    require(w['evidenceScope']=='source-closed-cpu-effective-opaque-state-and-after-PNG-retained-targets; not GPU shader fetch proof','Opaque evidence claim differs')
    require(type(w['deviceSession']) is str and re.fullmatch('[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}',w['deviceSession']) and w['deviceSession'].replace('-','').lower()==o['deviceIdentity']['sessionId'].lower(),'Opaque session differs')
    for key,expected in [('managedOwnerThreadId',o['managedOwnerThreadId']),('repetition',m['repetition']),('invocation',o['invocation'])]:require(same(w[key],expected),'Opaque current owner/repetition/invocation differs')
    frame=w['originalFrame'];fields(frame,'capturedSlot submission submittedFence completedFence deviceGeneration resourceGeneration presentedBefore presentedAfter','opaque original frame')
    expected_frame=dict(capturedSlot=o['frameIndex'],submission=o['submission']['submission'],submittedFence=o['submission']['submittedFence'],completedFence=o['completedFence'],deviceGeneration=o['submission']['generation'],resourceGeneration=o['submission']['resourceGeneration'],presentedBefore=o['presentedBefore'],presentedAfter=o['presentedAfter'])
    require(same(frame,expected_frame),'Opaque original tuple differs from actual independent receipt')
    count=number(w['rootWordCount'],'root word count',0,1048576);require(number(w['rootByteCount'],'root bytes',0,4194304)==count*4,'Root byte/word count differs')
    try:root_bytes=base64.b64decode(w['rootLittleEndianBase64'],validate=True)
    except Exception as error:raise ValueError('Malformed little-endian root words') from error
    require(len(root_bytes)==count*4 and digest(w['rootSha256'])==sha256_bytes(root_bytes),'Root word bytes/hash differ')
    identities={};kinds=('Buffer','Texture','Pipeline','RootSignature','ImplicitArena','RetainedTarget','DescriptorHeap')
    for index,i in enumerate(array(w['identities'],4096,'session identities')):
        fields(i,'id kind lifetimeAvailable epoch slot descriptor offset bytes stride','session identity')
        require(number(i['id'],'session identity',1,4096)==index+1 and i['kind'] in kinds and type(i['lifetimeAvailable']) is bool,'Invalid session identity/kind')
        require(i['lifetimeAvailable']==(i['kind'] in ('Buffer','Texture','Pipeline','RootSignature')),'Actual lifetime availability differs')
        for key in ('epoch','slot','descriptor','offset','bytes','stride'):number(i[key],key,0,2**64-1)
        require(i['offset']+i['bytes']<=2**64-1,'Identity range overflow');identities[i['id']]=i
        if i['kind'] in ('Pipeline','RootSignature'):require(all(i[k]==0 for k in ('epoch','slot','descriptor','offset','bytes','stride')),'Catalog identity has invented resource range')
        elif i['kind']=='ImplicitArena':require(i['epoch']==o['submission']['resourceGeneration'] and i['slot'] in (0,1,2) and i['descriptor']==i['offset']==0 and i['bytes']==4096*256 and i['stride']==4,'Implicit arena identity differs')
        elif i['kind']=='RetainedTarget':require(i['epoch']==o['submission']['resourceGeneration'] and i['slot'] in (0,1,2,2**64-1) and i['descriptor']<4096 and i['offset']==i['bytes']==i['stride']==0,'Retained target identity differs')
        else:
            require(i['epoch']==o['deviceIdentity']['generation'] and i['slot']==0,'Asset/heap identity epoch differs')
            if i['kind']=='DescriptorHeap':require(all(i[k]==0 for k in ('descriptor','offset','bytes','stride')),'Descriptor heap has resource/view operands')
            elif i['kind']=='Buffer':require(i['descriptor'] in (0,1) and i['offset']==i['stride']==0 and i['bytes']>0,'Buffer identity union differs')
            else:
                require(i['descriptor']<4096 and i['bytes']==0 and 1<=i['offset']&0xffffffff<=2**31-1 and 1<=i['offset']>>32<=2**31-1 and 1<=i['stride']&255<=32 and (i['stride']>>8)&255 in (1,2,3) and i['stride']>>16 in (1,2),'Texture identity union differs')
    require(identities,'Opaque identities missing')
    for cid,row in causal.items():
        require(cid in identities,'Late resource points to missing session identity');i=identities[cid];d=row['description'];kind=d['kind']
        require(i['kind']=={'buffer':'Buffer','texture':'Texture','implicit-buffer':'ImplicitArena','retained-attachment':'RetainedTarget'}[kind],'Late resource/session identity kind differs')
        if kind=='buffer':require(i['bytes']==d['buffer']['sizeInBytes'] and i['descriptor']=={'GpuLocal':0,'Upload':1}[d['buffer']['memory']] and i['offset']==i['stride']==0,'Buffer identity capacity/memory differs')
        elif kind=='texture':
            t=d['texture'];packed=t['mipCount']|({'Rgba8Unorm':1,'Bc7Unorm':2,'R8Unorm':3}[t['format']]<<8)|({'Linear':1,'Srgb':2}[t['colorSpace']]<<16)
            require(i['bytes']==0 and i['offset']==(t['width']|(t['height']<<32)) and i['stride']==packed,'Texture identity dimensions/encoding differs')
        elif kind=='implicit-buffer':require(i['slot']==o['frameIndex'] and i['bytes']==d['buffer']['sizeInBytes'],'Captured implicit arena identity differs')
        else:require(i['slot']==(2**64-1 if d['attachment']['role']==ATTACHMENTS[2] else o['frameIndex']),'Actual target slot/shared-depth identity differs')
    catalog=opaque_catalog(w['creations'],identities)
    states={};payloads={};draws=[];views=[];epoch=0;pass_name='Reset';proved=False;consumed=0
    scalar=('Pipeline','RootSignature','DescriptorHeap','Topology','IndexBuffer','Viewport','Scissor','RenderTarget','DepthTarget')
    def key(binding,slot):
        require((binding in scalar and slot==0) or (binding=='VertexBuffer' and slot<32) or (binding=='TextureTable' and slot<16) or (binding=='RootParameter' and slot<23),'Unknown binding or slot')
        return binding,slot
    def identity(identity_id,kind):
        require(identity_id in identities and identities[identity_id]['kind']==kind,'Missing/wrong effective resource identity')
        return identities[identity_id]
    def late(identity_id,kind=None):
        require(identity_id in causal,'Effective opaque resource has no retained late source join');row=causal[identity_id]
        if kind is not None:require(row['description']['kind']==kind,'Opaque late source role/kind differs')
        return row
    def view(identity_id,start,length,label):
        i=identities[identity_id];require(start<=i['bytes'] and 0<length<=i['bytes']-start,'Native view exceeds actual resource capacity')
        d=late(identity_id)['description'];begin=d['sourceOffset'];end=begin+d['observedBytes'];stop=start+length
        intersection_start=min(stop,max(start,begin));intersection_end=max(intersection_start,min(stop,end));overlap=max(0,intersection_end-intersection_start)
        tails=[]
        if start<min(stop,begin):tails.append(dict(offset=start,bytes=min(stop,begin)-start))
        if max(start,end)<stop:tails.append(dict(offset=max(start,end),bytes=stop-max(start,end)))
        record=dict(draw=len(draws),binding=label,causalIdentityId=identity_id,nativeOffset=start,nativeBytes=length,resourceCapacity=i['bytes'],dumpOffset=begin,dumpBytes=d['observedBytes'],intersectionOffset=intersection_start,intersectionBytes=overlap,uncapturedRanges=tails,completeViewCaptured=overlap==length,shaderFetchCoverageProved=False)
        require(len(views)<131072,'Derived native-view coverage capacity exceeded');views.append(record)
        return dict(semanticName=d['semanticName'],kind=d['kind'],nativeOffset=start,nativeBytes=length,resourceCapacity=i['bytes'],dumpOffset=begin,dumpBytes=d['observedBytes'],intersectionBytes=overlap,uncapturedRanges=tails)
    def known(binding,slot=0):
        require((binding,slot) in states,'Unknown effective state at required opaque draw: '+binding)
        return states[binding,slot]
    def effective(binding,slot,b):
        if binding=='DescriptorHeap' and states.get((binding,slot),{}).get('identity')!=b['identity']:
            for old in list(states):
                if old[0]=='TextureTable':del states[old]
        states[binding,slot]=dict(b,known=1)
    events=array(w['events'],131072,'opaque source events');require(events,'Missing opaque events')
    for ordinal,e in enumerate(events):
        fields(e,'sequence opcode provenance passEpoch binding viewport draw rootWords pass','opaque event')
        require(number(e['sequence'],'event sequence',0,131071)==ordinal,'Opaque event sequence differs')
        op=e['opcode'];provenance=e['provenance'];require(type(op) is str and op in ('Reset','Request','CacheHit','NativeReturn','NativeFailure','RootInvalidation','OwnerPostcondition','RootWords','Draw','DrawIndexed','PassBoundary'),'Unknown opaque opcode')
        require(provenance in ('Unknown','SetterRequested','CacheHitDecision','CpuNativeCallReturn','SourceProvedOwnerPostcondition'),'Unknown opaque provenance')
        number(e['passEpoch'],'pass epoch',0,2**32-1)
        payload=[name for name in ('binding','viewport','draw','rootWords','pass') if e[name] is not None]
        if op=='Reset':
            require(ordinal==0 and not payload and provenance=='Unknown' and e['passEpoch']==0,'Only original reset may seed unknown state');continue
        require(ordinal>0,'First opaque event must be reset')
        if op=='PassBoundary':
            require(payload==['pass'],'Pass boundary union differs');p=e['pass'];fields(p,'pass sourceBranchProved','source pass')
            require(p['pass'] in ('Reset','Shadow','OpaqueWorld','Particle','Composition','Overlay') and type(p['sourceBranchProved']) is bool,'Unknown source pass')
            epoch+=1;require(e['passEpoch']==epoch and provenance==('SourceProvedOwnerPostcondition' if p['sourceBranchProved'] else 'Unknown'),'Pass epoch/provenance differs')
            pass_name=p['pass'];proved=p['sourceBranchProved'] and pass_name=='OpaqueWorld'
            if not p['sourceBranchProved'] or pass_name=='Composition':states.clear();payloads.clear()
            continue
        require(e['passEpoch']==epoch,'Event borrowed a foreign pass epoch')
        if op=='RootInvalidation':
            require(not payload and provenance=='Unknown','Root invalidation union/provenance differs')
            for old in list(states):
                if old[0] in ('Pipeline','RootSignature','DescriptorHeap','TextureTable','RootParameter'):del states[old]
            payloads.clear();continue
        if op=='RootWords':
            require(payload==['rootWords'],'Root words union differs');r=e['rootWords'];fields(r,'parameter offsetWords wordCount buffered arenaIdentity arenaSlot resourceEpoch sourceOffset ownerSource','root words')
            parameter=number(r['parameter'],'root parameter',0,22);words=number(r['wordCount'],'root write words',1,64)
            for name,maximum in [('arenaIdentity',4096),('arenaSlot',2),('resourceEpoch',2**64-2),('sourceOffset',2**64-1)]:number(r[name],name,0,maximum)
            require(number(r['offsetWords'],'root word offset',0,count)==consumed and consumed+words<=count and type(r['buffered']) is bool,'Root arena range/order differs')
            data=root_bytes[consumed*4:(consumed+words)*4];consumed+=words
            if r['buffered']:
                require(provenance=='SourceProvedOwnerPostcondition' and r['ownerSource'] in ('BufferedVertexConstant','ShadowReceiverImplicit'),'Buffered root provenance differs')
                arena=identity(number(r['arenaIdentity'],'root arena',1,4096),'ImplicitArena');current=known('RootParameter',parameter)
                require(current['identity']==arena['id'] and current['a']==r['sourceOffset'] and current['b']>=words*4 and current['c']==r['resourceEpoch']==arena['epoch'] and current['d']==r['arenaSlot']==arena['slot']==o['frameIndex'],'Buffered root data borrowed another effective CBV/slot/epoch')
                number(r['sourceOffset'],'buffered source offset',0,arena['bytes']);require(r['sourceOffset']%256==0 and r['sourceOffset']+current['b']<=arena['bytes'],'Buffered root range differs')
                require((r['ownerSource']=='BufferedVertexConstant' and parameter==0 and r['sourceOffset']>=256) or (r['ownerSource']=='ShadowReceiverImplicit' and r['sourceOffset']==0 and words==24),'Buffered root owner source differs')
                payloads[parameter,arena['id'],r['sourceOffset']]=dict(words=words,sha256=sha256_bytes(data),rawLittleEndianBase64=base64.b64encode(data).decode())
            else:
                require(provenance=='CpuNativeCallReturn' and r['ownerSource'] is None and all(type(r[k]) is int and r[k]==0 for k in ('arenaIdentity','arenaSlot','resourceEpoch','sourceOffset')),'Immediate root borrowed buffered source')
                states['RootParameter',parameter]=dict(identity=0,a=r['offsetWords'],b=words,c=0,d=0,known=2)
                payloads[parameter,0,r['offsetWords']]=dict(words=words,sha256=sha256_bytes(data),rawLittleEndianBase64=base64.b64encode(data).decode())
            continue
        if op in ('Draw','DrawIndexed'):
            require(payload==['draw'] and provenance=='CpuNativeCallReturn' and pass_name=='OpaqueWorld' and proved,'Draw lacks source-proved opaque native-return phase');d=e['draw'];fields(d,'indexed count instances start baseVertex startInstance','draw arguments')
            require(type(d['indexed']) is bool and d['indexed']==(op=='DrawIndexed'),'Indexed draw union differs')
            for k in ('count','instances'):number(d[k],k,1,2**31-1)
            for k in ('start','startInstance'):number(d[k],k,0,2**31-1)
            number(d['baseVertex'],'base vertex',-2**31,2**31-1);require(d['indexed'] or d['baseVertex']==0,'Nonindexed base vertex differs')
            pair=(known('Pipeline')['identity'],known('RootSignature')['identity']);require(pair in catalog,'Effective pipeline/root lacks committed catalog');c=catalog[pair];required=c['required']
            topology=known('Topology')['identity'];vp=known('Viewport');scissor=known('Scissor');rt=known('RenderTarget')['identity']
            identity(rt,'RetainedTarget');require(late(rt,'retained-attachment')['description']['attachment']['role']==ATTACHMENTS[0],'Opaque RT is not the retained SceneLinear witness')
            derived=dict(arguments=d,pipeline=c['semantic'],topology=topology,viewport=vp,scissor=scissor,renderTarget=ATTACHMENTS[0],vertexViews={},textures={},roots={})
            if d['indexed']:
                ib=known('IndexBuffer');identity(ib['identity'],'Buffer');require(ib['c'] in (2,4) and ib['b']<=2**32-1 and (d['start']+d['count'])*ib['c']<=ib['b'],'Index view/argument range differs')
                derived['indexView']=view(ib['identity'],ib['a'],ib['b'],'index-buffer')
            for slot in range(32):
                if required['vertexSlots']&(1<<slot):
                    vb=known('VertexBuffer',slot);identity(vb['identity'],'Buffer');require(0<vb['c']<=2**32-1 and vb['b']<=2**32-1,'Vertex view stride/size differs');derived['vertexViews'][str(slot)]=view(vb['identity'],vb['a'],vb['b'],'vertex-'+str(slot))|dict(stride=vb['c'])
            for slot in range(16):
                retained=bool(required['retainedTargetTextureSlots']&(1<<slot))
                if retained or required['textureSlots']&(1<<slot):
                    table=known('TextureTable',slot);heap=known('DescriptorHeap')['identity'];identity(heap,'DescriptorHeap');source=identity(table['identity'],'RetainedTarget' if retained else 'Texture')
                    require(table['c']==heap and table['a']==source['descriptor'] and table['b']==c['tables'][slot] and table['d']==(1 if retained else (source['stride']>>8)&0xffff),'Effective table heap/descriptor/root/view differs')
                    row=late(source['id'],'retained-attachment' if retained else 'texture')['description']
                    if retained:require(row['attachment']['role']==ATTACHMENTS[1],'Implicit table is not retained shadow depth')
                    derived['textures'][str(slot)]=dict(semanticName=row['semanticName'],rootOrdinal=table['b'],view=table['d'],texture=row['texture'])
            for parameter,declaration in c['roots'].items():
                bound=known('RootParameter',parameter)
                if declaration['kind']==1:
                    require(bound['identity']==0 and bound['known']==2 and bound['b']==declaration['words'],'Effective immediate root has wrong kind/declared count')
                    source_payload=payloads.get((parameter,0,bound['a']));require(source_payload is not None,'Missing immediate root payload');derived['roots'][str(parameter)]=dict(kind='immediate',**source_payload)
                else:
                    arena=identity(bound['identity'],'ImplicitArena');require(bound['known']==1 and bound['a']%256==0 and bound['c']==arena['epoch'] and bound['d']==arena['slot']==o['frameIndex'],'Effective CBV identity/range/slot differs')
                    source_payload=payloads.get((parameter,arena['id'],bound['a']));require(source_payload is not None and source_payload['words']*4<=bound['b'],'Missing actual meaningful buffered root words')
                    derived['roots'][str(parameter)]=dict(kind='buffered',view=view(arena['id'],bound['a'],bound['b'],'root-'+str(parameter)),**source_payload)
            if required['depthTarget']:
                target=known('DepthTarget')['identity'];identity(target,'RetainedTarget');require(late(target,'retained-attachment')['description']['attachment']['role']==ATTACHMENTS[2],'Opaque depth lacks retained-final-depth alias');derived['depthTarget']=ATTACHMENTS[2]
            require(len(draws)<4096,'Opaque draw capacity exceeded');draws.append(derived);continue
        require(op in ('Request','CacheHit','NativeReturn','NativeFailure','OwnerPostcondition'),'Unhandled opaque event')
        expected_provenance={'Request':'SetterRequested','CacheHit':'CacheHitDecision','NativeReturn':'CpuNativeCallReturn','NativeFailure':'Unknown','OwnerPostcondition':'SourceProvedOwnerPostcondition'}[op]
        require(provenance==expected_provenance,'Setter/cache/native/owner provenance differs')
        if payload==['viewport']:
            require(op in ('Request','NativeReturn','OwnerPostcondition'),'Viewport union used by unsupported event');v=e['viewport'];fields(v,'xBits yBits widthBits heightBits minimumDepthBits maximumDepthBits ownerSource','viewport bits')
            for name in ('xBits','yBits','widthBits','heightBits','minimumDepthBits','maximumDepthBits'):number(v[name],name,0,2**32-1)
            decoded={name:struct.unpack('<f',struct.pack('<I',v[name]))[0] for name in ('xBits','yBits','widthBits','heightBits','minimumDepthBits','maximumDepthBits')}
            require(all(math.isfinite(x) for x in decoded.values()) and decoded['widthBits']>0 and decoded['heightBits']>0 and 0<=decoded['minimumDepthBits']<=decoded['maximumDepthBits']<=1,'Invalid effective viewport float bits')
            require(v['ownerSource'] in ('InitialTargets','ShadowTargets','ShadowReturnTargets','ParticleTargets','ParticleReturnTargets') if op=='OwnerPostcondition' else v['ownerSource'] is None,'Viewport owner source differs')
            if op!='Request':states['Viewport',0]={k:v[k] for k in v if k!='ownerSource'}
            continue
        require(payload==['binding'],'Binding union differs');b=e['binding'];fields(b,'binding slot identity a b c d ownerSource','typed binding')
        binding=b['binding'];slot=number(b['slot'],'binding slot',0,31);target=key(binding,slot)
        for name in ('identity','a','b','c','d'):number(b[name],name,0,2**64-1)
        if op=='OwnerPostcondition':
            owner=b['ownerSource'];allowed=(binding in ('RenderTarget','DepthTarget','Scissor')) if owner in ('InitialTargets','ShadowTargets','ShadowReturnTargets','ParticleTargets','ParticleReturnTargets') else (binding in ('RootParameter','TextureTable','DescriptorHeap')) if owner in ('ShadowReceiverImplicit','ParticleDepthReceiver') else binding=='RootParameter' and slot==0 if owner=='BufferedVertexConstant' else False
            require(allowed,'Unproved private owner postcondition')
        else:require(b['ownerSource'] is None,'Nonowner event borrowed owner provenance')
        if op in ('CacheHit','NativeFailure'):require(all(b[k]==0 for k in ('identity','a','b','c','d')),'Cache/failure event invented effective operands')
        if op=='Request':
            requested_kind={'Pipeline':('Pipeline',),'RootSignature':('RootSignature',),'DescriptorHeap':('DescriptorHeap',),'VertexBuffer':('Buffer',),'IndexBuffer':('Buffer',),'TextureTable':('Texture','RetainedTarget'),'RootParameter':('ImplicitArena',),'RenderTarget':('RetainedTarget',),'DepthTarget':('RetainedTarget',)}.get(binding)
            if requested_kind and b['identity']!=0:require(b['identity'] in identities and identities[b['identity']]['kind'] in requested_kind,'Requested resource reference is unregistered/wrong kind')
            continue
        if op=='CacheHit':continue
        if op=='NativeFailure':states.pop(target,None);continue
        if binding in ('Pipeline','RootSignature','DescriptorHeap'):identity(b['identity'],binding);require(all(b[k]==0 for k in ('a','b','c','d')),'Scalar native identity has range operands')
        elif binding=='Topology':require(b['identity'] in (2,4) and all(b[k]==0 for k in ('a','b','c','d')),'Unsupported native topology')
        elif binding in ('VertexBuffer','IndexBuffer'):identity(b['identity'],'Buffer')
        elif binding=='TextureTable':require(b['identity'] in identities and identities[b['identity']]['kind'] in ('Texture','RetainedTarget'),'Table source identity differs')
        elif binding in ('RenderTarget','DepthTarget'):
            if b['identity']!=0:identity(b['identity'],'RetainedTarget')
            require(all(b[k]==0 for k in ('a','b','c','d')),'Native target has invented range')
        elif binding=='RootParameter':identity(b['identity'],'ImplicitArena')
        elif binding=='Scissor':
            require(b['identity']==0 and all(b[k]<=2**32-1 for k in ('a','b','c','d')),'Native scissor integer bits differ')
            signed={k:b[k]-(2**32 if b[k]>=2**31 else 0) for k in ('a','b','c','d')}
            require(signed['a']<=signed['c'] and signed['b']<=signed['d'],'Native scissor rectangle differs')
        else:require(False,'Effective viewport must use exact float-bit union')
        effective(binding,slot,{k:b[k] for k in ('identity','a','b','c','d')})
    require(consumed==count and draws,'Unclaimed root bytes or no actual opaque draws')
    return dict(status='validated-source-closed-cpu-opaque-state',draws=draws,viewCoverage=views,identityCount=len(identities),creationCount=len(catalog),shaderFetchCoverageProved=False,nativeInterception=False)


def validate_series(records):
    require(type(records) is list and len(records)==8,'All eight selected snapshots required')
    first=records[0];mode=first.get('coverageMode');version=first['original']['schemaVersion']
    require(type(version) is int and version in (1,2) and (mode is None if version==1 else type(mode) is str and mode in MODES),'Series selection/version differs')
    status='complete-late-resident-subset' if version==1 else 'complete-retained-attachments' if mode==MODES[0] else 'complete-opaque-state-and-retained-attachments'
    require(first['status']==status,'Selected snapshot unavailable')
    prior=None;comparisons=[];copy_domains=set();raw_names=set()
    for i,r in enumerate(records):
        require(r['status']==status and r.get('coverageMode')==mode and same(r['original']['schemaVersion'],version),'Incomplete/mixed-mode selected series');v=r['original'];m=v['metadata'];o=v['originalFrameReceipt']
        receipt(o,snapshot_version=version,mode=mode);copy_receipt(v['copyReceipt'],o)
        require(type(m['repetition']) is int and m['repetition']==i,'Repetition order differs')
        if version==2:
            require(v['coverageMode']==mode and o['invocation']==i+1 and set(r['semanticAttachments'])==set(ATTACHMENTS),'Series mode/invocation/retained attachments differ')
            for resource in v['resources']:
                for raw in resource['rawMips']:
                    name=raw['file'].casefold();require(name not in raw_names,'Selected v2 repetitions reused a canonical raw file');raw_names.add(name)
            if mode==MODES[1]:
                require(same(v['opaqueWitness']['creations'],first['original']['opaqueWitness']['creations']),'Frozen session creation catalog changed')
                if i:
                    previous=records[i-1]['original']['opaqueWitness']['identities'];current=v['opaqueWitness']['identities']
                    require(len(current)>=len(previous) and same(current[:len(previous)],previous),'Session identities were recycled/replaced')
        for key in ('sourceSha','runtimeFingerprint','recipeFingerprint','recipeInputBytesBase64','camera','profileFingerprint','observedComposition'):require(same(m[key],first['original']['metadata'][key]),'Immutable series metadata differs: '+key)
        require(same(o['deviceIdentity'],first['original']['originalFrameReceipt']['deviceIdentity']),'Series device/session differs')
        require(same(o['managedOwnerThreadId'],first['original']['originalFrameReceipt']['managedOwnerThreadId']),'Series managed owner differs')
        domain=v['copyReceipt']['fenceDomain'];require(domain not in copy_domains,'Late copy fence domain reused');copy_domains.add(domain)
        if prior:
            require(o['completedFence']>=prior['completedFence'],'Original completed fence regressed')
            require(o['submission']['submission']==prior['submission']['submission']+1 and o['submission']['submittedFence']>prior['submission']['submittedFence'] and o['presentedBefore']==prior['presentedAfter'],'Original frame chronology differs')
            require(o['submission']['resourceGeneration']==prior['submission']['resourceGeneration'] and o['width']==prior['width'] and o['height']==prior['height'],'Series resource/raster epoch differs')
        prior=o
        comparison={'repetition':i,'frameIndex':o['frameIndex'],'actualSubmission':o['submission'],'semanticResidentBytesEqualToFirst':same(r['semanticResources'],first['semanticResources']),
            'retainedIntentEqualToFirst':same(v['intent'],first['original']['intent'])}
        if version==2:
            comparison.update(coverageMode=mode,retainedAttachmentBytesEqualToFirst={role:same(r['semanticAttachments'][role],first['semanticAttachments'][role]) for role in ATTACHMENTS},
                sourceClosedOpaqueStateEqualToFirst=None if mode==MODES[0] else same(r['opaqueState']['draws'],first['opaqueState']['draws']))
        comparisons.append(comparison)
    return {'status':'retained-diagnostic-comparison','qualified':False,'wave2Ready':False,'comparisons':comparisons,
        'scope':'Late bytes after PNG, process-local roster/slot identities and retained intent only; not shader contents, cause or qualification.' if version==1 else
            'Source-closed CPU opaque state only when explicitly selected, late resident bytes and three retained attachments compared separately; no GPU execution/fetch, cause, repair or qualification proof.'}
