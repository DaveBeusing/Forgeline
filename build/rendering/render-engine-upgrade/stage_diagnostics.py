"""Byte-exact offline stage diagnostics. Mismatches remain failures; never timing admission."""
from __future__ import annotations
import argparse,base64,json,math,struct
from pathlib import Path
from baseline_contracts import require,parse_json_bytes,sha256_bytes,write_new
from baseline_admission import digest,integer,local

STAGES=('ShadowDepth','WorldDepth','SceneLinear','ComposedColor','FinalColor')
FORMATS={'ShadowDepth':{'R32_Typeless':4,'D32_Float':4,'R32_Float':4},'WorldDepth':{'R32_Typeless':4,'D32_Float':4,'R32_Float':4},
 'SceneLinear':{'R16G16B16A16_Float':8},'ComposedColor':{'R8G8B8A8_UNorm':4},'FinalColor':{'R8G8B8A8_UNorm':4}}

def finite(value):
    if type(value) in (int,float):require(math.isfinite(value),'Nonfinite actual camera/profile value')
    elif type(value) is dict:
        for v in value.values():finite(v)
    elif type(value) is list:
        for v in value:finite(v)

def bounded_bytes(path,limit):
    require(path.is_file() and not path.is_symlink() and path.stat().st_size<=limit,'Missing/oversized/symlink diagnostic artifact')
    return path.read_bytes()

