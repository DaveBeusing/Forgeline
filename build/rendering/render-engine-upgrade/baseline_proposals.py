"""Actual preflight-bound full protocol proposals. Never publishes accepted policy/protocol kinds."""
from __future__ import annotations
import base64,copy,math,re
from pathlib import Path
from itertools import product
from diagnostic_paths import relative as windows_relative,absolute as windows_absolute,key as windows_key
from baseline_contracts import require,parse_json_bytes,sha256_bytes,canonical,utc
from baseline_admission import digest,integer,local,load,git,signature,required_cases,validate_protocol_body

PROPOSAL='complete-baseline-control-protocol-proposal'

def finite_number(value,name):
    require(type(value) in (int,float) and math.isfinite(value),name+' must be a finite nonboolean number')

def vector(value,axes,name):
    require(type(value) is dict and set(value)==set(axes),name+' vector shape differs')
    for axis in axes:finite_number(value[axis],name+'.'+axis)

def camera(value):
    require(type(value) is dict and set(value)=={'target','yawRadians','pitchRadians','distance','hasPointerPosition','pointerPosition'},'Actual camera shape is incomplete')
    vector(value['target'],'xyz','camera target');vector(value['pointerPosition'],'xy','camera pointer')
    for key in ('yawRadians','pitchRadians','distance'):finite_number(value[key],'camera '+key)
    require(value['distance']>0 and type(value['hasPointerPosition']) is bool,'Actual camera distance/pointer flag invalid')

def settings_tree(value,name):
    require(type(value) is dict and value,name+' actual settings object missing')
    def walk(item):
        if type(item) is dict:
            require(bool(item),'Empty nested actual setting')
            for child in item.values():walk(child)
        elif type(item) is list:
            for child in item:walk(child)
        else:require(item is None or type(item) in (str,bool) or type(item) in (int,float) and math.isfinite(item),'Nonfinite/unsupported actual setting value')
    walk(value)

QUALITY=('Low','Medium','High','Ultra')
ATMOSPHERE=('Disabled','Distance','Height')
AA=('Disabled','Low','High')
SHADOW=('Disabled','Low','Medium','High')
TEXTURE=('Full','Half','Quarter')

def typed_settings(value,numbers=(),booleans=(),enums=None):
    enums=enums or {};require(type(value) is dict and set(value)==set(numbers)|set(booleans)|set(enums),'Actual nested settings fields differ')
    for name in numbers:finite_number(value[name],name)
    for name in booleans:require(type(value[name]) is bool,'Actual nested boolean differs: '+name)
    for name,choices in enums.items():require(type(value[name]) is str and value[name] in choices,'Unknown actual nested enum: '+name)

def postprocessing(value):
    typed_settings(value,('whiteBalance','tint','contrast','saturation','edgeBlend'),enums={'antialiasing':AA})

def world_settings(value):
    typed_settings(value['environment'],('daylight','cloudCover','wetness'))
    typed_settings(value['vegetation'],('density','maximumDistanceMeters'))
    typed_settings(value['terrainResidency'],booleans=('enabled',),numbers=('maximumResidentChunks',))
    integer(value['terrainResidency']['maximumResidentChunks'],'resident chunks',1)

def requested_settings(value):
    fields={'quality','colorGrade','resolutionScale','antialiasing','shadows','textures','objectDetail','surfaceDetail','vegetationMotion','atmosphere','particles','environment','vegetation','terrainResidency'}
    require(type(value) is dict and set(value)==fields,'Actual requested settings fields differ')
    require(value['quality'] in QUALITY,'Actual requested quality enum differs');postprocessing(value['colorGrade']);world_settings(value)
    for name,choices in (('antialiasing',AA),('shadows',SHADOW),('textures',TEXTURE),('objectDetail',QUALITY),('atmosphere',ATMOSPHERE),('particles',SHADOW)):
        require(value[name] is None or type(value[name]) is str and value[name] in choices,'Unknown nullable requested enum: '+name)
    for name in ('surfaceDetail','vegetationMotion'):require(value[name] is None or type(value[name]) is bool,'Invalid nullable requested flag')
    if value['resolutionScale'] is not None:finite_number(value['resolutionScale'],'requested resolution scale')

