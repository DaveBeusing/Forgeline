"""Retain source-bound terrain evidence; never alter native images or samples."""
import argparse
import gzip
import hashlib
import json
from pathlib import Path
import re

ROOT=Path(__file__).resolve().parents[2]


def read(path):
    return json.loads((ROOT/path).read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256((ROOT/path).read_bytes()).hexdigest()


def counts(name):
    path=Path('artifacts/tp')/name
    text=(ROOT/path).read_text(encoding='utf-8-sig',errors='replace')
    m=re.search(r'gesamt:\s*(\d+).*?fehlgeschlagen:\s*(\d+).*?erfolgreich:\s*(\d+).*?bersprungen:\s*(\d+)',text,re.S)
    assert m,name
    result=dict(zip(('total','failed','passed','skipped'),map(int,m.groups())))
    assert result['failed']==0,name
    return {**result,'logSha256':sha(path)}


def collect(destination):
    destination=ROOT/destination
    destination.mkdir(parents=True,exist_ok=True)
    bindings=read('docs/qualification/terrain-pbr/SourceBindings.json')
    for master in bindings['masters']:assert sha(master['path'])==master['sha256']
    for item in bindings['maps']:
        assert sha(item['source'])==item['sha256']
        original=Path('assets/source/world/textures/terrain')/(item['family']+'_production_'+item['kind']+'.png')
        assert sha(original)==item['originalSha256'] and item['decodedPixelsIdentical']
    native=read('artifacts/tp/native3/qualification.json')
    assert native['success'] and len(native['families'])==7
    assert all(len(x['rejections'])==13 and x['boundedEndpointsAccepted'] for x in native['families'])
    assert native['savedTintEdit']['basePixelsChanged'] and native['savedTintEdit']['normalAndOrmPixelsUnchanged']
    independent=read('artifacts/tp/independent-exports.json')
    assert len(independent)==7
    for family in independent:
        for relative,expected in family['files'].items():
            for run in ('a','b'):
                assert sha(Path('artifacts/tp')/('export-'+run)/family['family']/'source'/relative)==expected
    raw=[];pairs=[];manifest_rows=[]
    def cohort(name,expected):
        folder=Path('artifacts/terrain-pbr')/name
        manifest=read(folder/'manifest.json')
        assert manifest['status']=='structural-pass' and len(manifest['captures'])==expected
        selected=[]
        for c in manifest['captures']:
            assert c['repeatPixelsEqual'] and not c['repeatFailures']
            assert c['terrain']['textureBindingsPerDraw']==13 and c['terrain']['maximumTextureSamplesPerPixel']==13
            assert not c['graphics']['device']['isSoftwareAdapter']
            assert c['instances']['fallbackMeshInstances']==c['materials']['bindingFailureCount']==0
            r=c['graphics']['resources']
            assert r['textureBindingFailureCount']==c['graphics']['health']['submissionFaultCount']==0
            assert r['peakResidentTextureBytes']<=64*1024*1024
            assert r['peakShaderResourceDescriptorsUsed']<=r['shaderResourceDescriptorCapacity']
            assert sha(folder/c['image']).lower()==c['imageSha256'].lower()
            selected.append({k:c[k] for k in ('key','kind','fingerprint','camera','requestedDisplay','actualSurface',
                'profile','lighting','cpu','gpu','performance','run','terrain','instances','materials','graphics',
                'pixelSha256','imageSha256','repeatPixelsEqual')})
        raw.append({'cohort':name,**{k:manifest[k] for k in ('commit','buildIdentity','runtimeFingerprint','driver','environment',
            'runtime','method','capturePath','clientAssemblySha256','presentationAssemblySha256')},'captures':selected})
        manifest_rows.append({'path':(folder/'manifest.json').as_posix(),'sha256':sha(folder/'manifest.json'),
                              'sourceCommit':manifest['commit'],'cases':expected})
        return {c['key']:c for c in selected}
    for suffix,expected in [('',54),('-dark',6),('-wet',6),('-traverse',3)]:
        before=cohort('before'+suffix,expected);after=cohort('after'+suffix,expected)
        assert before.keys()==after.keys()
        for key in before:
            a,b=before[key],after[key]
            assert a['camera']==b['camera'] and a['requestedDisplay']==b['requestedDisplay']
            pixels_equal=a['pixelSha256']==b['pixelSha256']
            png_equal=a['imageSha256']==b['imageSha256']
            if not pixels_equal or not png_equal:
                assert suffix=='-dark' and key=='EnvironmentKitBridge-3840x2160-High-NormalGameplay'
            pairs.append({'cohort':suffix or 'main','key':key,'rgbaIdentical':pixels_equal,'pngIdentical':png_equal,
                          'pixelSha256':a['pixelSha256'],'imageSha256':a['imageSha256'],
                          'beforeResources':a['graphics']['resources'],'afterResources':b['graphics']['resources'],
                          'beforeCpu':a['cpu'],'afterCpu':b['cpu'],'beforeGpu':a['gpu'],'afterGpu':b['gpu']})
    assert sum(p['rgbaIdentical'] and p['pngIdentical'] for p in pairs)==68
    retry_before=cohort('before-dark-check',1);retry_after=cohort('after-dark-check',1)
    assert retry_before.keys()==retry_after.keys()
    for key,a in retry_before.items():
        b=retry_after[key]
        assert a['pixelSha256']==b['pixelSha256'] and a['imageSha256']==b['imageSha256']
    for suffix,expected in [('',54),('-dark',6),('-wet',6),('-traverse',3)]:
        cohort('profile'+suffix,expected)
    cohort('profile-pilot',3)
    profile_comparison=read('artifacts/tp/profile-pixel-comparison.json')
    assert len(profile_comparison['cases'])==69 and profile_comparison['changedCases']>0
    installed=[]
    for name,reference in [('installed','profile'),('installed-wet','profile-wet')]:
        cases=cohort(name,3)
        original={c['key']:c for c in read(Path('artifacts/terrain-pbr')/reference/'manifest.json')['captures']}
        for key,c in cases.items():
            assert c['pixelSha256']==original[key]['pixelSha256'] and c['imageSha256']==original[key]['imageSha256']
            installed.append({'cohort':name,'key':key,'rgbaIdenticalToDevelopment':True,'pngIdenticalToDevelopment':True})
    rawtext=json.dumps(raw,sort_keys=True,separators=(',',':'))
    assert not re.search(r'[A-Za-z]:[\\/]',rawtext)
    compressed=gzip.compress(rawtext.encode(),mtime=0)
    (destination/'NativeSamples.json.gz').write_bytes(compressed)
    canonical=read('artifacts/tp/canonical-qualification.json')
    assert canonical['success'] and canonical['assetCount']==1363
    gameplay=canonical['textureRuntimeBytes']-canonical['studioSplashTextureRuntimeBytes']
    assert gameplay<=56*1024*1024
    scoped=read('artifacts/tp/scoped/compiled.json')
    assert scoped['success'] and (scoped['assetCount'],scoped['textureCount'],scoped['materialCount'])==(29,21,8)
    tool=read('artifacts/tp/toolchain/report.json')['toolchain']
    result={'schemaVersion':1,'baselineMaster':bindings['baselineMaster'],'version':'0.1.437-dev',
        'testedAssetAdoptionCommit':'89fd4a79ba5abc798adbaf20e90891580b378b44',
        'testedProductionCommit':'68d3765982936c8d6ae22860509fa616ecc62c8a',
        'blender':{'executableSha256':tool['executableSha256'],**tool['capabilities']},
        'sourceBindingsSha256':sha('docs/qualification/terrain-pbr/SourceBindings.json'),
        'releaseBuild':{'warnings':0,'errors':0,'logSha256':sha('artifacts/tp/build-adopted.log')},
        'profileReleaseBuild':{'warnings':0,'errors':0,'logSha256':sha('artifacts/tp/build-profile.log')},
        'portable':{'passed':122,'failed':0,'logSha256':sha('artifacts/tp/portable-all.log')},
        'nativeBlender':native,'independentExports':independent,
        'localTests':{name:counts(file) for name,file in [('assets','assets-tests.log'),('terrain','terrain-tests.log'),('world','world-tests.log'),('map','map-tests.log')]},
        'profileTerrainTests':counts('profile-terrain-tests.log'),
        'baselineRuntime':read('artifacts/tp/base-qualification.json'),'canonicalRuntime':canonical,
        'canonicalSourceLess':read('artifacts/tp/canonical-source-less.json'),
        'scopedRuntime':scoped,'scopedSourceLess':read('artifacts/tp/scoped/source-less.json'),
        'payloadComparison':read('artifacts/tp/runtime-comparison.json'),
        'naturalMatch':read('artifacts/tp/match-comparison.json'),
        'profileNaturalMatch':read('artifacts/tp/profile-match-comparison.json'),
        'compiledGameplayTextureBytes':gameplay,'compiledGameplayTextureCapBytes':56*1024*1024,
        'nativeManifests':manifest_rows,'migrationNativePairs':pairs,'installedCases':installed,
        'originalMigrationDifference':read('artifacts/tp/dark-4k-difference.json'),
        'isolatedPairedRetryExact':True,'originalFailureRetained':True,
        'intentionalProfileComparisons':profile_comparison,
        'installedPackage':read('artifacts/tp/installed-verification.json'),
        'rawNativeCases':sum(len(r['captures']) for r in raw),'nativeSamplesSha256':hashlib.sha256(compressed).hexdigest(),
        'performanceAccepted':False,'controlledThermalWindowVerified':False,'completePhysicalMemoryAccepted':False,
        'humanArtAccepted':False,'productionPromotionAccepted':False,
        'limits':'Source-bound functional/static evidence. Shared workstation, short capture windows; no sustained FPS, complete memory, live LOD or thermal acceptance.'}
    text=json.dumps(result,indent=2,sort_keys=True)+'\n'
    assert not re.search(r'[A-Za-z]:[\\/]',text)
    (destination/'Qualification.json').write_text(text,encoding='utf-8')
    print('Retained 68 exact migration pairs, one original 1-pixel difference and exact paired retry; 69 intentional profile comparisons, six installed cases and source/compiler evidence. Acceptance gates remain open.')


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--output-root',type=Path,required=True)
    collect(p.parse_args().output_root)
