"""Editable road, bridge and transition masters using the static compiler subset."""
import argparse
import copy
import hashlib
import json
import os
import sys
from pathlib import Path
import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
from asset_contract import Catalog, fixture_selection, read_geometry, require, sha, write_glb, write_json
from infrastructure_contract import canonical_geometry, family_catalog, export_filename
from production import surface_preview
from roundtrip import import_scene, export_scene, preview_state
from tank import restore_normals


def source_identity(catalog, ids):
    # JSON checkout line endings are not art edits. Every other byte remains bound.
    return {p.relative_to(catalog.root).as_posix():hashlib.sha256(
        p.read_bytes().replace(b'\r\n',b'\n') if p.suffix.lower() in ('.json','.gltf') else p.read_bytes()).hexdigest()
        for p in catalog.files(ids)}


def surface_identity(catalog, ids):
    result={}
    for aid in ids:
        if catalog.definitions[aid][1]['type'] not in ('material','texture'):continue
        path=catalog.source(aid);data=path.read_bytes()
        if path.suffix.lower()=='.json':data=data.replace(b'\r\n',b'\n')
        result[aid]=hashlib.sha256(data).hexdigest()
    return result


def graph_signature(material):
    nodes = []
    for node in material.node_tree.nodes:
        inputs = {}
        for socket in node.inputs:
            if hasattr(socket, 'default_value'):
                value = socket.default_value
                inputs[socket.identifier] = value if isinstance(value, (int, float, bool, str)) else list(value)
        row = dict(name=node.name, type=node.type, inputs=inputs)
        for field in ('operation', 'blend_type', 'interpolation', 'extension'):
            if hasattr(node, field): row[field] = getattr(node, field)
        if node.type == 'TEX_IMAGE':
            require(node.image and node.image.packed_file and not node.image.is_dirty, 'Packed infrastructure texture changed')
            row.update(imageHash=hashlib.sha256(node.image.packed_file.data).hexdigest(), colorSpace=node.image.colorspace_settings.name)
        nodes.append(row)
    links = sorted((l.from_node.name,l.from_socket.identifier,l.to_node.name,l.to_socket.identifier) for l in material.node_tree.links)
    return hashlib.sha256(json.dumps(dict(nodes=nodes,links=links),sort_keys=True).encode()).hexdigest()


def pack_preview(catalog, ids):
    costs = surface_preview(catalog, ids)
    bpy.context.scene['fl_infrastructure_preview'] = json.dumps({aid: graph_signature(bpy.data.materials[aid])
        for aid in ids if catalog.definitions[aid][1]['type'] == 'material'}, sort_keys=True)
    bpy.context.scene['fl_infrastructure_surfaces'] = json.dumps(surface_identity(catalog,ids), sort_keys=True)
    bpy.context.scene['fl_infrastructure_identity'] = json.dumps(source_identity(catalog,ids), sort_keys=True)
    for image in bpy.data.images:
        image.filepath = '//' + os.path.relpath(image.filepath, catalog.root / 'infrastructure/directorate/authoring').replace('\\','/')
        for packed in image.packed_files: packed.filepath = image.filepath
    return costs


def unpack_preview(catalog, ids):
    expected = json.loads(bpy.context.scene['fl_infrastructure_surfaces'])
    require(expected == surface_identity(catalog,ids), 'Infrastructure surface sources changed; re-author explicitly')
    require(json.loads(bpy.context.scene['fl_infrastructure_identity']) == source_identity(catalog,ids),
        'Infrastructure source identity changed; re-author explicitly')
    contract=json.loads(bpy.context.scene['fl_contract'])
    contract['sourceSignature']=catalog.signature(ids)[0]
    bpy.context.scene['fl_contract']=json.dumps(contract,sort_keys=True)
    signatures = json.loads(bpy.context.scene['fl_infrastructure_preview'])
    require(set(signatures) == {aid for aid in ids if catalog.definitions[aid][1]['type']=='material'}, 'Preview material set changed')
    for aid, signature in signatures.items():
        material = bpy.data.materials.get(aid)
        require(material and graph_signature(material) == signature, 'Infrastructure PBR preview changed')
        material.node_tree.nodes.clear()
        bsdf=material.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
        out=material.node_tree.nodes.new('ShaderNodeOutputMaterial')
        material.node_tree.links.new(bsdf.outputs['BSDF'],out.inputs['Surface'])
        source=json.loads(catalog.source(aid).read_text(encoding='utf-8-sig'))
        material.diffuse_color=source.get('baseColorFactor',[1,1,1,1])
        for name,key in (('Base Color','baseColorFactor'),('Metallic','metallicFactor'),('Roughness','roughnessFactor')):
            bsdf.inputs[name].default_value=source.get(key,[1,1,1,1] if name=='Base Color' else 1)
        material['fl_preview']=preview_state(material)


