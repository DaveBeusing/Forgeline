"""Retain original native evidence and summarize matched integration cohorts."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import shutil
import statistics
import zipfile

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--repository-root',type=Path,required=True)
p.add_argument('--cohorts',type=Path,required=True)
p.add_argument('--output',type=Path,required=True)
args=p.parse_args()
repo=args.repository_root.resolve();source=args.cohorts.resolve();out=args.output.resolve()
out.mkdir(parents=True,exist_ok=False)
sha=lambda b:hashlib.sha256(b).hexdigest()
def load(path):return json.loads(path.read_bytes())
def distribution(values):
    a=sorted(float(x) for x in values if x is not None and math.isfinite(float(x)))
    return {'samples':len(a),'mean':statistics.fmean(a) if a else None,
            **{k:a[min(len(a)-1,math.ceil(len(a)*q)-1)] if a else None for k,q in [('p50',.5),('p95',.95),('p99',.99)]}}
plan=load(source/'execution-plan.json')
runs=[];errors=[];images={};files={};image_map=[]
runtime=set();drivers=set();case_count=0;cpu_count=0;gpu_count=0
for row in plan['plan']:
    name,variant,camera,matrix,fallback=row
    path=source/name/'manifest.json'
    if not path.exists():errors.append({'cohort':name,'error':'missing-manifest'});continue
    raw=path.read_bytes();m=json.loads(raw)
    files[f'{name}/manifest.json']=raw
    request_path=source/(name+'.request.json')
    request=load(request_path)
    original_request=request_path.read_bytes()
    request['outputRoot']='<output>/'+name
    request['runtimeRoot']='<runtime-root>'
    retained_request=(json.dumps(request,indent=2)+'\n').encode()
    files[name+'.request.json']=retained_request
    expected=plan['baselineCommit'] if variant=='baseline' else plan['currentCommit']
    if m.get('commit')!=expected or not m.get('buildIdentity','').endswith(expected):errors.append({'cohort':name,'error':'source-identity'})
    if m.get('status')!='structural-pass':errors.append({'cohort':name,'error':m.get('error') or m.get('status')})
    runtime.add(m.get('runtimeFingerprint'));drivers.add(m.get('driver'))
    cases=[]
    for c in m.get('captures',[]):
        case_count+=1
        perf=c['performance'];cpu=perf['cpuFrames'];gpu=perf['gpuFrames']
        cpu_count+=len(cpu);gpu_count+=len(gpu)
        if len(cpu)!=plan['samples'] or [x['index'] for x in cpu]!=list(range(len(cpu))):errors.append({'cohort':name,'key':c['key'],'error':'CPU-window'})
        fences=[g['fence'] for g in gpu]
        if not gpu or fences!=sorted(set(fences)) or any(g['frameMilliseconds']<=0 or g.get('timestampFrequencyHz',0)<=0 for g in gpu):errors.append({'cohort':name,'key':c['key'],'error':'GPU-window'})
        controls=perf.get('controls',{})
        if controls.get('staticBufferUploadFallback')!=(fallback=='Upload'):errors.append({'cohort':name,'key':c['key'],'error':'static-memory-control'})
        if not c['repeatPixelsEqual'] or c['instances']['fallbackMeshInstances'] or c['materials']['bindingFailureCount'] or c['graphics']['health']['submissionFaultCount']:errors.append({'cohort':name,'key':c['key'],'error':'native-contract'})
        if c['graphics']['device']['debugLayerEnabled'] or c['graphics']['device']['isSoftwareAdapter']:errors.append({'cohort':name,'key':c['key'],'error':'timing-validation-or-software-enabled'})
        if c['actualSurface']['width']!=1920 or c['actualSurface']['height']!=1080:errors.append({'cohort':name,'key':c['key'],'error':'native-resolution'})
        image=source/name/c['image'];png=image.read_bytes();digest=sha(png)
        if digest.upper()!=c['imageSha256'].upper():raise ValueError('Image integrity failure')
        images.setdefault(digest,png)
        image_map.append({'cohort':name,'key':c['key'],'sha256':digest,'object':'images/'+digest+'.png'})
        prep=[x['submission']['assetPreparation'] for x in cpu]
        def phase(field):return distribution(x.get(field) for x in prep)
        memory=[x.get('memory') or {} for x in cpu]
        local=[x['localUsageBytes'] for x in memory if x.get('available')]
        uploads=[x['submission']['objects']['uploadBytes'] for x in cpu]
        terrain=[x['submission']['terrain'] for x in cpu]
        cases.append({'key':c['key'],'cpu':c['cpu'],'gpu':perf['gpu'],'cpuSamples':len(cpu),'gpuSamples':len(gpu),
                      'prepare':phase('prepareMilliseconds'),'record':phase('recordMilliseconds'),
                      'submit':phase('submitMilliseconds'),'retire':phase('retireMilliseconds'),
                      'present':distribution(x.get('presentMilliseconds') for x in cpu),
                      'frameReuseWait':distribution(x.get('frameWaitMilliseconds') for x in cpu),
                      'managedAllocation':perf['allocations'],'instanceUploadBytes':{'min':min(uploads),'max':max(uploads)},
                      'instancesLast':c['instances'],'terrainLast':c['terrain'],
                      'terrainUploadsFirst':terrain[0]['chunkUploads'],'terrainUploadsLast':terrain[-1]['chunkUploads'],
                      'shadowLast':c['graphics'].get('directionalShadows'),
                      'dxgiLocalUsageBytes':{'availableSamples':len(local),'min':min(local) if local else None,'max':max(local) if local else None},
                      'setupMilliseconds':c['run']['setupMilliseconds'],'setupAllocatedBytes':c['run']['setupAllocatedBytes'],
                      'gpuPasses':perf['gpuPasses'],'cpuPasses':perf['cpuPasses'],
                      'healthLast':c['graphics']['health'],'resourcesLast':c['graphics']['resources'],
                      'gpuTiming':perf['gpuTiming'],'imageSha256':digest,'fixtureFingerprint':c['fingerprint']})
    expected_cases=7 if matrix=='scaling' else 4
    if len(cases)!=expected_cases:errors.append({'cohort':name,'error':'matrix-cardinality','expected':expected_cases,'actual':len(cases)})
    runs.append({'cohort':name,'variant':variant,'camera':camera,'matrix':matrix,'staticMemory':fallback,
                 'source':expected,'build':m['buildIdentity'],'manifestSha256':sha(raw),
                 'requestOriginalSha256':sha(original_request),'requestRetainedSha256':sha(retained_request),
                 'requestRedaction':'Only output/runtime paths replaced with portable macros; original local bytes remain hash-bound.',
                 'startupMilliseconds':m['startupMilliseconds'],'startupAllocatedBytes':m['startupAllocatedBytes'],'cases':cases})
if len(runtime)!=1:errors.append({'error':'runtime-payload-mismatch','values':sorted(runtime)})
if len(drivers)!=1:errors.append({'error':'driver-mismatch','values':sorted(drivers)})
comparisons=[]
for path in sorted(source.glob('*.performance.json')):
    data=load(path);files[path.name]=path.read_bytes()
    comparisons.append({'report':path.name,'regressions':data['regressions'],'unqualified':data['unqualified'],'cases':data['cases']})
image_comparisons=[]
for path in sorted(source.glob('*.images.json')):
    data=load(path);files[path.name]=path.read_bytes()
    differences=[{'key':x['key'],'comparison':x['comparison'],'pixels':x.get('pixelComparison')} for x in data['captures'] if x.get('pixelComparison') and x['pixelComparison']['ChangedPixels']]
    image_comparisons.append({'report':path.name,'differences':differences,'structural':data['structural'],'imageFailures':data['imageFailures']})
for path in source.glob('*.unqualified.json'):files[path.name]=path.read_bytes();errors.append(load(path))
for name in ['execution-plan.json','cohorts.jsonl','inputs-before.json','inputs-after.json']:
    files[name]=(source/name).read_bytes()
if (source/'inputs-before.json').read_bytes()!=(source/'inputs-after.json').read_bytes():errors.append({'error':'input-inventory-changed'})
for name in ['Run-Comparisons.original.ps1','Run-Comparisons.ps1','Complete-Comparisons.ps1','RequestTemplate.json','comparisons-v1.log','comparison-recovery.log']:
    files['protocol/'+name]=(source.parent/name).read_bytes()
if sha(files['protocol/Run-Comparisons.original.ps1'])!=plan['runnerSha256']:raise ValueError('Original capture-runner hash mismatch')
files['images/index.json']=(json.dumps(image_map,indent=2)+'\n').encode()
summary={'schemaVersion':1,'baseline':plan['baselineCommit'],'current':plan['currentCommit'],
         'cases':case_count,'cpuSamples':cpu_count,'gpuSamples':gpu_count,'uniqueImages':len(images),
         'runtimeFingerprints':sorted(runtime),'driver':sorted(drivers),'runs':runs,
         'performanceComparisons':comparisons,'imageComparisons':image_comparisons,'errors':errors,
         'postprocessing':{'initialRunnerExitCode':1,'reason':'Compare collided with the existing PowerShell Compare-Object alias after all 80 native captures and unchanged-input verification completed.','recovery':'Unique Invoke-MatchedComparison name; original and corrected runners, recovery script and full logs retained. Native measurements were not repeated or rewritten.'},
         'performanceAcceptance':'not-granted','humanVisualAcceptance':'pending'}
(out/'summary.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
files['summary.json']=(out/'summary.json').read_bytes()
index={'schemaVersion':1,'method':'Native manifests and timing/comparison records retain original bytes. Request copies redact only absolute output/runtime paths. Original PNG bytes are content-addressed without editing.',
       'records':[{'path':name,'bytes':len(raw),'sha256':sha(raw)} for name,raw in sorted(files.items())]}
files['index.json']=(json.dumps(index,indent=2)+'\n').encode()
with zipfile.ZipFile(out/'raw-evidence.zip','w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
    for name,raw in sorted(files.items()):z.writestr(name,raw)
with zipfile.ZipFile(out/'images.zip','w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
    z.writestr('index.json',files['images/index.json'])
    for digest,png in sorted(images.items()):z.writestr('images/'+digest+'.png',png)
with zipfile.ZipFile(out/'raw-evidence.zip') as z:
    for record in index['records']:
        if sha(z.read(record['path']))!=record['sha256']:raise ValueError('Retained evidence hash mismatch')
print(json.dumps({'cases':case_count,'cpuSamples':cpu_count,'gpuSamples':gpu_count,'uniqueImages':len(images),'errors':len(errors),'comparisonCount':len(comparisons),'rawBytes':(out/'raw-evidence.zip').stat().st_size,'imageBytes':(out/'images.zip').stat().st_size}))
