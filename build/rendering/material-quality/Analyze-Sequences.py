"""Summarize fixed material sequence cohorts without accepting new goldens."""
from __future__ import annotations
import argparse
import gzip
import numpy as np
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
            def raw(member: str) -> np.ndarray:
                arrays = [np.frombuffer(gzip.decompress((args.root / f'frequency-{frequency}-{member}' / f'{i:02}.rgba.gz').read_bytes()), dtype=np.uint8).reshape(576,768,4)[...,:3] for i in range(32)]
                return np.stack(arrays).astype(np.float32)
            baseline_frames = raw(f'before-{pair}')
            candidate_frames = raw(f'after-{pair}')
            def signal(frames: np.ndarray, index: int) -> dict:
                x,y=index%4*192+32,index//4*192+32
                roi=frames[:,y:y+128,x:x+128,:]
                delta=np.abs(np.diff(roi,axis=0))
                acceleration=np.abs(np.diff(roi,n=2,axis=0))
                return dict(meanChannel=float(roi.mean()), motionMean=float(delta.mean()),
                            motionP99=float(np.percentile(delta,99)), motionMaximum=float(delta.max()),
                            motionAbove16Fraction=float(np.mean(delta>16)),
                            secondDifferenceMean=float(acceleration.mean()),
                            secondDifferenceAbove16Fraction=float(np.mean(acceleration>16)))
            for index, name in enumerate(data['panels']):
                old = before['motionMeanAbsoluteChannelDelta'][index]
                new = after['motionMeanAbsoluteChannelDelta'][index]
                changes.append(dict(panel=name, baselineMotion=old, candidateMotion=new,
                                    changePercent=(new / old - 1) * 100 if old > 0 else None,
                                    baselineSignal=signal(baseline_frames,index),candidateSignal=signal(candidate_frames,index)))
            del baseline_frames, candidate_frames
            def timing(row: dict) -> dict:
                values = sorted(row['worldGpuMilliseconds'])
                return dict(count=len(values), mean=statistics.mean(values) if values else None,
                            p95=values[int(.95 * (len(values)-1))] if values else None)
            results.append(dict(frequency=frequency, pair=pair, panels=changes,
                                baselineWorldGpu=timing(before), candidateWorldGpu=timing(after)))
    report = dict(method='Unregistered mean absolute RGB change, tails and second differences in fixed panel interiors. Mean/brightness and every tail remain visible, including worse results. Diagnostics, not perceptual quality scores. Tail analysis was added after the first squared-slope experiment exposed higher bare-steel mean motion, before analysis of the conservative variant. No automatic approval.',
                  repeatedSequencePixelsExact=True, results=results)
    (args.root / 'analysis.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    for row in results:
        changes = {panel['panel']: round(panel['changePercent'], 2) if panel['changePercent'] is not None else None for panel in row['panels']}
        print(f"frequency={row['frequency']} pair={row['pair']} motion_change_percent={changes}")


if __name__ == '__main__':
    main()
