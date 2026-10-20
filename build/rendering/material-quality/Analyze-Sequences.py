"""Summarize fixed material sequence cohorts without accepting new goldens."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import statistics


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('root', type=Path)
    args = parser.parse_args()
    data = json.loads((args.root / 'report.json').read_text(encoding='utf-8-sig'))
    rows = data['results']
    results = []
    for frequency in sorted({row['frequency'] for row in rows}):
        group = {row['member']: row for row in rows if row['frequency'] == frequency}
        for prefix in ('before', 'after'):
            assert group[f'{prefix}-1']['captures'] == group[f'{prefix}-2']['captures'], 'Repeated sequence pixels changed'
        for pair in (1, 2):
            before, after = group[f'before-{pair}'], group[f'after-{pair}']
            changes = []
            for index, name in enumerate(data['panels']):
                old = before['motionMeanAbsoluteChannelDelta'][index]
                new = after['motionMeanAbsoluteChannelDelta'][index]
                changes.append(dict(panel=name, baselineMotion=old, candidateMotion=new,
                                    changePercent=(new / old - 1) * 100 if old > 0 else None))
            def timing(row: dict) -> dict:
                values = sorted(row['worldGpuMilliseconds'])
                return dict(count=len(values), mean=statistics.mean(values) if values else None,
                            p95=values[int(.95 * (len(values)-1))] if values else None)
            results.append(dict(frequency=frequency, pair=pair, panels=changes,
                                baselineWorldGpu=timing(before), candidateWorldGpu=timing(after)))
    report = dict(method='Unregistered mean absolute RGB change in fixed interior panel regions. A diagnostic, not a perceptual quality score; interpret together with images and baseline brightness. No automatic approval.',
                  repeatedSequencePixelsExact=True, results=results)
    (args.root / 'analysis.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    for row in results:
        changes = {panel['panel']: round(panel['changePercent'], 2) if panel['changePercent'] is not None else None for panel in row['panels']}
        print(f"frequency={row['frequency']} pair={row['pair']} motion_change_percent={changes}")


if __name__ == '__main__':
    main()
