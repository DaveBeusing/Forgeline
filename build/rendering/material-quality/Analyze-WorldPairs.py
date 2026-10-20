"""Compare fixed source-qualified world/shadow GPU cohorts without waiving gates."""
from __future__ import annotations
import argparse
import json
import math
from pathlib import Path
import statistics


def stats(values: list[float]) -> dict | None:
    if len(values)<128 or any(not math.isfinite(value) or value<0 for value in values):
        return None
    ordered=sorted(values)
    result=dict(count=len(ordered),mean=statistics.mean(ordered),
                p50=ordered[math.ceil(len(ordered)*.5)-1],
                p95=ordered[math.ceil(len(ordered)*.95)-1],
                p99=ordered[math.ceil(len(ordered)*.99)-1])
    # An enabled timed pass with an unavailable/zero distribution cannot prove
    # a percentage budget. Disabled shadow profiles are handled separately.
    return result if all(result[key]>0 for key in ('mean','p50','p95','p99')) else None


def main() -> None:
    parser=argparse.ArgumentParser()
    parser.add_argument('root',type=Path)
    args=parser.parse_args()
    reports={name:json.loads((args.root/(name+'.json')).read_text(encoding='utf-8-sig')) for name in ('before-1','after-1','after-2','before-2')}
    failures=[];unqualified=[];comparisons=[]
    for pair in (1,2):
        before,after=reports[f'before-{pair}'],reports[f'after-{pair}']
        assert before['catalogSignature']==after['catalogSignature'] and before['manifestSha256']==after['manifestSha256']
        assert before['Device']==after['Device'] and before['driver']==after['driver'], 'Hardware/driver changed'
        for name in ('schemaVersion','windowEventsPumped','powerRequirement'):
            assert before.get(name)==after.get(name), 'Component measurement method changed between arms'
        for name in ('Width','Height','BufferCount','PresentMode'):
            assert before['Surface'][name]==after['Surface'][name], 'Actual surface changed'
        for report in (before,after):
            assert not any(report['Surface'][name] for name in ('IsSuspended','IsOccluded','ResizePending')), 'Unsettled surface'
        old={(r['quality'],r['scene'],r['zoom']):r for r in before['rows']}
        new={(r['quality'],r['scene'],r['zoom']):r for r in after['rows']}
        assert old.keys()==new.keys() and len(old)==8
        for key in old:
            a,b=old[key],new[key]
            assert a['Fingerprint']==b['Fingerprint'] and a['profile']==b['profile']
            row=dict(pair=pair,key=key,metrics={},baselineSource=before['source'],candidateSource=after['source'])
            for metric in ('cpuMilliseconds','worldGpuMilliseconds','shadowGpuMilliseconds','totalGpuMilliseconds'):
                if metric=='shadowGpuMilliseconds' and not a['profile']['Shadows']['Enabled']:
                    row['metrics'][metric]=dict(status='not-applicable-disabled-profile',baseline=None,candidate=None)
                    continue
                baseline,candidate=stats(a[metric]),stats(b[metric])
                row['metrics'][metric]=dict(status='qualified' if baseline and candidate else 'unavailable',baseline=baseline,candidate=candidate)
                if not baseline or not candidate:
                    unqualified.append(dict(pair=pair,key=key,metric=metric))
                    continue
                delta={stat:(candidate[stat]/baseline[stat]-1)*100 if baseline[stat]>0 else None for stat in ('mean','p50','p95','p99')}
                row['metrics'][metric]['changePercent']=delta
                for stat,percent in delta.items():
                    if percent is not None and percent>15:
                        failures.append(dict(pair=pair,key=key,metric=metric,statistic=stat,changePercent=percent))
            comparisons.append(row)
    result=dict(schemaVersion=2,statisticDefinition="nearest-rank-ceil-np-minus-one; lower median; invalid/zero distributions unavailable",maximumRegressionPercent=15,minimumSamples=128,
                performancePassed=not failures and not unqualified,regressions=failures,unqualified=unqualified,cases=comparisons,
                interpretation='Two predefined pairs, raw completed-fence world/shadow series retained. Percent changes are observations. No CPU or total frame timing is relabeled as shader gain. Disabled shadows are inapplicable, unavailable timings stay null. Human image/extra-cost acceptance remains separate.')
    (args.root/'analysis.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(f'World/shadow pairs: {len(comparisons)} cases, {len(failures)} metric/statistic exceedances, {len(unqualified)} unqualified metrics.')


if __name__=='__main__':
    main()