def actual_settings(case):
    profile=case['effectiveProfile']
    fields={'requested','active','linearScene','shadows','atmosphere','particles','terrainDetail','lodDiameterScale','postProcessing','fallbackReason','resolutionScale','textures','vegetationMotion','requestedSettings','environment','vegetation','terrainResidency'}
    require(type(profile) is dict and set(profile)==fields,'Actual effective profile fields are incomplete')
    require(profile['requested']==case['quality'] and profile['active'] in ('Low','Medium','High','Ultra') and type(profile['linearScene']) is bool and type(profile['vegetationMotion']) is bool,'Actual selected profile/request differs')
    require(profile['fallbackReason'] is None or type(profile['fallbackReason']) is str,'Actual fallback reason type differs')
    for name in ('shadows','particles','terrainDetail','postProcessing','requestedSettings','environment','vegetation','terrainResidency'):settings_tree(profile[name],name)
    requested_settings(profile['requestedSettings']);world_settings(profile)
    require(profile['requestedSettings']['quality']==case['quality'],'Actual requested quality differs')
    require(profile['atmosphere'] in ATMOSPHERE and profile['textures'] in TEXTURE,'Unknown actual effective atmosphere/texture enum')
    typed_settings(profile['shadows'],('resolution','cascadeCount','radius','maximumCasters','maximumDraws'),('enabled',),{'quality':SHADOW})
    require(profile['shadows']['enabled']==(profile['shadows']['quality']!='Disabled'),'Shadow enabled/quality differs')
    typed_settings(profile['particles'],('capacity','maximumFallbackEffects','maximumScreenCoverage'),enums={'quality':SHADOW})
    for name in ('capacity','maximumFallbackEffects'):integer(profile['particles'][name],name)
    for name in ('resolution','cascadeCount','maximumCasters','maximumDraws'):integer(profile['shadows'][name],name)
    typed_settings(profile['terrainDetail'],('microTileMeters','microColorStrength','microNormalStrength','rockSlopeStart','rockSlopeEnd'),('enabled',))
    postprocessing(profile['postProcessing'])
    for name in ('lodDiameterScale','resolutionScale'):
        finite_number(profile[name],name);require(profile[name]>0,name+' must be positive')
    lighting=case['lighting'];settings_tree(lighting,'lighting')
    require(set(lighting)=={'directionToLight','directionalIntensity','directionalColor','ambientIntensity','ambientColor','exposure','groundAmbientFactor','toneMapping'},'Actual lighting fields incomplete')
    for name in ('directionToLight','directionalColor','ambientColor'):vector(lighting[name],'xyz',name)
    for name in ('directionalIntensity','ambientIntensity','exposure','groundAmbientFactor'):finite_number(lighting[name],name)
    require(lighting['toneMapping'] in ('None','AcesFitted'),'Actual tone mapping enum unavailable')
    atmosphere=case['atmosphere'];settings_tree(atmosphere,'atmosphere')
    require(set(atmosphere)=={'quality','color','density','startDistance','baseHeight','heightFalloff','maximumOpacity','zoomCompensation','localHaze','enabled'},'Actual atmosphere fields incomplete')
    require(profile['atmosphere']==atmosphere['quality'],'Actual effective/composed atmosphere qualities differ')
    vector(atmosphere['color'],'xyz','atmosphere color')
    for name in ('density','startDistance','baseHeight','heightFalloff','maximumOpacity','zoomCompensation'):finite_number(atmosphere[name],name)
    require(atmosphere['quality'] in ATMOSPHERE and type(atmosphere['enabled']) is bool and atmosphere['enabled']==(atmosphere['quality']!='Disabled'),'Actual atmosphere enum/flag unavailable')
    if atmosphere['localHaze'] is not None:
        haze=atmosphere['localHaze'];require(type(haze) is dict and set(haze)=={'center','radiusMeters','density'},'Actual local haze fields differ');vector(haze['center'],'xyz','haze center');finite_number(haze['radiusMeters'],'haze radius');finite_number(haze['density'],'haze density')
    camera(case['initialCamera']);require(type(case['sampleCameras']) is list and len(case['sampleCameras'])==case['samples'],'Actual sampled pose sequence incomplete')
    for pose in case['sampleCameras']:camera(pose)

def evidence_ref(value,name):
    require(type(value) is dict and set(value)=={'path','sha256'},name+' must be an exact hash-bound reference')
    windows_relative(value['path']);digest(value['sha256'],name);return value

def actual_tool_source(repository):
    root=Path(repository).resolve();script=Path(__file__).resolve()
    require(script.is_relative_to(root),'Executing proposal module does not belong to the supplied repository')
    require(not git(root,'status','--porcelain','--untracked-files=normal').strip(),'Complete proposal assembly needs clean committed tool source')
    head=git(root,'rev-parse','HEAD').decode().strip();relative=script.relative_to(root).as_posix()
    blob=git(root,'show',head+':'+relative);raw=script.read_bytes()
    require(raw.replace(b'\r\n',b'\n')==blob,'Actual executing proposal source differs from the attributed Git tree')
    return head,dict(path=relative,sha256=sha256_bytes(raw),gitBlobSha=git(root,'rev-parse',head+':'+relative).decode().strip(),bytesBase64=base64.b64encode(raw).decode())