def read_capture(directory,reference):
    metadata_path=local(directory,reference['path'])
    raw=bounded_bytes(metadata_path,1024*1024)
    require(sha256_bytes(raw)==digest(reference['sha256'],'stage metadata'),'Stage metadata hash differs')
    r=parse_json_bytes(raw)
    require(type(r.get('schemaVersion')) is int and r['schemaVersion']==1 and r.get('kind')=='renderer-stage-readback' and r.get('qualified') is False,'Wrong diagnostic record contract')
    require(r.get('producer') in ('native-reference','actual-render-host'),'Unsupported actual stage producer')
    for name,n in [('sourceSha',40),('runtimeFingerprint',64),('recipeFingerprint',64),('inputFingerprint',64)]:digest(r.get(name),name,n)
    integer(r.get('repetition'),'repetition');integer(r.get('frameIndex'),'actual frame slot')
    require(r['frameIndex']<16,'Invalid actual frame slot')
    submission=r['submission']
    for key in ('submission','submittedFence','generation','resourceGeneration'):integer(submission.get(key),key,1)
    require(integer(r.get('completedFence'),'actual completed fence',1)>=submission['submittedFence'] and r['completedFence']<2**64-1,'Capture fence did not actually complete')
    require(integer(r.get('presentedBefore'),'actual present count')+1==integer(r.get('presentedAfter'),'actual present count',1),'Capture did not present exactly once')
    device=r['deviceIdentity'];require(integer(device.get('generation'),'actual device generation',1)==submission['generation'],'Capture device generation differs')
    require(type(device.get('schemaVersion')) is int and device['schemaVersion']==1 and type(device.get('source')) is str and device['source'],'Actual native device identity contract unavailable')
    integer(device.get('nodeCount'),'actual node count',1);integer(device.get('queueNodeMask'),'actual queue node mask');integer(device.get('effectiveNodeMask'),'actual effective node mask',1)
    integer(device.get('processId'),'actual producer PID',1);integer(device.get('processStartedFileTimeUtc'),'actual process creation',1)
    digest(device.get('sessionId'),'actual device session',32);require(type(device.get('adapterLuidHex')) is str and len(device['adapterLuidHex'])==16,'Actual adapter LUID unavailable')
    camera=r.get('camera');profile=r.get('effectiveProfile')
    require(type(camera) is dict and set(camera)=={'target','yawRadians','pitchRadians','distance','hasPointerPosition','pointerPosition'},'Complete actual camera state unavailable')
    for vector,keys in ((camera['target'],{'x','y','z'}),(camera['pointerPosition'],{'x','y'})):
        require(type(vector) is dict and keys<=set(vector) and all(type(vector[k]) in (int,float) for k in keys),'Actual camera vector coordinates missing')
    require(all(type(camera[k]) in (int,float) for k in ('yawRadians','pitchRadians','distance')) and camera['distance']>0 and type(camera['hasPointerPosition']) is bool,'Actual camera scalars invalid')
    require(type(profile) is dict and {'requested','active','linearScene','shadows','atmosphere','particles','terrainDetail','lodDiameterScale','postProcessing','fallbackReason','resolutionScale','textures','vegetationMotion','requestedSettings','environment','vegetation','terrainResidency'}<=set(profile),'Complete effective profile unavailable')
    require(profile['linearScene'] is True and type(profile['resolutionScale']) in (int,float) and 0<profile['resolutionScale']<=1,'All-stage diagnostics require actual linear scene configuration')
    finite(camera);finite(profile)
    rows=r.get('stages');require(type(rows) is list and len(rows)==len(STAGES) and [row.get('stage') for row in rows]==list(STAGES),'Every stage in actual pipeline order is required')
    logical={};files=set()
    for row in rows:
        stage=row['stage'];layout=row['layout'];layout_raw=base64.b64decode(row['layoutBytesBase64'],validate=True)
        require(sha256_bytes(layout_raw)==digest(row['layoutSha256'],'layout') and parse_json_bytes(layout_raw)==layout,'Raw footprint layout/hash differs')
        width=integer(layout.get('width'),'width',1);height=integer(layout.get('height'),'height',1)
        require(width<=8192 and height<=8192,'Diagnostic dimensions exceed supported bounded surface')
        bpp=FORMATS[stage].get(layout.get('format'));require(bpp is not None and type(row.get('bytesPerTexel')) is int and row['bytesPerTexel']==bpp,'Stage format/texel size differs')
        pitch=integer(layout.get('rowPitch'),'row pitch',1);offset=integer(layout.get('offset'),'placed offset')
        require(pitch>=width*bpp and pitch%256==0 and offset%512==0,'Malformed actual D3D12 footprint alignment')
        file=local(metadata_path.parent,row['file']);require(file not in files,'Stage artifacts cannot alias');files.add(file)
        data=bounded_bytes(file,512*1024*1024)
        require(type(row.get('rawBytes')) is int and len(data)==row['rawBytes'] and sha256_bytes(data)==digest(row['rawSha256'],'raw stage'),'Raw stage bytes/hash differ')
        require(offset+(height-1)*pitch+width*bpp<=len(data),'Stage footprint escapes original retained bytes')
        texels=b''.join(data[offset+y*pitch:offset+y*pitch+width*bpp] for y in range(height))
        require(type(row.get('logicalBytes')) is int and len(texels)==row['logicalBytes'] and sha256_bytes(texels)==digest(row['logicalSha256'],'logical stage'),'Logical texel hash/length differs')
        logical[stage]=(layout,texels,bpp)
    require(logical['ComposedColor'][0]==logical['FinalColor'][0],'Composed/final output layouts differ')
    recipe_bytes=base64.b64decode(r.get('recipeInputBytesBase64',''),validate=True);recipe=parse_json_bytes(recipe_bytes)
    require(sha256_bytes(recipe_bytes)==digest(r['recipeFingerprint'],'actual recipe'),'Original recipe bytes differ from stage identity')
    raster=r.get('outputRaster');require(type(raster) is dict and set(raster)=={'width','height','bufferCount'},'Actual output raster metadata missing')
    width=integer(raster['width'],'actual output width',1);height=integer(raster['height'],'actual output height',1)
    buffers=integer(raster['bufferCount'],'actual buffer count',1);require(buffers==3 and r['frameIndex']<buffers,'Actual fixed three-buffer slot differs')
    require(recipe.get('display')==dict(width=width,height=height) and recipe.get('settings')==profile['requestedSettings'] and digest(recipe.get('runtimeFingerprint'),'recipe runtime')==r['runtimeFingerprint'].lower(),'Actual raster/requested profile/runtime differs from original recipe')
    final=logical['FinalColor'][0];require((final['width'],final['height'])==(width,height),'Final/composed raster differs from actual output dimensions')
    # C# promotes the original float32 resolution scale to double before Ceil.
    scale=struct.unpack('<f',struct.pack('<f',profile['resolutionScale']))[0]
    expected=(max(1,math.ceil(width*scale)),max(1,math.ceil(height*scale)))
    require(all((logical[stage][0]['width'],logical[stage][0]['height'])==expected for stage in ('WorldDepth','SceneLinear')),'Precomposition depth/FP16 dimensions differ from actual renderer scaled rounding')
    shadows=profile['shadows'];require(type(shadows) is dict and shadows.get('enabled') is True,'Requested shadow attachment has no actual enabled shadow profile')
    resolution={'Low':1024,'Medium':2048,'High':4096}.get(shadows.get('quality'))
    require(resolution is not None and type(shadows.get('resolution')) is int and shadows['resolution']==resolution and (logical['ShadowDepth'][0]['width'],logical['ShadowDepth'][0]['height'])==(resolution,resolution),'Shadow footprint differs from actual shadow quality resolution')
    return r,logical

