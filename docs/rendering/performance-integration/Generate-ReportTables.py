"""Generate auditable markdown tables from retained native summaries."""
import json,sys
from pathlib import Path
s=json.loads(Path(sys.argv[1]).read_bytes());lines=[]
def table(headers,rows):
    lines.append('| '+' | '.join(headers)+' |');lines.append('| '+' | '.join(['---']*len(headers))+' |')
    lines.extend('| '+' | '.join(str(x) for x in row)+' |' for row in rows);lines.append('')
def n(v):return 'n/a' if v is None else f'{v:.3f}'
def short(key):return key.replace('-1920x1080-Medium','')
def get(run,key):return next(c for c in next(r for r in s['runs'] if r['cohort']==run)['cases'] if c['key']==key)
lines+=['## Completed matrix and valid samples','',f"{s['cases']} cases retain {s['cpuSamples']:,} CPU frames and {s['gpuSamples']:,} distinct completed GPU observations. {s['uniqueImages']} unique original PNGs cover the content-addressed image archive. Qualification errors: {len(s['errors'])}. CPU/GPU/frame/pass sample counts remain explicit in summary.json and the original manifests.",'']
rows=[]
for r in s['runs']:
    rows.append([r['cohort'],sum(c['cpuSamples'] for c in r['cases']),sum(c['gpuSamples'] for c in r['cases']),n(r['startupMilliseconds']),n(r['startupAllocatedBytes']/1048576)])
table(['Cohort','CPU samples','GPU samples','Process startup ms','Startup managed allocation MiB'],rows)
lines+=['## Matched whole-frame p95','', 'All times below are milliseconds. Each cell is CPU / GPU p95, kept separate. Complete mean/p50/p95/p99 distributions, valid counts and per-frame chronology remain in the retained JSON. The A/B values are independent runs; they are not pooled into a confidence interval.','']
rows=[]
for suffix in ['production','fixed','sweep']:
    for c in next(r for r in s['runs'] if r['cohort']=='baseline-a-'+suffix)['cases']:
        vals=[]
        for name in ['baseline-a-','candidate-a-','candidate-b-','baseline-b-']:
            x=get(name+suffix,c['key']);vals.append(n(x['cpu']['p95'])+' / '+n(x['gpu']['p95']))
        rows.append([suffix+'/'+short(c['key']),*vals])
table(['Case / camera','Baseline A','Current A','Current B','Baseline B'],rows)
lines+=['## CPU phases and blocking','', 'Current A p95 in milliseconds. Present/reuse and Prepare/Record/Submit/Retire overlap whole-frame CPU time and must not be added. These are observed phase boundaries of this collector, not a simulation or worker-utilization profile.','']
rows=[]
for r in s['runs']:
    if not r['cohort'].startswith('candidate-a-'):continue
    for c in r['cases']:
        rows.append([r['camera']+'/'+short(c['key']),*[n(c[k]['p95']) for k in ['prepare','record','submit','retire','present','frameReuseWait']]])
table(['Case / camera','Prepare','Record','Submit','Retire','Present','Reuse wait'],rows)
lines+=['## GPU pass observations','', 'Current A production p95 in milliseconds. Null means unavailable/no eligible pass samples. Passes overlap; atmosphere/composite can describe the same work.','']
rows=[]
for c in next(r for r in s['runs'] if r['cohort']=='candidate-a-production')['cases']:
    rows.append([short(c['key']),*[n(c['gpuPasses'][k]['p95']) for k in ['world','overlay','composite','shadows','particles','atmosphere']]])
table(['Case','World','Overlay','Composite','Shadow','Particle','Atmosphere'],rows)
lines+=['## Upload and memory observations','', 'Current A fixed/production views. Visible roots, actual reduced-LOD roots and last observed shadow casters are distinct populations. Instance bytes are per-frame traffic; skin upload fields retained in the raw records are capacity observations. DXGI values are local process usage, not total VRAM. All per-frame budgets, loaded textures, live/retired resources, setup allocations and terrain upload counters remain in the original manifests.','']
rows=[]
for r in s['runs']:
    if r['cohort'] not in ['candidate-a-fixed','candidate-a-production']:continue
    for c in r['cases']:
        i=c['instancesLast'];mem=c['dxgiLocalUsageBytes'];sh=c['shadowLast'] or {}
        rows.append([short(c['key']),i['visibleInstances'],i['reducedLodInstances'],sh.get('submittedCasters','n/a'),c['instanceUploadBytes']['max'],str(c['terrainUploadsFirst'])+' / '+str(c['terrainUploadsLast']),n(mem['min']/1048576) if mem['min'] is not None else 'n/a',n(mem['max']/1048576) if mem['max'] is not None else 'n/a',n(c['managedAllocation']['p95']),n(c['setupMilliseconds'])])
table(['Case','Visible roots','Reduced LOD','Shadow casters','Instance upload B/frame max','Terrain uploads first/last','DXGI min MiB','DXGI max MiB','Managed B/frame p95','Renderer setup ms'],rows)
lines+=['## Static-memory production control','', 'Same current source, payload, four production views and frame counts: Default / Upload / Upload / Default. This control affects the existing static-memory paths together. It does not isolate the 12-byte particle handoff, prove a memory-system gain, or qualify synthetic Upload scaling. Each value is CPU / GPU p95 milliseconds.','']
rows=[]
for c in next(r for r in s['runs'] if r['cohort']=='candidate-a-production')['cases']:
    row=[short(c['key'])]
    for name in ['candidate-a-production','memory-upload-a','memory-upload-b','candidate-b-production']:
        v=get(name,c['key']);row.append(n(v['cpu']['p95'])+' / '+n(v['gpu']['p95']))
    rows.append(row)
table(['Case','Default A','Upload A','Upload B','Default B'],rows)
lines+=['## Regressions, repeats and image differences','', 'The existing advisory threshold remains 15%. The following counts are individual CPU/GPU mean/p50/p95/p99 metrics above that threshold, not failed frames or independent statistical trials. Within-source repeat changes disclose noise/order dependence. No portable performance acceptance or isolated optimization gain is granted from these observations.','']
table(['Comparison','Metrics >15%','Unqualified cases'],[[r['report'],len(r['regressions']),len(r['unqualified'])] for r in s['performanceComparisons']])
rows=[]
for r in s['imageComparisons']:rows.append([r['report'],len(r['differences']),len(r['imageFailures'])])
table(['Strict image comparison','Cases with changed pixels','Image-gate failures'],rows)
lines+=['Image gates were not requested: structural checks, source-labelled comparisons and exact-pixel observations are retained; human visual acceptance remains pending. The images are original native PNGs with a case/cohort-to-SHA mapping, not resized or edited derivatives.','']
Path(sys.argv[2]).write_text('\n'.join(lines),encoding='utf-8')
