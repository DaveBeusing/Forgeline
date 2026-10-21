"""Saved editable logistics modules over the existing strict compiler subset."""
import argparse
import copy
import hashlib
import json
import os
import sys
from pathlib import Path
import bpy
sys.path.insert(0, str(Path(__file__).resolve().parent))
from asset_contract import Catalog, fixture_selection, geometry_stats, read_geometry, require, sha, write_glb, write_json
from logistics_contract import FAMILIES, bindings, compare, preserve_uvs
from command_core_contract import material_source_hashes
from mechanical import layers
from production import surface_preview
from roundtrip import import_scene, export_scene, preview_state
from tank import restore_normals

def graph_signature(material):
    def scalar(value):
        if isinstance(value, (int, bool, str)):
            return value
        if isinstance(value, float):
            return round(value, 6)
        return [scalar(v) for v in value]
    nodes = []
    for node in material.node_tree.nodes:
        row = {'name': node.name, 'type': node.type,
               'inputs': {socket.identifier: scalar(socket.default_value) for socket in node.inputs if hasattr(socket, 'default_value')}}
        for field in ('operation', 'blend_type', 'interpolation', 'extension'):
            if hasattr(node, field):
                row[field] = getattr(node, field)
        if node.type == 'TEX_IMAGE':
            require(node.image and node.image.packed_file and not node.image.is_dirty, 'Packed preview texture changed')
            row['image'] = node.image.get('lg_source_hash')
            row['packedSha256'] = hashlib.sha256(node.image.packed_file.data).hexdigest()
            row['colorSpace'] = node.image.colorspace_settings.name
        nodes.append(row)
    links = sorted((link.from_node.name, link.from_socket.identifier, link.to_node.name, link.to_socket.identifier)
                   for link in material.node_tree.links)
    return hashlib.sha256(json.dumps({'nodes': sorted(nodes, key=lambda n: n['name']), 'links': links}, sort_keys=True).encode()).hexdigest()


def preview(catalog, ids):
    costs = surface_preview(catalog, ids)
    require(sum(row['estimatedPayloadBytes'] for row in costs) <= 7325040,
            'Existing logistics shared surface texture budget exceeded')
    for aid in ids:
        if catalog.definitions[aid][1]['type'] == 'material':
            bpy.data.materials[aid]['lg_material_source_hash'] = sha(catalog.source(aid))
    for image in bpy.data.images:
        matching = next((aid for aid in ids if catalog.definitions[aid][1]['type'] == 'texture'
                         and Path(image.filepath).name == catalog.source(aid).name), None)
        if matching:
            image['lg_source_hash'] = sha(catalog.source(matching))
            image.filepath = '//' + os.path.relpath(catalog.source(matching), catalog.root / 'buildings/directorate/authoring').replace('\\', '/')
            for packed in image.packed_files:
                packed.filepath = image.filepath
    bpy.context.scene['lg_preview'] = json.dumps({aid: graph_signature(bpy.data.materials[aid])
        for aid in ids if catalog.definitions[aid][1]['type'] == 'material'}, sort_keys=True)
    bpy.context.scene['lg_texture_payload_bytes'] = sum(row['estimatedPayloadBytes'] for row in costs)


def scalar_materials(catalog, ids):
    expected = json.loads(bpy.context.scene['lg_preview'])
    for aid, signature in expected.items():
        material = bpy.data.materials.get(aid)
        require(material and graph_signature(material) == signature, 'Unsupported material preview edit')
        require(material.get('lg_material_source_hash') in material_source_hashes(catalog.source(aid)), 'Material source changed; refresh the reviewed preview explicitly')
        material_source = json.loads(catalog.source(aid).read_text(encoding='utf-8-sig'))
        for node in material.node_tree.nodes:
            if node.type == 'TEX_IMAGE':
                require(node.image.get('lg_source_hash') == sha(catalog.source(material_source[node.label])),
                        'Texture source changed; refresh the reviewed preview explicitly')
    for aid in ids:
        if catalog.definitions[aid][1]['type'] != 'material':
            continue
        material = bpy.data.materials[aid]
        material.node_tree.nodes.clear()
        bsdf = material.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
        output = material.node_tree.nodes.new('ShaderNodeOutputMaterial')
        material.node_tree.links.new(bsdf.outputs['BSDF'], output.inputs['Surface'])
        source = json.loads(catalog.source(aid).read_text(encoding='utf-8-sig'))
        material.diffuse_color = source.get('baseColorFactor', [1,1,1,1])
        for name, key in (('Base Color', 'baseColorFactor'), ('Metallic', 'metallicFactor'), ('Roughness', 'roughnessFactor')):
            bsdf.inputs[name].default_value = source.get(key, [1,1,1,1] if name == 'Base Color' else 1)
        material['fl_preview'] = preview_state(material)
    for image in list(bpy.data.images):
        if image.users == 0:
            bpy.data.images.remove(image)