def payload(rows):
    require(type(rows) is list and rows,'Complete actual executable payload inventory missing')
    names=[]
    for row in rows:
        require(type(row) is dict and set(row)=={'path','bytes','sha256'},'Unknown executable inventory schema')
        name=row['path'];windows_relative(name)
        names.append(name);integer(row['bytes'],'payload bytes');require(row['sha256']==digest(row['sha256'],'payload'),'Executable inventory hashes must be canonical lowercase')
    require(len({windows_key(windows_relative(n)) for n in names})==len(names) and names==sorted(names,key=lambda v:v.encode('utf-16-be')),'Duplicate/unordered canonical executable inventory')
    return rows

def preflight(root,reference,source):
    evidence_ref(reference,'actual preflight');value=load(root,reference)
    require(type(value.get('schemaVersion')) is int and value['schemaVersion']==1 and value.get('kind')=='actual-fixture-preflight-proposal' and value.get('accepted') is False and value.get('qualified') is False,'Actual proposal-only fixture preflight required')
    require(value.get('sourceSha')==source and str(value.get('buildIdentity','')).lower().endswith('+'+source),'Actual preflight source/build differs')
    require(value.get('compositionApplied') is False and value.get('renderingValidated') is False and type(value.get('submittedOrPresentedFrames')) is int and value['submittedOrPresentedFrames']==0,'Preflight must preserve no-frame/not-applied scope')
    before=payload(value['payloadBefore']);after=payload(value['payloadAfter']);require(canonical(before)==canonical(after),'Actual preflight executable inventory changed')
    assemblies=[r for r in before if r['path']=='ForgeLine.Client.dll'];require(len(assemblies)==1 and assemblies[0]['sha256']==digest(value.get('clientAssemblySha256'),'actual assembly'),'Actual preflight assembly is absent/different in its executable inventory')
    runtime=digest(value.get('runtimeFingerprint'),'actual preflight runtime')
    case=copy.deepcopy(value['case']);raw=base64.b64decode(case['recipeInputBytesBase64'],validate=True)
    require(sha256_bytes(raw)==digest(case['recipeFingerprint'],'actual recipe') and parse_json_bytes(raw)==case['recipe'] and digest(case['recipe'].get('runtimeFingerprint'),'recipe runtime')==runtime,'Preflight recipe bytes/runtime differ')
    request_bytes=base64.b64decode(value['requestBytesBase64'],validate=True);request=parse_json_bytes(request_bytes)
    require(sha256_bytes(request_bytes)==digest(value['requestSha256'],'actual request') and request['commit'].lower()==source,'Actual preflight request/source differs')
    require(request['scenes']==[case['scene']] and request['profiles']==[case['quality']] and request['zooms']==[case['zoom']] and request['displays']==[dict(width=case['width'],height=case['height'])],'Preflight case differs from actual single-case request')
    require(request.get('runProfile','Warm')==case['runProfile'] and request.get('cameraPath','Fixed')==case['cameraPath'] and request.get('uiScenarios',['World'])==[case['uiScenario']],'Original preflight path/run/UI differs')
    for name,default in (('warmups',32),('samples',128),('repeatCaptures',8),('rasterDpi',0)):
        integer(request.get(name,default),'actual request '+name)
    require(request.get('samples',128)==case['samples'] and request.get('repeatCaptures',8)==8 and request.get('warmups',32)==64 and case['warmups']==(0 if case['runProfile']=='RendererCold' else 64),'Actual requested window/readbacks differ')
    require(type(request.get('rasterDpi')) is int and request['rasterDpi']==96,'Full protocol needs explicit requested rasterDpi96; default0 case keys cannot be rewritten')
    dpi=request.get('rasterDpi',0) or (192 if case['height']==2160 else 144 if case['height']==1440 else 96)
    require(dpi==case['rasterDpi'],'Actual raster DPI differs')
    require(all(type(request.get(k,False)) is bool for k in ('software','continueAfterMismatch','captureStageDiagnostics','captureShadowDiagnostics')) and all(request.get(k,False) is False for k in ('continueAfterMismatch','captureStageDiagnostics','captureShadowDiagnostics')) and request.get('ownerTraceControlDirectory') is None,'Malformed/request-only diagnostic mode in no-frame preflight')

    require(value.get('origin',{}).get('kind')=='authored-reference-fixture' and value['origin'].get('actualGameplayAttestationAvailable') is False,'Authored recipe cannot supply actual gameplay origin')
    require(value['origin'].get('scene')==case['scene'] and value['origin'].get('snapshotFingerprint')==case['recipe'].get('snapshotFingerprint'),'Actual scene/snapshot origin differs')
    caps=value['capabilityInputs'];require(all(type(caps.get(k)) is bool for k in ('supportsPostProcessing','supportsParticleRendering','supportsDirectionalShadows','supportsAtmosphere','isSoftwareAdapter')),'Actual capability booleans unavailable')
    require(integer(caps.get('bufferCount'),'actual fixed buffer count',1)==3,'Actual preflight must retain fixed three-buffer configuration')
    budget=caps.get('availableMemoryBudgetBytes');require('availableMemoryBudgetBytes' in caps and (budget is None or type(budget) is int and 0<=budget<=2**64-1),'Actual nullable ulong budget type/range differs')
    surface=caps.get('actualSurface');require(type(surface) is dict,'Actual surface evidence missing')
    for name,expected in (('width',case['width']),('height',case['height']),('bufferCount',3)):
        require(type(surface.get(name)) is int and surface[name]==expected,'Actual surface differs from requested preflight raster/buffers')
    require(type(surface.get('frameIndex')) is int and 0<=surface['frameIndex']<3,'Actual surface frame slot invalid')
    for name in ('isSuspended','isOccluded','resizePending'):require(type(surface.get(name)) is bool,'Actual surface flag missing/malformed')
    require(type(surface.get('presentMode')) is str and bool(surface['presentMode']),'Actual present mode unavailable')
    for name in ('resizeGeneration','appliedResizeGeneration','submittedFrameCount','presentedFrameCount'):integer(surface.get(name),'actual surface '+name)
    require(surface['submittedFrameCount']==0 and surface['presentedFrameCount']==0,'No-frame preflight surface reports submitted/presented work')
    require(type(case.get('effectiveProfile')) is dict and type(case.get('sampleCameras')) is list and len(case['sampleCameras'])==(128 if case['runProfile']=='RendererCold' else 512),'Actual profile/pose sequence unavailable')
    require(case['samples']==len(case['sampleCameras']) and type(case.get('warmups')) is int and case['warmups']==(0 if case['runProfile']=='RendererCold' else 64),'Actual preflight window differs')
    actual_settings(case)
    return value,case

