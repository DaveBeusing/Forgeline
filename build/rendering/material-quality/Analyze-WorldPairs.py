"""Compare fixed source-qualified world/shadow GPU cohorts without waiving gates."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import statistics


def stats(values: list[float]) -> dict | None:
    if len(values)<128:
        return None
    ordered=sorted(values)
    return dict(count=len(ordered),mean=statistics.mean(ordered),
                p50=ordered[min(len(ordered)-1,int(len(ordered)*.5))],
                p95=ordered[min(len(ordered)-1,int(len(ordered)*.95))],
                p99=ordered[min(len(ordered)-1,int(len(ordered)*.99))])


def main() -> None:
    parser=argparse.ArgumentParser()
    parser.add_argument('root',type=Path)
    args=parser.parse_args()
    reports={name:json.loads((args.root/(name+'.json')).read_text(encoding='utf-8-sig')) for name in ('before-1','after-1','after-2','before-2')}
    failures=[];unqualified=[];comparisons=[]
    for pair in (1,2):
        before,after=reports[f'before-{pair}'],reports[f'after-{pair}']
        assert before['catalogSignature']==after['catalogSignature'] and before['manifestSha256']==after['manifestSha256']
        assert before['Device']==after['Device'], 'Hardware changed'
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
    result=dict(schemaVersion=1,maximumRegressionPercent=15,minimumSamples=128,
                performancePassed=not failures and not unqualified,regressions=failures,unqualified=unqualified,cases=comparisons,
                interpretation='Two predefined pairs, raw completed-fence world/shadow series retained. Percent changes are observations. No CPU or total frame timing is relabeled as shader gain. Disabled shadows are inapplicable, unavailable timings stay null. Human image/extra-cost acceptance remains separate.')
    (args.root/'analysis.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(f'World/shadow pairs: {len(comparisons)} cases, {len(failures)} metric/statistic exceedances, {len(unqualified)} unqualified metrics.')


if __name__=='__main__':
    main()