def difference(left,right,layout,bpp):
    width,height=layout['width'],layout['height'];row_bytes=width*bpp;first=None;count=0
    for y in range(height):
        a,b=left[y*row_bytes:(y+1)*row_bytes],right[y*row_bytes:(y+1)*row_bytes]
        if a==b:continue
        for x,(v,w) in enumerate(zip(a,b)):
            if v!=w:
                count+=1
                if first is None:first=(x//bpp,y,x%bpp,v,w)
    if first is None:return dict(equal=True,differingBytes=0,firstDifference=None,neighborhood=[])
    x,y,byte,a,b=first;near=[]
    for yy in range(max(0,y-1),min(height,y+2)):
        for xx in range(max(0,x-1),min(width,x+2)):
            start=(yy*width+xx)*bpp;near.append(dict(x=xx,y=yy,leftHex=left[start:start+bpp].hex(),rightHex=right[start:start+bpp].hex()))
    return dict(equal=False,differingBytes=count,firstDifference=dict(x=x,y=y,byteWithinTexel=byte,left=a,right=b),neighborhood=near)

def compare_series(directory,references,expected_repeats=8,*,inspector_references=None,inspector_version=None):
    require(type(expected_repeats) is int and expected_repeats==8,'This frozen diagnostic retains all eight readbacks')
    require(type(references) is list and len(references)==expected_repeats,'All eight immutable stage records are required')
    require(len({r['path'] for r in references})==expected_repeats,'Readback references cannot repeat')
    captures=[read_capture(directory,r) for r in references];first=captures[0][0]
    immutable=('producer','sourceSha','runtimeFingerprint','recipeFingerprint','inputFingerprint','camera','effectiveProfile','deviceIdentity')
    inspector=None
    if inspector_version is not None or inspector_references is not None:
        from world_input_diagnostics import validate_loaded
        inspector=validate_loaded(directory,references,captures,inspector_references,inspector_version)
    previous=None;comparisons=[];all_equal=True;seen=set()
    for i,(record,logical) in enumerate(captures):
        require(type(record['repetition']) is int and record['repetition']==i,'Readback ordinal is missing/duplicated/reordered')
        require(all(record[k]==first[k] for k in immutable),'Source/runtime/actual input/recipe/profile/pose/device changed between repeats')
        current=record['submission'];key=tuple(current[k] for k in ('generation','resourceGeneration','submittedFence'))
        require(key not in seen,'A prior completed submission cannot be reused');seen.add(key)
        if previous is not None:
            require(current['submission']==previous['submission']['submission']+1 and current['submittedFence']>previous['submission']['submittedFence'] and record['presentedBefore']==previous['presentedAfter'] and current['resourceGeneration']==previous['submission']['resourceGeneration'],'Readback submission/presentation chronology differs')
        previous=record
        stages=[];earliest=None
        for name in STAGES:
            layout,texels,bpp=logical[name];baseline_layout,baseline,_=captures[0][1][name]
            require(layout==baseline_layout,'Actual stage footprint changed between repeats')
            d=difference(baseline,texels,layout,bpp);stages.append(dict(stage=name,**d))
            if not d['equal'] and earliest is None:earliest=name
        if earliest:all_equal=False
        comparisons.append(dict(repetition=i,frameIndex=record['frameIndex'],submission=current,firstDivergentStage=earliest,stages=stages))
    return dict(schemaVersion=1,kind='offline-render-stage-comparison',qualified=False,status='equal' if all_equal else 'mismatch',
        sourceSha=first['sourceSha'],runtimeFingerprint=first['runtimeFingerprint'],recipeFingerprint=first['recipeFingerprint'],
        repeatCount=len(captures),comparisons=comparisons,worldInputInspector=inspector if inspector is not None else dict(available=False,status='historical-unavailable'),interpretation='First divergent observed boundary; not shader/driver cause, tolerance, speed or baseline acceptance')

def verified_lane_binding(directory,reference,actual):
    from baseline_admission import load
    from adapter_identity import bind_adapter,normalized_pci
    from provider import parse_inventory
    require(type(reference) is dict,'Cross-lane physical binding references are required')
    checkpoint_path=local(directory,reference['checkpoint']['path']);raw=bounded_bytes(checkpoint_path,1024*1024)
    require(sha256_bytes(raw)==digest(reference['checkpoint']['sha256'],'original checkpoint'),'Original checkpoint bytes differ')
    checkpoint=parse_json_bytes(raw);binding=load(directory,reference['binding']);cuda=load(directory,reference['cudaInventory'])
    sensor=bounded_bytes(local(directory,reference['sensorRaw']['path']),1024*1024)
    require(sha256_bytes(sensor)==digest(reference['sensorRaw']['sha256'],'original sensor raw'),'Original physical inventory differs')
    recomputed=bind_adapter(raw,cuda,parse_inventory(sensor),binding['processBefore'],binding['processAfter'],binding['commandContext'])
    require(recomputed['exactPhysicalMappingAvailable'] is True and all(binding.get(k)==v for k,v in recomputed.items()),'Original physical/lifetime binding cannot be recomputed')
    require(checkpoint['adapterIdentity']==actual['deviceIdentity'] and checkpoint['sourceSha']==actual['sourceSha'] and str(checkpoint['runtimeFingerprint']).lower()==actual['runtimeFingerprint'].lower(),'Stage capture belongs to another producer process/device/source/runtime')
    return (binding['uuid'],normalized_pci(binding['pciBusId']))

def compare_lanes(directory,native_references,host_references,native_binding,host_binding):
    # Process/session/slot/fence identities are checked inside each series, never aliased across runs.
    within_native=compare_series(directory,native_references);within_host=compare_series(directory,host_references)
    native=[read_capture(directory,r) for r in native_references];host=[read_capture(directory,r) for r in host_references]
    require(native[0][0]['producer']=='native-reference' and host[0][0]['producer']=='actual-render-host','Cross-lane actual producers are reversed/unsupported')
    require(verified_lane_binding(directory,native_binding,native[0][0])==verified_lane_binding(directory,host_binding,host[0][0]),'Original native/host physical UUID and full PCI differ')
    comparisons=[];equal=within_native['status']=='equal' and within_host['status']=='equal'
    for i,((a,aa),(b,bb)) in enumerate(zip(native,host)):
        require(all(a[k]==b[k] for k in ('sourceSha','runtimeFingerprint','recipeFingerprint','inputFingerprint','camera','effectiveProfile')),'Actual cross-lane source/runtime/recipe/input/profile/pose differs')
        stages=[];earliest=None
        for name in STAGES:
            la,ba,bpp=aa[name];lb,blob,_=bb[name]
            require((la['width'],la['height'],la['format'])==(lb['width'],lb['height'],lb['format']),'Cross-lane logical stage dimensions/formats differ')
            result=difference(ba,blob,la,bpp);stages.append(dict(stage=name,**result))
            if not result['equal'] and earliest is None:earliest=name
        if earliest:equal=False
        comparisons.append(dict(repetition=i,nativeSubmission=a['submission'],hostSubmission=b['submission'],nativeFrameIndex=a['frameIndex'],hostFrameIndex=b['frameIndex'],firstDivergentStage=earliest,stages=stages))
    return dict(schemaVersion=1,kind='offline-native-host-stage-comparison',qualified=False,status='equal' if equal else 'mismatch',
        nativeWithinSeries=within_native,hostWithinSeries=within_host,comparisons=comparisons,
        interpretation='Corresponding eight boundaries on independently bound physical runs; no tolerance, performance or baseline acceptance')

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--index',required=True);p.add_argument('--output',required=True);a=p.parse_args()
    try:
        path=Path(a.index).resolve();index=parse_json_bytes(bounded_bytes(path,1024*1024));value=compare_lanes(path.parent,index['nativeRecords'],index['hostRecords'],index['nativeBinding'],index['hostBinding']) if 'nativeRecords' in index else compare_series(path.parent,index['records']);write_new(a.output,value);return 0 if value['status']=='equal' else 2
    except (OSError,ValueError,KeyError,TypeError,IndexError) as error:
        write_new(a.output,dict(schemaVersion=1,kind='offline-render-stage-comparison',status='unavailable',qualified=False,reason=str(error)));return 2
if __name__=='__main__':raise SystemExit(main())