def supplemental_rows(cases,rows):
    require(type(rows) is list and len(rows)==2 and {r.get('mode') for r in rows}=={'actual-live-gameplay','observer-on-off'},'Exactly separate actual gameplay and observer cohorts required')
    ids=set();result={}
    for row in rows:
        key=row.get('id');require(type(key) is str and key and key not in cases and key not in ids,'Supplemental case IDs alias or repeat');ids.add(key)
        if row['mode']=='observer-on-off':
            require(set(row)=={'id','mode','fixtureCaseId'},'Unknown observer supplemental schema')
            fixture=cases.get(row['fixtureCaseId']);require(fixture is not None and fixture['runProfile']=='Warm' and fixture['uiScenario']=='World','Observer controls need an existing warm authored World recipe')
        else:
            require(set(row)=={'id','mode','origin','originFingerprint','originInputBytesBase64','originEvidence'},'Complete actual-gameplay origin record required')
            raw=base64.b64decode(row['originInputBytesBase64'],validate=True);origin=parse_json_bytes(raw)
            require(sha256_bytes(raw)==digest(row['originFingerprint'],'actual composed origin') and origin==row['origin'] and origin.get('kind')=='NewGame','Gameplay origin bytes differ/loaded save unsupported')
            require(origin.get('load') is None and origin.get('smoke') is True and type(origin.get('originVersion')) is int and origin['originVersion']==1 and type(origin.get('declaredAdditionalInstances')) is int and origin['declaredAdditionalInstances']==0 and type(origin.get('seed')) is int and type(origin.get('scenarioKey')) is str and type(origin.get('participants')) is list and origin['participants'],'Actual composed origin is partial')
            evidence_ref(row['originEvidence'],'actual producer origin')
        result[key]=row
    return result

