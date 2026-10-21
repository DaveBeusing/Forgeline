"""Saved Cargo/Supply masters over the existing bounded Blender source adapter."""
import argparse
import copy
import hashlib
import json
import os
import sys
from pathlib import Path
import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
from asset_contract import Catalog, compare_geometry, fixture_selection, read_geometry, require, sha, write_glb, write_json
from logistics_truck_contract import FAMILIES, SUFFIXES, compare, component_parts, metadata
from mechanical import layers
from roundtrip import import_scene, export_scene
from roundtrip import preview_state
from production import surface_preview
from tank import restore_normals


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
            if hasattr(node, field):
                row[field] = getattr(node, field)
        if node.type == 'TEX_IMAGE':
            require(node.image and node.image.packed_file and not node.image.is_dirty, 'Truck packed texture changed')
            row['imageHash'] = hashlib.sha256(node.image.packed_file.data).hexdigest()
            row['colorSpace'] = node.image.colorspace_settings.name
        nodes.append(row)
    links = sorted((link.from_node.name, link.from_socket.identifier,
                    link.to_node.name, link.to_socket.identifier) for link in material.node_tree.links)
    return hashlib.sha256(json.dumps(dict(nodes=nodes, links=links), sort_keys=True).encode()).hexdigest()


def pack_preview(catalog, ids):
    surface_preview(catalog, ids)
    scene = bpy.context.scene
    scene['fl_truck_preview'] = json.dumps({aid: graph_signature(bpy.data.materials[aid])
        for aid in ids if catalog.definitions[aid][1]['type'] == 'material'}, sort_keys=True)
    scene['fl_truck_surfaces'] = json.dumps({aid: sha(catalog.source(aid)) for aid in ids
        if catalog.definitions[aid][1]['type'] in ('material', 'texture')}, sort_keys=True)
    for image in bpy.data.images:
        image.filepath = '//' + os.path.relpath(image.filepath,
            catalog.root / 'units/directorate/authoring').replace('\\', '/')
        for packed in image.packed_files:
            packed.filepath = image.filepath


def validate_preview(catalog, ids):
    scene = bpy.context.scene
    require(json.loads(scene['fl_truck_surfaces']) == {aid: sha(catalog.source(aid)) for aid in ids
        if catalog.definitions[aid][1]['type'] in ('material', 'texture')}, 'Truck material or texture source changed')
    for aid, signature in json.loads(scene['fl_truck_preview']).items():
        material = bpy.data.materials.get(aid)
        require(material and graph_signature(material) == signature, 'Truck PBR preview graph changed')
        material.node_tree.nodes.clear()
        bsdf = material.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
        out = material.node_tree.nodes.new('ShaderNodeOutputMaterial')
        material.node_tree.links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])
        source = json.loads(catalog.source(aid).read_text(encoding='utf-8-sig'))
        material.diffuse_color = source.get('baseColorFactor', [1,1,1,1])
        for name, key in (('Base Color', 'baseColorFactor'), ('Metallic', 'metallicFactor'), ('Roughness', 'roughnessFactor')):
            bsdf.inputs[name].default_value = source.get(key, [1,1,1,1] if name == 'Base Color' else 1)
        material['fl_preview'] = preview_state(material)