def run(request):
    output = Path(request['outputRoot'])
    report = {'success': False, 'errors': []}
    try:
        parser = argparse.ArgumentParser()
        parser.add_argument('--action', choices=['Author', 'Export'], required=True)
        parser.add_argument('--family', choices=FAMILIES, required=True)
        parser.add_argument('--blend-file', type=Path)
        args = parser.parse_args(request['arguments'])
        require(not (output / 'source').exists(), 'Use fresh logistics output')
        original = Catalog(request['inputRoot'])
        fixture = Path(__file__).parent / 'fixtures' / ('logistics_' + args.family + '.json')
        ids, families = fixture_selection(original, fixture, [])
        signature = original.signature(ids)[0]
        metadata = bindings(original, ids)
        original.copy_to(ids, output / 'source')
        catalog = Catalog(output / 'source')
        root = 'building.directorate.' + args.family
        kit = json.loads((original.root / 'buildings/directorate/authoring' / (args.family + '.kit.json')).read_text())
        if args.action == 'Author':
            require(not args.blend_file, 'Authoring never replaces a saved master')
            parts = {}
            for aid in ids:
                if aid != root and not aid.startswith(root + '.'):
                    continue
                suffix = aid[len(root):].lstrip('.') or 'lod0'
                source = original.root / 'buildings/directorate/sources/logistics' / args.family / (suffix + '.gltf')
                ranges = json.loads(source.read_text())['extras']['componentHierarchy']
                parts[aid] = [{'name': p['name'], 'firstIndex': p['firstIndex'],
                               'indexCount': p['indexCount'], 'parent': -1, 'motion': 'static'} for p in ranges]
        else:
            require(args.blend_file and args.blend_file.is_file(), 'Saved logistics master required')
            bpy.ops.wm.open_mainfile(filepath=str(args.blend_file))
            require(bpy.context.scene['lg_family'] == args.family, 'Logistics family changed')
            require(json.loads(bpy.context.scene['lg_metadata']) == metadata, 'Logistics source metadata changed')
            parts = json.loads(bpy.context.scene['lg_modules'])
            scalar_materials(catalog, ids)
        for aid, ranges in parts.items():
            catalog.definitions[aid][1]['mechanicalParts'] = copy.deepcopy(ranges)
        geometries = {aid: read_geometry(catalog.source(aid), catalog.root, catalog.definitions[aid][1])
                      for aid in ids if catalog.definitions[aid][1]['type'] == 'mesh'}
        if args.action == 'Author':
            import_scene(catalog, ids, families, geometries)
            bpy.context.scene['lg_family'] = args.family
            bpy.context.scene['lg_metadata'] = json.dumps(metadata, sort_keys=True)
            bpy.context.scene['lg_modules'] = json.dumps(parts, sort_keys=True)
            layers(families)
        else:
            contract = json.loads(bpy.context.scene['fl_contract'])
            require(contract['ids'] == ids and contract['families'] == families, 'Logistics family contract changed')
            contract['sourceSignature'] = catalog.signature(ids)[0]
            bpy.context.scene['fl_contract'] = json.dumps(contract, sort_keys=True)
            for obj in bpy.context.scene.objects:
                if obj.get('fl_role') == 'geometry' and 'fl_normal_preview' in obj.data.attributes:
                    expected = obj.data.attributes['fl_normal_preview'].data
                    require(all(max(abs(a-b) for a,b in zip(n.vector, expected[i].vector)) <= .0002
                                for i,n in enumerate(obj.data.corner_normals)), 'Unsupported split-normal preview edit')
        restore_normals()
        results, meshes, deviations = export_scene(catalog, ids, families, geometries,
            lambda aid, a, b: compare(a, b, kit['bounds']['min'], kit['bounds']['max'])
            if aid in parts else __import__('asset_contract').compare_geometry(a, b))
        # Only family meshes are adopted; dependencies remain canonical.
        for aid in parts:
            preserve_uvs(geometries[aid], results[aid])
            relative = original.definitions[aid][0].relative_to(original.root)
            definition = copy.deepcopy(original.definitions[aid][1])
            suffix = aid[len(root):].lstrip('.') or 'lod0'
            target = output / 'source/buildings/directorate/sources/logistics' / args.family / 'blender' / (suffix + '.glb')
            write_glb(target, results[aid])
            definition['source'] = os.path.relpath(target, (output / 'source' / relative).parent).replace('\\', '/')
            write_json(output / 'source' / relative, definition)
        exported = Catalog(output / 'source')
        exported.validate(ids)
        if args.action == 'Author':
            preview(exported, ids)
            bpy.context.scene['lg_description'] = 'Editable logistics modules; engine +Y/+Z, Blender +Z/-Y; real external loading lane remains authoritative'
            bpy.context.preferences.filepaths.save_version = 0
            bpy.ops.wm.save_as_mainfile(filepath=str(output / (args.family + '.blend')), check_existing=False, relative_remap=False)
        require(original.signature(ids)[0] == signature, 'Input sources changed')
        report.update(success=True, family=args.family, ids=ids, meshes=meshes, deviations=deviations,
                      modules=parts, sourceSignature=signature, productionSourcesUnchanged=True,
                      blendSha256=sha(args.blend_file or output / (args.family + '.blend')))
    except Exception as error:
        report['errors'].append(str(error))
        raise
    finally:
        write_json(output / 'logistics-report.json', report)


if __name__ == '__main__':
    run(json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig')))