def ordered_keys(pairs,cases,extras):
    keys=[]
    for pair in range(1,pairs+1):
        for arm in (('A','B') if pair%2 else ('B','A')):
            for case in cases:
                for producer in ('native','host'):keys.append((pair,arm,case,producer))
            for key,row in extras.items():keys.append((pair,arm,key,'gameplay' if row['mode']=='actual-live-gameplay' else 'collector-on' if arm=='A' else 'collector-off'))
    return keys

def validate_schedule(schedule,pairs,cases,extras,inventory_root,launch_root):
    inventory_path=windows_relative(inventory_root);launch_path=windows_absolute(launch_root)
    expected=ordered_keys(pairs,cases,extras);require(type(schedule) is list and len(schedule)==len(expected),'Full schedule count differs')
    directories=[];launches=[]
    for row,key in zip(schedule,expected):
        require(type(row) is dict and set(row)=={'pair','arm','caseId','producer','directory','launchOutputPath'},'Unknown/partial schedule row')
        integer(row['pair'],'pair',1);require((row['pair'],row['arm'],row['caseId'],row['producer'])==key,'Schedule differs from deterministic contiguous alternating pair/arm/case/producer order')
        directory=windows_relative(row['directory']);require(windows_key(directory)!=windows_key(inventory_path) and directory.is_relative_to(inventory_path),'Attempt directory escapes/aliases frozen inventory root')
        launch=windows_absolute(row['launchOutputPath']);require(windows_key(launch)!=windows_key(launch_path) and launch.is_relative_to(launch_path),'Original command output escapes frozen launch root')
        directories.append(directory);launches.append(launch)
    for paths in (directories,launches):
        all_paths={windows_key(p) for p in paths};require(len(all_paths)==len(paths),'Attempt output paths alias/repeat on Windows')
        require(all(not any(windows_key(parent) in all_paths for parent in p.parents) for p in paths),'Nested attempt directories can hide failed attempts')
    return len(expected)

