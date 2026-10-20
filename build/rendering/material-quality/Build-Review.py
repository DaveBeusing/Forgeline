"""Build the final human review from immutable native readbacks."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont


def main() -> None:
    parser=argparse.ArgumentParser()
    parser.add_argument('artifacts',type=Path)
    parser.add_argument('documentation',type=Path)
    args=parser.parse_args()
    root=args.artifacts/'native-isolated';docs=args.documentation
    objects=docs/'evidence/objects';objects.mkdir(parents=True,exist_ok=True)
    comparisons=docs/'evidence/comparisons';comparisons.mkdir(parents=True,exist_ok=True)
    manifests={name:json.loads((root/name/'manifest.json').read_text(encoding='utf-8-sig')) for name in ('before-1-fixed','after-1-fixed','after-2-fixed','before-2-fixed')}
    rows=[];lines=['# Material and shadow candidate review','',
        'Status: **human material and shadow-contact decisions pending**. Numerical/structural success does not approve material appearance or GPU cost.', '',
        'Baseline `bfc16a953d731a844321a0848a2ebadc71c5875f` (0.1.423); candidate production source `57fa863f540f283ffbed742923de3e9fa6b3f80c` (0.1.424).',
        'High, 1920x1080, fixed lighting and assets; close/normal/strategic cameras are 120/420/1000 metres. Original goldens are unchanged.', '',
        'Review material identity, normal seams, silhouette/readability, contact, acne and detached shadows separately. The existing specular variance filter is retained; the two rejected experiments are not the candidate.', '',
        'The material/shadow delta is intentionally local. Do not infer a broad artwork improvement or an accepted performance result from these images.', '',
        '| Reference | Baseline | Candidate | Side by side | Changed pixels | Maximum channel delta |','|---|---|---|---|---:|---:|']
    for before,after,again_before,again_after in zip(manifests['before-1-fixed']['captures'],manifests['after-1-fixed']['captures'],manifests['before-2-fixed']['captures'],manifests['after-2-fixed']['captures']):
        assert before['key']==after['key']==again_before['key']==again_after['key']
        assert before['imageSha256']==again_before['imageSha256'] and after['imageSha256']==again_after['imageSha256']
        links=[];arrays=[]
        for name,case in (('before-1-fixed',before),('after-1-fixed',after)):
            path=root/name/case['image'];raw=path.read_bytes();digest=hashlib.sha256(raw).hexdigest()
            assert digest.upper()==case['imageSha256'].upper()
            target=objects/(digest+'.png')
            if target.exists():assert target.read_bytes()==raw
            else:target.write_bytes(raw)
            links.append('evidence/objects/'+target.name);arrays.append(np.asarray(Image.open(path)).astype(np.int16))
        delta=np.abs(arrays[0]-arrays[1]);changed=int(np.count_nonzero(np.any(delta!=0,axis=2)))
        comparison=Image.new('RGBA',(3840,1128),(16,20,24,255))
        draw=ImageDraw.Draw(comparison)
        font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',24)
        draw.text((12,10),'Baseline bfc16a95 / '+after['key'],fill='white',font=font)
        draw.text((1932,10),'Candidate 57fa863f / '+after['key'],fill='white',font=font)
        for offset,array in zip((0,1920),arrays):
            comparison.paste(Image.fromarray(array.astype(np.uint8)),(offset,48))
        comparison_path=comparisons/(after['key']+'.png');comparison.save(comparison_path)
        comparison_link='evidence/comparisons/'+comparison_path.name
        row=dict(key=after['key'],baselineImageSha256=before['imageSha256'],candidateImageSha256=after['imageSha256'],
                 baselinePixelSha256=before['pixelSha256'],candidatePixelSha256=after['pixelSha256'],changedPixels=changed,
                 maximumChannelDelta=int(delta.max()),baseline=links[0],candidate=links[1],crossCohortPixelsExact=True,sideBySide=comparison_link,
                 sideBySideSha256=hashlib.sha256(comparison_path.read_bytes()).hexdigest())
        rows.append(row)
        lines.append(f"| {after['key']} | [Original]({links[0]}) | [Candidate]({links[1]}) | [Comparison]({comparison_link}) | {changed:,} | {int(delta.max())} |")
    lines+=['','Side-by-side figures preserve every original pixel and add only a 48-pixel label strip. Open at 100% for native-size inspection.', '',
        '![Tank close comparison]('+rows[0]['sideBySide']+')','',
        '![Industrial close comparison]('+rows[3]['sideBySide']+')']
    lines+=['','## Native GPU costs and unresolved gates','',
        'Both full-client pairs pass the unchanged 15% total-GPU checks. They still fail 13 CPU metric/statistic observations (2/4 and 2/5 for fixed/sweep pairs). These are not shader gains.',
        'The focused world/shadow probe retains 25 metric/statistic exceedances: 10 world GPU, 5 shadow GPU, 7 total GPU and 3 CPU. It does not pass performance acceptance. No unavailable metric is replaced by zero.', '',
        'The table gives measured mean changes for both independent focused pairs. Raw p50/p95/p99 and every failed observation remain in the evidence index. The focused probe omits dedicated atmosphere/particle passes; full-client references remain separate.', '',
        '| Profile / scene | World mean pair 1 / pair 2 | Shadow mean pair 1 / pair 2 |','|---|---:|---:|']
    analysis=json.loads((args.artifacts/'world-isolated/analysis.json').read_text())
    pairs={(row['pair'],tuple(row['key'])):row for row in analysis['cases']}
    for key in [tuple(row['key']) for row in analysis['cases'] if row['pair']==1]:
        a,b=pairs[1,key],pairs[2,key]
        values=[]
        for metric in ('worldGpuMilliseconds','shadowGpuMilliseconds'):
            values.append(' / '.join(f"{row['metrics'][metric]['changePercent']['mean']:+.2f}%" for row in (a,b)))
        name=['Low','Medium','High','Ultra'][key[0]]+' / '+('Tank' if key[1]==15 else 'Industrial base')
        lines.append(f'| {name} | {values[0]} | {values[1]} |')
    lines+=['','## Separate decisions','',
        '- Material appearance: pending.','- Shadow contact and receiver stability: pending.',
        '- Additional GPU costs / timing qualification: not accepted; strict gates remain open.',
        '- Complete solution and CI: see [qualification](Qualification.md), including baseline failures.','',
        'All 32 focused native measurements release asset textures and pending retirements; the same one device-owned registered resource remains after every renderer disposal. This is a registered-resource plateau, not a physical VRAM-cap claim.', '',
        'The [evidence index](evidence/index.json) maps original artifacts to content-addressed PNGs or compressed records, with original/retained/stored hashes. The rejected experiment pixel packs are lossless and independently verified by `Retain-Sequences.py`.']
    (docs/'Review.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    result=dict(schemaVersion=1,baselineSource=manifests['before-1-fixed']['commit'],candidateSource=manifests['after-1-fixed']['commit'],
                runtimeFingerprint=manifests['after-1-fixed']['runtimeFingerprint'],materialDecision='pending',shadowContactDecision='pending',
                performanceDecision='not-accepted',captures=rows)
    (docs/'evidence/review.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print('Final review built: six unchanged native source-qualified pairs; cross-cohort pixels exact.')
    print([(r['key'],r['changedPixels'],r['maximumChannelDelta']) for r in rows])


if __name__=='__main__':
    main()
