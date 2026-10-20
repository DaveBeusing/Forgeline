"""Export portable native frame observations without machine-specific capture paths."""
import argparse
import hashlib
import json
from pathlib import Path


def collect(root):
    manifest_path = root / 'manifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    assert manifest['status'] == 'structural-pass'
    report = {k: manifest[k] for k in (
        'schemaVersion', 'status', 'commit', 'buildIdentity', 'runtime', 'environment',
        'runtimeFingerprint', 'driver', 'software', 'fixtureVersion', 'hostWindowDpi',
        'clientAssemblySha256', 'presentationAssemblySha256', 'method', 'warmups', 'samples')}
    report['originalManifestSha256'] = hashlib.sha256(manifest_path.read_bytes()).hexdigest()
    report['projection'] = 'Original per-frame CPU/resource/memory/health and completed GPU observations, excluding redundant draw diagnostics and machine-specific request paths. Full manifest retained in local qualification artifacts. No human visual acceptance implied.'
    report['captures'] = []
    for capture in manifest['captures']:
        assert hashlib.sha256((root / capture['image']).read_bytes()).hexdigest().upper() == capture['imageSha256']
        assert capture['repeatPixelsEqual']
        item = {k: capture[k] for k in (
            'key', 'imageSha256', 'pixelSha256', 'seed', 'tick', 'captureVisualSeconds',
            'camera', 'requestedDisplay', 'actualSurface', 'rasterDpi', 'uiScale',
            'profile', 'cpu', 'gpu', 'repeatPixelsEqual', 'repeatCaptures', 'visualReview', 'submission')}
        performance = capture['performance']
        item['cpuFrames'] = [{k: frame[k] for k in (
            'index', 'milliseconds', 'allocatedBytes', 'worldMilliseconds', 'overlayMilliseconds',
            'compositeMilliseconds', 'shadowMilliseconds', 'resources', 'health', 'memory')}
            for frame in performance['cpuFrames']]
        item['gpuFrames'] = performance['gpuFrames']
        item['gpuTiming'] = performance['gpuTiming']
        report['captures'].append(item)
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('root', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    result = collect(args.root)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