def validate_complete_schema(value):
    require(type(value) is dict and type(value.get('schemaVersion')) is int and value['schemaVersion']==1 and value.get('kind')==PROPOSAL,'Complete proposal contract missing')
    require(value.get('accepted') is False and value.get('qualified') is False,'Proposal cannot grant accepted or qualified status')
    required={'schemaVersion','kind','accepted','qualified','sourceInfo','sourceSdkPin','sourceSdkBytesBase64','sourceSdkSha256','payloadInventory','runtimeFingerprint','nativeIdentityEvidence','protocol','planningEstimate','policyReferences'}
    require(set(value) in (required,required|{'validation'}),'Unknown/partial complete proposal fields')
    sdk_raw=base64.b64decode(value['sourceSdkBytesBase64'],validate=True)
    require(sha256_bytes(sdk_raw)==digest(value['sourceSdkSha256'],'source SDK bytes') and parse_json_bytes(sdk_raw)['sdk']['version']==value['sourceSdkPin'],'Original producer-source SDK pin differs')
    info=value['sourceInfo'];digest(info['producerSourceSha'],'immutable producer S',40);digest(info['proposalToolSourceSha'],'proposal builder source',40)
    require(set(info)=={'producerSourceSha','proposalToolSourceSha','proposalToolSourceTreeClean','toolScript','acceptedProtocolSourceSha','resultsReviewSourceSha'} and info['proposalToolSourceTreeClean'] is True,'Actual clean tool source attribution is partial')
    script=info['toolScript'];require(type(script) is dict and set(script)=={'path','sha256','gitBlobSha','bytesBase64'},'Original tool script provenance missing')
    windows_relative(script['path']);digest(script['gitBlobSha'],'tool Git blob',40);raw_script=base64.b64decode(script['bytesBase64'],validate=True)
    require(sha256_bytes(raw_script)==digest(script['sha256'],'original tool bytes'),'Original tool script bytes/hash differ')
    require(info.get('acceptedProtocolSourceSha') is None and info.get('resultsReviewSourceSha') is None,'Proposal cannot fabricate future accepted P/results R source')
    source=info['producerSourceSha'];protocol=value['protocol']
    expected_fields={'schemaVersion','kind','executionSource','frozenAtUtc','proposedAtUtc','comparison','preserveAllAttempts','pairs','regressionPercent','rasterDpi','warmups','warmSamples','coldSamples','cases','supplementalCoverage','attemptInventoryRoot','launchOutputRoot','attemptSchedule','payloadInventory','nativeHardwareIdentitySha256'}
    require(set(protocol)==expected_fields and type(protocol.get('schemaVersion')) is int and protocol['schemaVersion']==1,'Unknown/partial full protocol fields')
    payload(protocol['payloadInventory']);require(canonical(protocol['payloadInventory'])==canonical(value['payloadInventory']),'Actual full executable inventory must be frozen in the protocol itself')
    digest(protocol['nativeHardwareIdentitySha256'],'original native hardware identity')
    evidence_ref(value['nativeIdentityEvidence'],'actual native identity evidence')
    require(protocol.get('kind')=='baseline-control-body-proposal' and protocol.get('executionSource')==source and protocol.get('frozenAtUtc') is None,'Proposal cannot be relabelled accepted or retroactively frozen')
    utc(protocol['proposedAtUtc']);cases=validate_protocol_body(protocol)
    signatures=[signature(c) for c in cases.values()];require(len(set(signatures))==len(signatures),'Different case labels cannot reuse the same recipe signature')
    require(all(type(protocol.get(k)) is int for k in ('pairs','warmups','warmSamples','coldSamples','regressionPercent','rasterDpi')),'Protocol integers cannot be booleans')
    for case in cases.values():
        required_case={'id','scene','quality','zoom','width','height','runProfile','cameraPath','rasterDpi','uiScenario','warmups','samples','recipeFingerprint','recipe','recipeInputBytesBase64','initialCamera','sampleCameras','effectiveProfile','lighting','atmosphere','preflight','coverage'}
        require(set(case) in (required_case,required_case|{'mixedEvidence'}),'Unknown/partial original preflight case fields')
        if case.get('coverage')=='mixed-material-skin-mechanics-effects':evidence_ref(case.get('mixedEvidence'),'actual mixed evidence')
        require(case['recipe'].get('kind')==case['scene'] and case['recipe'].get('display')==dict(width=case['width'],height=case['height']) and case['recipe'].get('rasterDpi')==case['rasterDpi'] and case['recipe'].get('cameraPath')==case['cameraPath'] and case['recipe'].get('samples')==case['samples'],'Case labels/window/raster differ from immutable actual recipe')
        require(case['warmups']==(0 if case['runProfile']=='RendererCold' else 64) and type(case['warmups']) is int and case['samples']==(128 if case['runProfile']=='RendererCold' else 512) and type(case['samples']) is int,'Cold/warm original preflight window differs')

        require(case['scene'] in {s[0] for s in required_cases()} and case['quality'] in ('Medium','High') and case['zoom'] in ('CloseTactical','NormalGameplay','Strategic') and case['runProfile'] in ('Warm','RendererCold') and case['cameraPath'] in ('Fixed','Sweep','Traverse'),'Unsupported/renamed case enum')
        require(type(case.get('width')) is int and type(case.get('height')) is int and type(case.get('rasterDpi')) is int,'Case dimensions/DPI must be actual integers')
        require(case.get('coverage') in ('authored-world','mixed-material-skin-mechanics-effects'),'Unsupported authored coverage label')
        evidence_ref(case.get('preflight'),'actual case preflight')
        actual_settings(case)
    extras=supplemental_rows(cases,protocol['supplementalCoverage'])
    payload(value['payloadInventory']);digest(value['runtimeFingerprint'],'sealed runtime')
    count=validate_schedule(protocol['attemptSchedule'],protocol['pairs'],cases,extras,protocol['attemptInventoryRoot'],protocol['launchOutputRoot'])
    estimates=value.get('planningEstimate');require(type(estimates) is dict and estimates.get('producerSourceSha')==source and type(estimates.get('basis')) is str and estimates['basis'] and type(estimates.get('sourceReferences')) is list and estimates['sourceReferences'],'Source-bound planning duration/storage basis required before freeze')
    for name in ('estimatedSecondsPerCommand','estimatedRetainedBytesPerCommand'):
        v=estimates.get(name);require(type(v) in (int,float) and math.isfinite(v) and v>0,'Finite positive planning estimate missing')
    require(type(value.get('policyReferences')) is dict and set(value['policyReferences'])=={'hardware','allocation','gpuMemory'},'All three explicit future policy references required')
    for reference in value['policyReferences'].values():
        evidence_ref(reference,'proposed policy');require(reference['path'].startswith('docs/rendering/render-engine-upgrade/policies/'),'Policy reference outside its owned policy directory')
    for reference in estimates['sourceReferences']:evidence_ref(reference,'planning estimate basis')
    require('validation' not in value,'Self-reported validation claims are not part of a protocol proposal')
    return dict(status='schema-complete-proposal',originalInputsValidation='unavailable-until-reference-resolution',accepted=False,qualified=False,commands=count,
        estimatedCommandSeconds=count*estimates['estimatedSecondsPerCommand'],estimatedRetainedBytes=count*estimates['estimatedRetainedBytesPerCommand'],
        interpretation='Schema/actual-input preparation only; policy approvals, accepted master P, exact S CI and physical/measurement admissions remain separate')