def run(request):
    output=Path(request['outputRoot']);report=dict(schemaVersion=1,success=False,errors=[])
    try:
        parser=argparse.ArgumentParser()
        parser.add_argument('--action',choices=('Author','Export'),required=True)
        parser.add_argument('--family',choices=('roads','bridge'),required=True)
        parser.add_argument('--fixtures',type=Path,required=True)
        parser.add_argument('--blend-file',type=Path)
        args=parser.parse_args(request['arguments'])
        print('Infrastructure:',args.action,args.family,flush=True)
        fixture=json.loads(args.fixtures.read_text(encoding='utf-8-sig'))
        family=next(f for f in fixture['families'] if f['name']==args.family)
        catalog=family_catalog(request['inputRoot'],fixture);ids,families=fixture_selection(catalog,args.fixtures,[family['root']])
        print('Validated family closure:',len(ids),flush=True)
        signature,hashes=catalog.signature(ids)
        geometries={aid:read_geometry(catalog.source(aid),catalog.root,catalog.definitions[aid][1]) for aid in ids if catalog.definitions[aid][1]['type']=='mesh'}
        if args.action=='Author':
            require(not args.blend_file,'Author does not accept an existing master')
            import_scene(catalog,ids,families,geometries)
        else:
            require(args.blend_file and args.blend_file.is_file(),'Export requires the saved family master')
            bpy.ops.wm.open_mainfile(filepath=str(args.blend_file.resolve()))
            unpack_preview(catalog,ids)
        restore_normals()
        def compare(aid,before,after):
            _,maximum=canonical_geometry(before,after,'.collision' in aid)
            return dict(maximumPositionDelta=maximum)
        results,meshes,deviations=export_scene(catalog,ids,families,geometries,compare)
        print('Validated editable mesh channels:',len(meshes),flush=True)
        if args.action=='Export':
            source=output/'source';catalog.copy_to(ids,source)
            for aid,geometry in results.items():
                path,definition=catalog.definitions[aid]
                target=Path('_infrastructure')/export_filename(aid)
                canonical,maximum=canonical_geometry(geometries[aid],geometry,'.collision' in aid)
                write_glb(source/path.relative_to(catalog.root).parent/target,canonical)
                updated=copy.deepcopy(definition);updated['source']=target.as_posix()
                write_json(source/path.relative_to(catalog.root),updated)
        textures=[]
        if args.action=='Author':
            print('Packing existing PBR preview textures',flush=True)
            textures=pack_preview(catalog,ids)
            for aid in geometries:
                layer=bpy.data.collections.new(aid);bpy.context.scene.collection.children.link(layer)
                for obj in list(bpy.context.scene.objects):
                    if obj.get('fl_asset')!=aid: continue
                    for previous in list(obj.users_collection): previous.objects.unlink(obj)
                    layer.objects.link(obj);obj.hide_set(aid!=family['root']);obj.hide_render=aid!=family['root']
            bpy.ops.wm.save_as_mainfile(filepath=str(output/(args.family+'.blend')),check_existing=False,compress=True)
            print('Saved editable family master',flush=True)
        require(catalog.signature(ids)[0]==signature,'Infrastructure source changed during export')
        report.update(success=True,family=args.family,sourceSignature=signature,sourceHashes=hashes,
            meshes=meshes,deviations=deviations,textures=textures,assetIds=ids,
            axes=dict(engine='+Y up / +Z forward',blender='+Z up / -Y forward',metres=1))
    except Exception as error:
        report['errors'].append(str(error));raise
    finally: write_json(output/'infrastructure-report.json',report)


if __name__=='__main__':run(json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig')))