def run(request):
    output = Path(request['outputRoot'])
    report = {'schemaVersion': 1, 'success': False, 'errors': []}
    try:
        parser = argparse.ArgumentParser()
        parser.add_argument('--action', choices=('Author', 'Export'), required=True)
        parser.add_argument('--family', choices=FAMILIES, required=True)
        parser.add_argument('--fixtures', type=Path, required=True)
        parser.add_argument('--blend-file', type=Path)
        args = parser.parse_args(request['arguments'])
        catalog = Catalog(request['inputRoot'])
        root = 'unit.directorate.' + args.family
        ids, families = fixture_selection(catalog, args.fixtures, [root])
        require(len(families) == 1 and set(families[0]['assets']) == {root+s for s in SUFFIXES},
                'Truck fixture must include every LOD, state and collision')
        original = catalog.signature(ids)[0]
        definitions = metadata(catalog, ids)
        geometries = {aid: read_geometry(catalog.source(aid), catalog.root, catalog.definitions[aid][1])
                      for aid in ids if catalog.definitions[aid][1]['type'] == 'mesh'}
        runtime_parts = {}
        for aid, geometry in geometries.items():
            if aid.endswith('.collision'):
                continue
            definition = catalog.definitions[aid][1]
            runtime_parts[aid] = copy.deepcopy(definition.get('mechanicalParts'))
            definition['mechanicalParts'] = component_parts(definition, geometry)
        if args.action == 'Author':
            require(not args.blend_file, 'Author does not accept an existing master')
            import_scene(catalog, ids, families, geometries)
            scene = bpy.context.scene
            scene['fl_truck_metadata'] = json.dumps(definitions, sort_keys=True)
            scene['fl_truck_baseline'] = json.dumps({aid: {'primitives': g['primitives']}
                                                   for aid, g in geometries.items()}, sort_keys=True)
            layers(families)
        else:
            require(args.blend_file and args.blend_file.is_file(), 'Export requires a saved truck master')
            bpy.ops.wm.open_mainfile(filepath=str(args.blend_file.resolve()))
            scene = bpy.context.scene
            require(json.loads(scene.get('fl_truck_metadata', 'null')) == definitions,
                    'Truck metadata changed; migrate the master explicitly')
            baselines = json.loads(scene['fl_truck_baseline'])
            require(set(baselines) == set(geometries), 'Truck baseline asset set changed')
            for aid, geometry in geometries.items():
                compare_geometry(baselines[aid], geometry)
            contract = json.loads(scene['fl_contract'])
            require(contract['ids'] == ids and contract['families'] == families, 'Truck family changed')
            contract['sourceSignature'] = catalog.signature(ids)[0]
            scene['fl_contract'] = json.dumps(contract, sort_keys=True)
            validate_preview(catalog, ids)
        restore_normals()
        results, meshes, deviations = export_scene(catalog, ids, families, geometries,
            lambda aid, before, after: compare(aid, before, after, catalog.definitions[aid][1]))
        if args.action == 'Author':
            pack_preview(catalog, ids)
            bpy.ops.wm.save_as_mainfile(filepath=str(output / (args.family + '.blend')), check_existing=False)
        # The fine authoring partition is never promoted into runtime articulation.
        for aid, parts in runtime_parts.items():
            definition = catalog.definitions[aid][1]
            if parts is None:
                definition.pop('mechanicalParts', None)
            else:
                definition['mechanicalParts'] = parts
        source = output / 'source'
        catalog.copy_to(ids, source)
        exports = []
        for aid, geometry in results.items():
            if aid.endswith('.collision'):
                continue
            path, definition = catalog.definitions[aid]
            relative = path.relative_to(catalog.root)
            target = Path('../sources/combat_support') / args.family / 'blender' / (aid + '.glb')
            destination = source / relative.parent / target
            write_glb(destination, geometry)
            updated = copy.deepcopy(definition)
            updated['source'] = target.as_posix()
            write_json(source / relative, updated)
            exports.append({'id': aid, 'path': target.as_posix(), 'sha256': sha(destination)})
        Catalog(source).validate(ids)
        require(catalog.signature(ids)[0] == original, 'Input source changed during export')
        report.update(success=True, family=root, sourceSignature=original, meshes=meshes,
                      deviations=deviations, exports=exports,
                      blendSha256=sha(args.blend_file or output / (args.family + '.blend')))
    except Exception as error:
        report['errors'].append(str(error))
        raise
    finally:
        write_json(output / 'truck-report.json', report)


if __name__ == '__main__':
    run(json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig')))