def assemble(draft,root,repository):
    source=digest(draft['producerSourceSha'],'immutable producer S',40);tool,tool_script=actual_tool_source(repository)
    git(repository,'cat-file','-e',source+'^{commit}')
    source_sdk=git(repository,'show',source+':global.json');sdk=parse_json_bytes(source_sdk)['sdk']['version']
    actuals=[];cases=[]
    for reference in draft['preflightRecords']:
        actual,case=preflight(root,reference,source);actuals.append(actual);case.update(preflight=reference,coverage='authored-world');cases.append(case)
    require(actuals,'Actual source-bound preflights missing')
    require(all(r['payloadBefore']==actuals[0]['payloadBefore'] and digest(r['runtimeFingerprint'],'runtime')==digest(actuals[0]['runtimeFingerprint'],'runtime') for r in actuals),'Preflight source payload/runtime identities differ across cases')
    require(len({c['id'] for c in cases})==len(cases),'Preflight case IDs repeat')
    by_id={c['id']:c for c in cases}
    mixed=draft.get('mixedEvidence')
    if mixed is not None:
        evidence=load(root,mixed['run']);case=by_id[mixed['caseId']]
        require(evidence.get('commit')==source and digest(evidence['runtimeFingerprint'],'mixed runtime')==digest(actuals[0]['runtimeFingerprint'],'runtime'),'Actual mixed diagnostic source/runtime differs')
        capture=next(c for c in evidence['captures'] if str(c.get('recipeFingerprint','')).lower()==case['recipeFingerprint'].lower())
        from baseline_admission import validate_mixed_consumption,validate_recipe
        validate_recipe(capture,case);validate_mixed_consumption(capture['performance']['cpuFrames'],case,'native');case['coverage']='mixed-material-skin-mechanics-effects';case['mixedEvidence']=mixed['run']
    owner=load(root,draft['gameplayOwner']);origin=owner['sessionOrigin']
    frozen=dict(id='actual-gameplay',mode='actual-live-gameplay',origin=origin['inputs'],originFingerprint=origin['fingerprint'],originInputBytesBase64=origin['inputBytesBase64'],originEvidence=draft['gameplayOwner'])
    from baseline_admission import validate_gameplay
    require(owner['sourceSha']==source and digest(owner['runtimeFingerprint'],'actual gameplay runtime')==digest(actuals[0]['runtimeFingerprint'],'runtime'),'Actual gameplay preflight source/runtime differs');validate_gameplay(owner,frozen)
    extras=[frozen,dict(id='collector-control',mode='observer-on-off',fixtureCaseId=draft['observerFixtureCaseId'])]
    native_manifest=load(root,draft['nativeIdentityEvidence'])
    require(native_manifest.get('commit')==source and str(native_manifest.get('buildIdentity','')).lower().endswith(source) and digest(native_manifest.get('runtimeFingerprint'),'native identity runtime')==digest(actuals[0]['runtimeFingerprint'],'runtime'),'Original native hardware identity source/runtime differs')
    from baseline_contracts import native_identity
    native_hardware=native_identity(native_manifest,sdk)['hardwareIdentitySha256']
    require(all(capture.get('graphics',{}).get('device')==actuals[0]['device'] for capture in native_manifest['captures']),'Original native hardware device differs from actual preflight')
    protocol=dict(schemaVersion=1,kind='baseline-control-body-proposal',executionSource=source,frozenAtUtc=None,proposedAtUtc=draft['proposedAtUtc'],comparison='identical-instrumented-source-A/A',preserveAllAttempts=True,pairs=draft['pairs'],regressionPercent=15,rasterDpi=96,warmups=64,warmSamples=512,coldSamples=128,cases=cases,supplementalCoverage=extras,attemptInventoryRoot=draft['attemptInventoryRoot'],launchOutputRoot=draft['launchOutputRoot'])
    protocol.update(payloadInventory=actuals[0]['payloadBefore'],nativeHardwareIdentitySha256=native_hardware)
    keys=ordered_keys(draft['pairs'],by_id,{r['id']:r for r in extras});protocol['attemptSchedule']=[]
    for ordinal,(pair,arm,case,producer) in enumerate(keys):
        leaf=f'{ordinal:05d}-{pair}-{arm}-{case}-{producer}';protocol['attemptSchedule'].append(dict(pair=pair,arm=arm,caseId=case,producer=producer,directory=(Path(draft['attemptInventoryRoot'])/leaf).as_posix(),launchOutputPath=str(windows_absolute(draft['launchOutputRoot'])/leaf)))
    value=dict(schemaVersion=1,kind=PROPOSAL,accepted=False,qualified=False,sourceInfo=dict(producerSourceSha=source,proposalToolSourceSha=tool,proposalToolSourceTreeClean=True,toolScript=tool_script,acceptedProtocolSourceSha=None,resultsReviewSourceSha=None),
        sourceSdkPin=sdk,sourceSdkBytesBase64=base64.b64encode(source_sdk).decode(),sourceSdkSha256=sha256_bytes(source_sdk),
        payloadInventory=actuals[0]['payloadBefore'],runtimeFingerprint=actuals[0]['runtimeFingerprint'],nativeIdentityEvidence=draft['nativeIdentityEvidence'],protocol=protocol,planningEstimate=draft['planningEstimate'],policyReferences=draft['policyReferences'])
    validate_bound_proposal(value,root,repository);return value


