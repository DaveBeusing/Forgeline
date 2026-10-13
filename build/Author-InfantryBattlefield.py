"""Deterministic editable infantry and battlefield component exporter.

Repository geometry recipes own all parts, LOD membership and rigid pivots.
Requires NumPy. Runtime payloads remain exclusively owned by the asset compiler.
"""
import argparse
import hashlib
import json
from importlib.machinery import SourceFileLoader
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'assets/source'
primitive = SourceFileLoader('infantry_environment_primitives', str(ROOT/'build/Author-EnvironmentKit.py')).load_module()
validate = SourceFileLoader('infantry_source_validation', str(ROOT/'build/Author-OperationalBuildings.py')).load_module().validate


def emit(mesh, part, lod):
    if lod not in part['lods']:
        return
    if part['kind'] == 'box':
        mesh.box(part['min'], part['max'], part['cell'], part.get('bevel', 0) if lod == 0 else 0)
    elif part['kind'] == 'cylinder':
        mesh.cylinder(part['start'], part['end'], part['radius'], part['cell'], part['sides'][lod], part.get('inner', 0))
    elif part['kind'] == 'blob':
        mesh.blob(part['centre'], part['radius'], part['cell'], part['sides'][lod], part['rings'][lod])
    else:
        raise ValueError('Unsupported component kind: '+part['kind'])


def assemble(modules, lod):
    mesh = primitive.Mesh(); hierarchy = []; ranges = []
    for module in modules:
        first = len(mesh.indices)
        for part in module['parts']:
            start = len(mesh.indices); emit(mesh, part, lod)
            if len(mesh.indices) > start:
                hierarchy.append(dict(module=module['name'], name=part['name'], firstIndex=start, indexCount=len(mesh.indices)-start))
        if len(mesh.indices) > first:
            motion = module.get('motion', 'static') if lod == 0 else 'static'
            entry = dict(name=module['name'],firstIndex=first,indexCount=len(mesh.indices)-first,parent=-1,motion=motion)
            if motion != 'static':entry.update(socket=module['socket'],rate=module['rate'],travel=module['travel'])
            ranges.append(entry)
    validate(mesh)
    return mesh, hierarchy, ranges


def export(modules, lod, path, axes):
    mesh, hierarchy, groups = assemble(modules, lod)
    report = mesh.export(path)
    gltf = json.loads(path.read_text()); gltf['asset']['generator'] = 'ForgeLine infantry and battlefield component exporter'
    gltf['extras'] = dict(componentHierarchy=hierarchy,axes=axes,lod=lod)
    path.write_text(json.dumps(gltf,indent=2)+'\n',encoding='utf-8')
    report.update(sha256=hashlib.sha256(path.read_bytes()).hexdigest(),groups=groups)
    return report


def write_metadata(path, definition, output):
    path.parent.mkdir(parents=True, exist_ok=True)
    import os
    definition['source'] = Path(os.path.relpath(output,path.parent)).as_posix()
    path.write_text(json.dumps(definition,indent=2)+'\n',encoding='utf-8')


def author(output, update):
    unit_source = SOURCE/'units/directorate'
    kit = json.loads((unit_source/'authoring/infantry.kit.json').read_text())
    battlefield = json.loads((SOURCE/'world/authoring/battlefield.kit.json').read_text())
    report = {}
    unit_output = output/'units' if output else unit_source/'sources/infantry'
    world_output = output/'world' if output else SOURCE/'world/meshes/sources/battlefield'
    infrastructure_output = output/'infrastructure' if output else SOURCE/'infrastructure/directorate/sources/battlefield'
    dedicated_units = set()
    for definition_path in unit_source.rglob('*.asset.json'):
        definition = json.loads(definition_path.read_text(encoding='utf-8-sig'))
        if '/combat_support/' in definition.get('source', '').replace('\\', '/'):
            dedicated_units.add(definition['id'])
    rifle_primary = json.loads((unit_source/'infantry/rifle_squad.asset.json').read_text(encoding='utf-8-sig'))
    for family, recipe in kit['families'].items():
        for lod in range(3):
            path = unit_output/f'{family}_lod{lod}.gltf'
            entry = export(recipe['modules'],lod,path,kit['axes']); report[f'{family}.lod{lod}'] = entry
            if update:
                metadata = unit_source/'infantry'/(family+(f'_lod{lod}' if lod else '')+'.asset.json')
                definition = json.loads(metadata.read_text(encoding='utf-8-sig')) if metadata.exists() else dict(id='unit.directorate.'+family+(f'.lod{lod}' if lod else ''),type='mesh')
                definition['materialReferences']=['material.directorate.unit.rifle_squad']
                if lod == 0:
                    definition.update(lods=[dict(level=i,assetId='unit.directorate.'+family+f'.lod{i}',maxDistance=80 if i==1 else 220) for i in (1,2)],collisionRequired=True,collisionReference=rifle_primary['collisionReference'],sockets=recipe['sockets'],mechanicalParts=entry['groups'])
                else:
                    definition.pop('mechanicalParts',None)
                write_metadata(metadata,definition,path)
        for module in recipe['modules']:
            path = unit_output/'kit'/family/(module['name']+'.gltf')
            report[family+'.kit.'+module['name']] = export([module],0,path,kit['axes'])
    for recipe in battlefield['families']:
        name = recipe['name']; world = recipe['id'].startswith('mesh.world.')
        for lod in range(2 if world else 1):
            path = (world_output if world else infrastructure_output if recipe['id'].startswith('infrastructure.') else unit_output)/(name+(f'_lod{lod}' if world else '')+'.gltf')
            entry = export([dict(name=name,parts=recipe['parts'])],lod if world else 1,path,battlefield['axes']);report[name+(f'.lod{lod}' if world else '')]=entry
            if update:
                if recipe['id'].removesuffix('.wreck') in dedicated_units: continue
                metadata = SOURCE/recipe['metadata']
                if lod:metadata = metadata.with_name(metadata.stem.replace('.asset','_lod1.asset')+'.json')
                definition = json.loads(metadata.read_text(encoding='utf-8-sig')) if metadata.exists() else dict(id=recipe['id']+('_lod1' if lod else ''),type='mesh')
                definition['materialReferences'] = [recipe['material']]
                if world and not lod:definition['lods']=[dict(level=1,assetId=recipe['id']+'_lod1',maxDistance=recipe.get('reducedDistance',220))]
                if world and not lod and 'collisionReference' in recipe:
                    definition.update(collisionRequired=True,collisionReference=recipe['collisionReference'])
                write_metadata(metadata,definition,path)
    for family in kit['families']:
        counts=[report[family+f'.lod{i}']['triangles'] for i in range(3)]
        if not counts[0]>counts[1]>counts[2]:raise ValueError('Infantry LODs must strictly decrease: '+str(counts))
    report_path=(output if output else unit_output)/'exports.json'; report_path.parent.mkdir(parents=True,exist_ok=True)
    report_path.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({key:value['triangles'] for key,value in report.items()},indent=2))


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=None)
    parser.add_argument('--update-metadata',action='store_true')
    args=parser.parse_args();author(args.output,args.update_metadata)
