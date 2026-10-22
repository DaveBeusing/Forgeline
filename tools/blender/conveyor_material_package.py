"""Adopt generated conveyor art and stage its complete compiler input closure.

No tests, previews or acceptance checks are run. Requires completed native
conveyor_author output; never changes the read-only planning design contract.
"""
import argparse
import hashlib
import json
import shutil
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from asset_contract import Catalog
from conveyor_models import ROOT, MATERIAL, STATUS, write_json

PACKAGE = ROOT/'assets/source/infrastructure/directorate/conveyors'


def adopt(author_root, stage):
    author_root = author_root.resolve()
    if author_root.is_relative_to(PACKAGE):
        raise ValueError('Native generation must be outside adopted source assets')
    records = json.loads((author_root/'conveyor-production.v1.json').read_text())
    if len(records['modules']) != 15:
        raise ValueError('Adoption requires the complete 15-module generation')
    for record in records['modules']:
        master = author_root/(record['slug']+'.blend')
        if not master.is_file():
            raise ValueError('Generation has no native master: '+str(master))
    for record in records['modules']:
        shutil.copyfile(author_root/(record['slug']+'.blend'), PACKAGE/'authoring'/(record['slug']+'.blend'))
    shutil.copytree(author_root/'source', PACKAGE/'models', dirs_exist_ok=True)
    shutil.copyfile(author_root/'conveyor-production.v1.json', PACKAGE/'conveyor-production.v1.json')
    moving = ['flowing','idle_no_input','disconnected','congested','blocked','unpowered','damaged','critical','destroyed']
    cues = {
        'flowing':'two printed bars, dim green pilot; existing cleat motion at LOD0/1',
        'idle_no_input':'printed hollow circle, dark pilot, static empty belt',
        'disconnected':'printed separated link, open mechanical jaws, static belt',
        'congested':'printed amber X with queued generic packs; static belt',
        'blocked':'printed amber X and physical striped stop gate; static belt',
        'unpowered':'printed crossed power circle, dark pilot, engaged brake; static belt',
        'damaged':'exposed substrate/crack and bent guard; static until explicitly integrated',
        'critical':'concentrated soot/heat/crack, deformed bearer; static',
        'destroyed':'interrupted/collapsed geometry, exposed wreck substrate, no light or motion',
        'structural_intact':'painted Directorate bars/service mark; no operating light or motion'}
    write_json(PACKAGE/'conveyor-materials.v1.json', {
        'schema':'forgeline.conveyor_materials','schemaVersion':1,
        'materialAssetId':MATERIAL,'recipe':'materials/recipe.json',
        'runtimeTextures':{k:'texture.infrastructure.directorate.conveyor.'+k for k in ('base','normal','orm','emissive')},
        'authoringWearMask':'materials/conveyor_wear.png',
        'maskChannels':{'R':'dirt','G':'exposed substrate','B':'heat / soot'},
        'runtimeMaskSampling':False,'uvInset':.018,'uvGrid':[4,4],
        'statusBinding':'presentation selection contract; no conveyor simulation enum or live binding exists yet',
        'selectionPriority':['destroyed','critical','damaged','unpowered','disconnected','blocked','congested','flowing','idle_no_input'],
        'selectionPolicy':'health/destruction takes precedence; unavailable power/connectivity precedes flow; flow requires authoritative visible operating observation; structural modules ignore operating observations',
        'stateMapping':{s:{'variantSuffix':'.'+s,'statusCell':STATUS.get(s),
                           'cue':cues[s],'motion':s=='flowing'} for s in moving+['structural_intact']},
        'modules':[{'assetId':m['assetId'],'materialAssetId':MATERIAL,
                    'defaultState':'structural_intact' if m['role'] in ('structure','closure') else 'idle_no_input',
                    'states':sorted(set(v['state'] for v in m['variants']))} for m in records['modules']],
        'visibility':'consume copied visible observations only; never infer hidden inventory/ownership from textures',
        'acceptance':{'automatedTestsExecuted':False,'runtimeSmokesExecuted':False,'manualReview':'pending'}})
    catalog = Catalog(ROOT/'assets/source')
    ids = sorted(aid for aid in catalog.definitions if aid.startswith(('infrastructure.directorate.conveyor.','texture.infrastructure.directorate.conveyor.')) or aid==MATERIAL)
    # Explicit complete module set includes animations; the existing static-only
    # Catalog.closure deliberately rejects them and is not weakened here.
    catalog.copy_to(ids, stage)
    signature, files = catalog.signature(ids)
    write_json(stage.parent/'source-receipt.json', {
        'assetCount':len(ids),'sourceSetSHA256':signature,'sourceFiles':files,
        'masters':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted((PACKAGE/'authoring').glob('*.blend'))},
        'testsExecuted':False,'manualAcceptance':'pending'})


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--author-root',type=Path,required=True)
    parser.add_argument('--stage',type=Path,required=True)
    args=parser.parse_args()
    adopt(args.author_root,args.stage)