def validate_bound_proposal(value,root,repository):
    result=validate_complete_schema(value);source=value['sourceInfo']['producerSourceSha']
    sdk=git(repository,'show',source+':global.json');require(sha256_bytes(sdk)==value['sourceSdkSha256'] and sdk==base64.b64decode(value['sourceSdkBytesBase64'],validate=True),'SDK pin is not from actual original producer source S')
    tool=value['sourceInfo']['toolScript'];tool_sha=value['sourceInfo']['proposalToolSourceSha']
    require(git(repository,'rev-parse',tool_sha+':'+tool['path']).decode().strip()==tool['gitBlobSha'] and base64.b64decode(tool['bytesBase64'],validate=True).replace(b'\r\n',b'\n')==git(repository,'show',tool_sha+':'+tool['path']),'Original executing proposal script is not bound to its recorded source tree')
    cases={c['id']:c for c in value['protocol']['cases']}
    native=load(root,value['nativeIdentityEvidence']);require(native.get('commit')==source and str(native.get('buildIdentity','')).lower().endswith(source) and digest(native['runtimeFingerprint'],'actual native runtime')==digest(value['runtimeFingerprint'],'proposal runtime'),'Original native identity source/runtime differ')
    from baseline_contracts import native_identity
    require(native_identity(native,value['sourceSdkPin'])['hardwareIdentitySha256']==value['protocol']['nativeHardwareIdentitySha256'],'Actual original native hardware hash differs')
    for case in cases.values():
        actual,actual_case=preflight(root,case['preflight'],source)
        require(all(canonical(case[k])==canonical(v) for k,v in actual_case.items()),'Proposed case/profile/pose differs from retained original preflight bytes')
        require(canonical(actual['payloadBefore'])==canonical(value['payloadInventory']) and digest(actual['runtimeFingerprint'],'actual runtime')==digest(value['runtimeFingerprint'],'proposal runtime'),'Preflight executable/runtime identity differs from full proposal')
        require(all(c['graphics']['device']==actual['device'] for c in native['captures']),'Preflight actual device differs from original native hardware record')
        if case['coverage']=='mixed-material-skin-mechanics-effects':
            mixed=load(root,case['mixedEvidence']);require(mixed.get('commit')==source and digest(mixed.get('runtimeFingerprint'),'actual mixed runtime')==digest(value['runtimeFingerprint'],'proposal runtime'),'Actual mixed evidence belongs to another source/runtime')
            capture=next(c for c in mixed['captures'] if str(c.get('recipeFingerprint','')).lower()==case['recipeFingerprint'].lower())
            from baseline_admission import validate_mixed_consumption,validate_recipe
            validate_recipe(capture,case);validate_mixed_consumption(capture['performance']['cpuFrames'],case,'native')
    for extra in value['protocol']['supplementalCoverage']:
        if extra['mode']=='actual-live-gameplay':
            owner=load(root,extra['originEvidence']);require(owner.get('sourceSha')==source and digest(owner['runtimeFingerprint'],'actual gameplay runtime')==digest(value['runtimeFingerprint'],'proposal runtime'),'Gameplay origin source/runtime differ')
            from baseline_admission import validate_gameplay
            validate_gameplay(owner,extra)
    for reference in value['planningEstimate']['sourceReferences']:load(root,reference)
    for reference in value['policyReferences'].values():load(repository,reference)
    return dict(result,status='original-inputs-bound-proposal',originalInputsValidation='verified retained original inputs',policyApproval='not verified by proposal tooling; accepted master protocol P remains required')
