"""Author bounded functional fittings over retained production mesh sources.

Run after the existing bevel/running-gear and mechanical partition passes.
Original production sources, collision meshes and sockets remain unchanged.
Reduced building LODs retain roof material separation without extra fittings.
All fittings use the existing atlas and single material slot.
"""
import base64
import json
from pathlib import Path
from importlib.machinery import SourceFileLoader
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
props = SourceFileLoader('industrial_details', str(ROOT/'build/Author-IndustrialProps.py')).load_module()


def fittings(name):
    mesh = props.Mesh()
    if name == 'main_battle_tank':
        # Forward access hatches and steel hinges clear the articulated turret.
        for x in (-.22, .22):
            mesh.box([x-.065,.021,.22], [x+.065,.045,.31], 15)
            mesh.box([x-.05,.045,.23], [x+.05,.052,.241], 1)
    elif name == 'scout_vehicle':
        mesh.box([-.21,.065,.271], [.21,.165,.276], 4)
        mesh.box([-.105,.458,.101], [.105,.502,.106], 4)
        mesh.box([-.012,.06,.272], [.012,.17,.279], 1)
    elif name in ('cargo_truck', 'supply_truck'):
        for x in (-.12,.12):
            mesh.box([x-.105,.065,.431], [x+.105,.19,.436], 4)
        mesh.box([-.18,-.035,.431], [.18,.025,.437], 9)
        if name == 'cargo_truck':
            for z in (-.34,-.20,-.06):
                for sign in (-1,1):
                    x = sign*.314
                    mesh.box([x-.003,-.08,z-.009], [x+.003,.245,z+.009], 2)
        else:
            for sign in (-1,1):
                x = sign*.305
                mesh.box([x-.003,.005,-.30], [x+.003,.19,-.10], 9)
    elif name == 'command_core':
        mesh.box([-.125,.21,.151], [.125,.32,.156], 4)
        mesh.box([-.012,.205,.153], [.012,.325,.159], 1)
        mesh.box([-.15,.395,-.12], [.15,.41,.08], 9)
    elif name == 'vehicle_factory':
        # Reinforced loading portal and roof ventilation, within existing bounds.
        for x in (-.285,.285):
            mesh.box([x-.012,-.28,.482], [x+.012,.06,.49], 1)
        mesh.box([-.28,.052,.477], [.28,.071,.488], 5)
        for x in (-.16,.16):
            mesh.box([x-.07,.303,-.17], [x+.07,.319,.07], 9)
    elif name == 'power_plant':
        # Service louvers and roof-mounted generator heat exchangers.
        mesh.box([-.21,-.04,.341], [.12,.135,.347], 9)
        for x in (-.25,-.08,.09):
            mesh.box([x-.045,.203,-.05], [x+.045,.245,.05], 7)
    else:
        raise ValueError(name)
    return mesh


def author():
    targets = {f'unit.directorate.{n}': n for n in
        ('main_battle_tank','scout_vehicle','cargo_truck','supply_truck')}
    targets.update({f'building.directorate.{n}': n for n in
        ('command_core','vehicle_factory','power_plant')})
    for asset_id,name in list(targets.items()):
        if asset_id.startswith('building.'):
            for lod in (1,2):
                targets[f'{asset_id}.lod{lod}'] = name
    for meta in (ROOT/'assets/source').rglob('*.asset.json'):
        definition = json.loads(meta.read_text())
        if definition['id'] not in targets:
            continue
        # This family has dedicated component/LOD sources, not retained box fittings.
        if definition['id'].startswith('building.directorate.command_core'):
            continue
        source = (meta.parent/definition['source']).resolve()
        reduced = '.lod' in definition['id']
        mechanical = definition.get('mechanicalParts', [])
        if source.stem.endswith('_detailed'):
            suffix = '.gltf' if reduced else '_production_motion.gltf' if mechanical else '_production.gltf'
            source = source.with_name(source.stem.removesuffix('_detailed')+suffix)
        if not reduced and not source.stem.endswith(('_production', '_production_motion')):
            raise ValueError(f'Expected retained production source: {source}')
        gltf = json.loads(source.read_text())
        primitive = gltf['meshes'][0]['primitives'][0]
        raw = base64.b64decode(gltf['buffers'][0]['uri'].split(',')[1])
        original = [props.reader.read(gltf,raw,primitive['attributes'][key]).copy()
                    for key in ('POSITION','NORMAL','TEXCOORD_0')]
        indices = props.reader.read(gltf,raw,primitive['indices']).ravel().copy()
        if definition['id'].startswith('building.'):
            # Broad upward faces are painted roofs, not wall-to-wall vent grilles.
            uv = original[2]
            roof = (original[1][:,1] > .5) & (np.floor(uv[:,0]*4) == 1) & (np.floor(uv[:,1]*4) == 2)
            uv[roof] -= [.25,.5]
        if reduced:
            arrays = original + [indices.astype('<u4')]
        else:
            detail = fittings(targets[definition['id']])
            arrays = [np.concatenate((old,np.asarray(new)),axis=0).astype('<f4')
                      for old,new in zip(original,(detail.p,detail.n,detail.uv))]
            arrays.append(np.concatenate((indices,np.asarray(detail.idx)+len(original[0]))).astype('<u4'))
            if mechanical:
                # Retain all existing moving ranges. Appended details form explicit
                # ranges, with sensor glass sharing the sensor's authored motion.
                mechanical = [p for p in mechanical if not p['name'].startswith('fidelity_')]
                offset = len(indices)
                is_scout = targets[definition['id']] == 'scout_vehicle'
                ranges = [36, 36, 36] if is_scout else [len(detail.idx)]
                for i, count in enumerate(ranges):
                    part = dict(name=f'fidelity_{i}', firstIndex=offset+i*36,
                                indexCount=count, parent=-1, motion='static')
                    if is_scout and i == 1:
                        sensor = next(p for p in mechanical if p['name'] == 'sensor')
                        part.update({k: sensor[k] for k in ('motion','socket','rate','travel')})
                    mechanical.append(part)
                definition['mechanicalParts'] = mechanical
        if not (np.array_equal(arrays[0].min(0),original[0].min(0)) and
                np.array_equal(arrays[0].max(0),original[0].max(0))):
            raise ValueError(f'Fittings exceed original bounds: {source}')
        blob = b''; views = []; accessors = []
        for array,kind,component in zip(arrays,('VEC3','VEC3','VEC2','SCALAR'),(5126,5126,5126,5125)):
            payload = array.tobytes()
            views.append(dict(buffer=0,byteOffset=len(blob),byteLength=len(payload)))
            accessors.append(dict(bufferView=len(views)-1,componentType=component,count=len(array),type=kind))
            blob += payload
        gltf['buffers'] = [dict(byteLength=len(blob),uri='data:application/octet-stream;base64,'+base64.b64encode(blob).decode())]
        gltf['bufferViews'] = views; gltf['accessors'] = accessors
        gltf['asset']['generator'] = 'ForgeLine functional fittings authoring'
        stem = source.stem.removesuffix('_motion').removesuffix('_production')
        output = source.with_name(stem+'_detailed.gltf')
        output.write_text(json.dumps(gltf,indent=2)+'\n')
        definition['source'] = str(Path(definition['source']).with_name(output.name)).replace('\\','/')
        meta.write_text(json.dumps(definition,indent=2)+'\n')
        print(definition['id'],len(indices)//3,'->',len(arrays[3])//3,'triangles')


if __name__ == '__main__':
    author()
