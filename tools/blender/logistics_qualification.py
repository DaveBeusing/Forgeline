"""Collect source-bound logistics evidence without modifying raw captures."""
import argparse
import gzip
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]


def read(path):
    return json.loads((ROOT / path).read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256((ROOT / path).read_bytes()).hexdigest()


def collect(destination):
    destination = ROOT / destination
    destination.mkdir(parents=True, exist_ok=True)
    bindings=read('docs/qualification/logistics-blender/SourceBindings.json')
    for family in bindings['families']:
        assert sha(family['master'])==family['masterSha256']
    for mesh in bindings['meshes']:
        assert sha(mesh['source'])==mesh['exportSha256']
    groups = [('',54),('-traffic',8),('-dark',6),('-wet',6),('-sweep',3),('-workloads',18),('-inspection',6)]
    pairs = []
    raw = []
    manifests = []
    for suffix, count in groups:
        cohorts = []
        for side in ('before','after'):
            folder = Path('artifacts/logistics') / (side + suffix)
            path = folder / 'manifest.json'
            manifest = read(path)
            assert manifest['status'] == 'structural-pass', path
            assert len(manifest['captures']) == count, path
            selected = []
            for capture in manifest['captures']:
                assert capture['repeatPixelsEqual'], capture['key']
                assert sha(folder / capture['image']).lower() == capture['imageSha256'].lower(), capture['key']
                assert capture['instances']['fallbackMeshInstances'] == 0
                assert capture['materials']['bindingFailureCount'] == 0
                assert capture['graphics']['resources']['textureBindingFailureCount'] == 0
                assert capture['graphics']['health']['submissionFaultCount'] == 0
                assert not capture['graphics']['device']['isSoftwareAdapter']
                selected.append({key:capture[key] for key in ('key','fingerprint','camera','requestedDisplay','actualSurface','profile','lighting','cpu','gpu','performance','run','instances','materials','graphics','pixelSha256','imageSha256','repeatPixelsEqual')})
            header = {key:manifest[key] for key in ('commit','buildIdentity','runtimeFingerprint','driver','environment')}
            raw.append({'cohort':side+suffix, **header, 'captures':selected})
            manifests.append({'path':path.as_posix(),'sha256':sha(path),'cases':count,'sourceCommit':manifest['commit']})
            cohorts.append({c['key']:c for c in selected})
        assert cohorts[0].keys() == cohorts[1].keys()
        for key in cohorts[0]:
            before, after = (c[key] for c in cohorts)
            for field in ('fingerprint','camera','requestedDisplay','actualSurface','profile','lighting'):
                assert before[field] == after[field], (key,field)
            equal = before['pixelSha256'] == after['pixelSha256']
            assert equal, 'Pixel difference requires explicit investigation: '+key
            pairs.append({'cohort':suffix.lstrip('-') or 'gallery','key':key,'rgbaIdentical':equal,
                'pngByteIdentical':before['imageSha256']==after['imageSha256'],
                'beforeCpuMilliseconds':before['cpu'],'afterCpuMilliseconds':after['cpu'],
                'beforeGpuMilliseconds':before['gpu'],'afterGpuMilliseconds':after['gpu'],
                'beforeResources':before['graphics']['resources'],'afterResources':after['graphics']['resources'],
                'beforeInstances':before['instances'],'afterInstances':after['instances']})
    assert len(pairs) == 101
    for group in ('installed','installed-inspection'):
        path=Path('artifacts/logistics')/group/'manifest.json'
        if (ROOT/path).exists():
            manifest=read(path)
            assert manifest['status']=='structural-pass'
            raw.append({'cohort':group, **{key:manifest[key] for key in ('commit','buildIdentity','runtimeFingerprint','driver','environment')},
                'captures':[{key:capture[key] for key in ('key','fingerprint','camera','requestedDisplay','actualSurface','profile','lighting','cpu','gpu','performance','run','instances','materials','graphics','pixelSha256','imageSha256','repeatPixelsEqual')} for capture in manifest['captures']]})
    raw_text = json.dumps(raw, sort_keys=True, separators=(',',':'))
    assert not re.search(r'[A-Za-z]:[\\/]',raw_text), 'Omit local absolute paths from retained samples'
    compressed = gzip.compress(raw_text.encode(), mtime=0)
    (destination / 'NativeSamples.json.gz').write_bytes(compressed)
    guards=[]
    for key in ('s','p','h'):
        native=read(f'artifacts/lg/n3/{key}/contracts.json')
        sources=read(f'artifacts/lg/ns2/{key}/contracts.json')
        assert native['success'] and sources['success']
        assert len(native['rejections'])==12 and len(sources['cases'])==2
        guards.append({'family':native['family'],'native':native,'sources':sources})
    canonical=read('artifacts/lg/canonical-qualification.json')
    original=read('artifacts/lg/base-qualification.json')
    assert canonical['textureRuntimeBytes']==original['textureRuntimeBytes']
    assert canonical['materialRuntimeBytes']==original['materialRuntimeBytes']
    texture_bytes=canonical['textureRuntimeBytes']-canonical['studioSplashTextureRuntimeBytes']
    assert texture_bytes <= 56*1024*1024
    log=(ROOT/'artifacts/lg/full-tests.log').read_text(encoding='utf-8-sig')
    totals=re.search(r'gesamt:\s*(\d+)\s+fehlgeschlagen:\s*(\d+)\s+erfolgreich:\s*(\d+)\s+\u00fcbersprungen:\s*(\d+)',log)
    assert totals and tuple(map(int,totals.groups()))==(3240,0,3214,26)
    test_counts=dict(zip(('total','failed','passed','skipped'),map(int,totals.groups())))
    report={'schemaVersion':1,'baselineMaster':'b32bf04671fb579e01a7e282eac76ff91c5b2f1d',
        'branch':'art/logistics-blender','version':'0.1.429-dev',
        'testedSourceCommit':'eda79f7a005d8970147cf289dab255f17622065b',
        'fullRelease':{'warnings':0,'errors':0,'logSha256':sha('artifacts/lg/build-adopted.log')},
        'fullSolution':{**test_counts,'logSha256':sha('artifacts/lg/full-tests.log')},
        'portableBlender':{'passed':103,'failed':0,'logSha256':sha('artifacts/lg/portable-final.log')},
        'nativeBlender':guards,'masterAudit':read('artifacts/lg/master-audit.json'),
        'canonicalRuntime':canonical,'baselineRuntime':original,
        'compiledGameplayTextureBytes':texture_bytes,'compiledGameplayTextureCapBytes':56*1024*1024,
        'runtimeNumericComparison':read('artifacts/lg/runtime-numeric-comparison.json'),
        'matchComparison':read('artifacts/lg/match-comparison.json'),
        'nativeManifests':manifests,'nativePairs':pairs,
        'nativeSamplesSha256':hashlib.sha256(compressed).hexdigest(),
        'rawNativeCaptureCount':sum(len(cohort['captures']) for cohort in raw),
        'performanceAcceptance':{'accepted':False,'controlledQuietWindow':False,'thermalStabilityVerified':False,
            'scope':'Short native capture windows with concurrent external work; raw timings, current-thread allocations and DXGI usage are observations, not full-client or sustained performance acceptance.'},
        'humanVisualAccepted':False,'productionPromotionAccepted':False}
    if (ROOT/'artifacts/lg/installed-verification.json').exists():
        report['installedPackage']=read('artifacts/lg/installed-verification.json')
    text=json.dumps(report,indent=2,sort_keys=True)+'\n'
    assert not re.search(r'[A-Za-z]:[\\/]',text), 'Omit local absolute paths from retained index'
    (destination/'Qualification.json').write_text(text,encoding='utf-8')
    print('Qualified 101 exact image pairs; 36 Blender rejections and six source rejections; performance and human gates remain open.')


if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--output-root',type=Path,required=True)
    collect(parser.parse_args().output_root)
