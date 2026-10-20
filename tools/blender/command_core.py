"""Saved modular Command Core authoring; generic preservation contracts stay shared."""
import argparse
import copy
import hashlib
import importlib.util
import json
import os
import sys
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from asset_contract import Catalog, fixture_selection, geometry_stats, read_geometry, require, sha, write_glb, write_json
from command_core_contract import ROOT, SUFFIXES, MATERIAL, bindings, compare, refined_kit, material_for
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
            row['image'] = node.image.get('cc_source_hash')
            row['packedSha256'] = hashlib.sha256(node.image.packed_file.data).hexdigest()
            row['colorSpace'] = node.image.colorspace_settings.name
        nodes.append(row)
    links = sorted((link.from_node.name, link.from_socket.identifier, link.to_node.name, link.to_socket.identifier)
                   for link in material.node_tree.links)
    return hashlib.sha256(json.dumps({'nodes': sorted(nodes, key=lambda n: n['name']), 'links': links}, sort_keys=True).encode()).hexdigest()


def preview(catalog, ids):
    costs = surface_preview(catalog, ids)
    require(sum(row['estimatedPayloadBytes'] for row in costs) <= 7325040,
            'Existing Command Core/tank surface texture budget exceeded')
    for aid in ids:
        if catalog.definitions[aid][1]['type'] == 'material':
            bpy.data.materials[aid]['cc_material_source_hash'] = sha(catalog.source(aid))
    for image in bpy.data.images:
        matching = next((aid for aid in ids if catalog.definitions[aid][1]['type'] == 'texture'
                         and Path(image.filepath).name == catalog.source(aid).name), None)
        if matching:
            image['cc_source_hash'] = sha(catalog.source(matching))
            image.filepath = '//' + os.path.relpath(catalog.source(matching), catalog.root / 'buildings/directorate/authoring').replace('\\', '/')
    bpy.context.scene['cc_preview'] = json.dumps({aid: graph_signature(bpy.data.materials[aid])
        for aid in ids if catalog.definitions[aid][1]['type'] == 'material'}, sort_keys=True)
    bpy.context.scene['cc_texture_payload_bytes'] = sum(row['estimatedPayloadBytes'] for row in costs)


def scalar_materials(catalog, ids):
    expected = json.loads(bpy.context.scene['cc_preview'])
    for aid, signature in expected.items():
        material = bpy.data.materials.get(aid)
        require(material and graph_signature(material) == signature, 'Unsupported material preview edit')
        require(material.get('cc_material_source_hash') == sha(catalog.source(aid)), 'Material source changed; refresh the reviewed preview explicitly')
        material_source = json.loads(catalog.source(aid).read_text(encoding='utf-8-sig'))
        for node in material.node_tree.nodes:
            if node.type == 'TEX_IMAGE':
                require(node.image.get('cc_source_hash') == sha(catalog.source(material_source[node.label])),
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


def virtual_parts(catalog, parts):
    for aid, ranges in parts.items():
        require(aid in catalog.definitions and all(p['motion'] == 'static' for p in ranges), 'Unexpected mechanical content')
        catalog.definitions[aid][1]['mechanicalParts'] = copy.deepcopy(ranges)


def bootstrap(catalog, ids, output):
    repository = Path(__file__).resolve().parents[2]
    spec = importlib.util.spec_from_file_location('command_core_components', repository / 'build/Author-CommandCore.py')
    producer = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(producer)
    kit = refined_kit(json.loads((catalog.root / 'buildings/directorate/authoring/command_core.kit.json').read_text()))
    source = output / 'seed'
    catalog.copy_to(ids, source)
    parts = {}
    names = {'foundation': 'ground_foundation', 'command_blocks': 'heavy_command_bunker',
             'access_portal': 'loading_access', 'roof_systems': 'roof_mechanical_array', 'communications': 'signal_array'}
    for suffix in SUFFIXES:
        aid = ROOT + suffix
        lod = int(suffix[-1]) if suffix.startswith('.lod') else 0 if suffix == '' else 1
        selected = kit['intactModules'] if suffix in ('', '.lod1', '.lod2') else kit['states'][suffix[1:]]
        assembly = producer.Mesh()
        ranges = []
        for module in kit['modules']:
            if module['name'] not in selected:
                continue
            mesh = producer.assemble(kit, lod, [module['name']])
            # Only the access-facing glass panes emit; backs and edges are steel.
            # The existing unpowered shutters cover those same front surfaces.
            for index, uv in enumerate(mesh.uvs):
                if .25 < uv[1] < .5 and 0 < uv[0] < .25 and mesh.normals[index][2] < .9999:
                    mesh.uvs[index] = (uv[0] + .25, uv[1] - .25)
            if not mesh.indices:
                continue
            ranges.append({'name': names.get(module['name'], module['name']), 'firstIndex': len(assembly.indices),
                           'indexCount': len(mesh.indices), 'parent': -1, 'motion': 'static'})
            offset = len(assembly.positions)
            assembly.positions.extend(mesh.positions)
            assembly.normals.extend(mesh.normals)
            assembly.uvs.extend(mesh.uvs)
            assembly.indices.extend(index + offset for index in mesh.indices)
        filename = suffix[1:] if suffix else 'lod0'
        temporary = output / 'bootstrap' / (filename + '.gltf')
        assembly.export(temporary)
        path, definition = catalog.definitions[aid]
        relative = path.relative_to(catalog.root)
        updated = copy.deepcopy(definition)
        if material_for(suffix):
            updated['materialReferences'] = [material_for(suffix)]
        target = source / 'buildings/directorate/sources/command_core/blender' / (filename + '.glb')
        geometry = read_geometry(temporary, output, updated)
        write_glb(target, geometry)
        updated['source'] = os.path.relpath(target, (source / relative).parent).replace('\\', '/')
        write_json(source / relative, updated)
        parts[aid] = ranges
    return Catalog(source), parts


def run(request):
    output = Path(request['outputRoot'])
    report = {'success': False, 'errors': []}
    try:
        parser = argparse.ArgumentParser()
        parser.add_argument('--action', choices=['Author', 'Export'], required=True)
        parser.add_argument('--fixtures', type=Path, required=True)
        parser.add_argument('--blend-file', type=Path)
        args = parser.parse_args(request['arguments'])
        original = Catalog(request['inputRoot'])
        ids, families = fixture_selection(original, args.fixtures, [])
        original_ids = list(ids)
        original_signature = original.signature(ids)[0]
        if args.action == 'Author':
            require(not args.blend_file, 'Bootstrap must not reopen or replace a saved master')
            catalog, parts = bootstrap(original, ids, output)
            ids, families = fixture_selection(catalog, args.fixtures, [])
            catalog.copy_to(ids, output / 'source')
            catalog = Catalog(output / 'source')
            metadata = bindings(catalog, ids)
        else:
            require(args.blend_file and args.blend_file.is_file(), 'A saved Command Core master is required')
            bpy.ops.wm.open_mainfile(filepath=str(args.blend_file))
            require(json.loads(bpy.context.scene['cc_metadata']) == bindings(original, ids), 'Command Core source metadata changed; migrate explicitly')
            parts = json.loads(bpy.context.scene['cc_modules'])
            metadata = bindings(original, ids)
            original.copy_to(ids, output / 'source')
            catalog = Catalog(output / 'source')
            scalar_materials(catalog, ids)
        virtual_parts(catalog, parts)
        geometries = {aid: read_geometry(catalog.source(aid), catalog.root, catalog.definitions[aid][1])
                      for aid in ids if catalog.definitions[aid][1]['type'] == 'mesh'}
        if args.action == 'Author':
            import_scene(catalog, ids, families, geometries)
            bpy.context.scene['cc_metadata'] = json.dumps(metadata, sort_keys=True)
            bpy.context.scene['cc_modules'] = json.dumps(parts, sort_keys=True)
            layers(families)
        else:
            # Reviewed source bindings may advance; no regeneration or reset of Blender geometry.
            contract = json.loads(bpy.context.scene['fl_contract'])
            require(contract['ids'] == ids and contract['families'] == families, 'Command Core family changed')
            contract['sourceSignature'] = catalog.signature(ids)[0]
            bpy.context.scene['fl_contract'] = json.dumps(contract, sort_keys=True)
        if args.action == 'Export':
            for obj in bpy.context.scene.objects:
                if obj.get('fl_role') != 'geometry' or 'fl_normal_preview' not in obj.data.attributes:
                    continue
                expected = obj.data.attributes['fl_normal_preview'].data
                require(all(max(abs(a-b) for a,b in zip(normal.vector, expected[i].vector)) <= .0002
                            for i, normal in enumerate(obj.data.corner_normals)), 'Unsupported split-normal preview edit')
        restore_normals()
        results, meshes, deviations = export_scene(catalog, ids, families, geometries,
            lambda aid, a, b: compare(aid, a, b, catalog.definitions[aid][1]))
        source = output / 'source'
        for aid in (ROOT + suffix for suffix in SUFFIXES):
            relative = original.definitions[aid][0].relative_to(original.root)
            definition = copy.deepcopy(original.definitions[aid][1])
            if material_for(aid[len(ROOT):]):
                definition['materialReferences'] = [material_for(aid[len(ROOT):])]
            target = source / 'buildings/directorate/sources/command_core/blender' / ((aid[len(ROOT):].lstrip('.') or 'lod0') + '.glb')
            write_glb(target, results[aid])
            definition['source'] = os.path.relpath(target, (source / relative).parent).replace('\\', '/')
            write_json(source / relative, definition)
        exported = Catalog(source)
        exported.validate(ids)
        if args.action == 'Author':
            preview(exported, ids)
            bpy.context.scene['cc_description'] = 'Editable Command Core modular source; engine +Y/+Z, Blender +Z/-Y; canonical instance 20 x 12 x 20 metres'
            bpy.context.preferences.filepaths.save_version = 0
            bpy.ops.wm.save_as_mainfile(filepath=str(output / 'command_core.blend'), check_existing=False, relative_remap=False)
        require(original.signature(original_ids)[0] == original_signature, 'Input sources changed during authoring')
        report.update(success=True, sourceSignature=original_signature, ids=ids, meshes=meshes,
                      deviations=deviations, modules=parts, productionSourcesUnchanged=True,
                      blendSha256=sha(args.blend_file) if args.blend_file else sha(output / 'command_core.blend'))
    except Exception as error:
        report['errors'].append(str(error))
        raise
    finally:
        write_json(output / 'command-core-report.json', report)


if __name__ == '__main__':
    run(json.loads(Path(sys.argv[sys.argv.index('--request') + 1]).read_text(encoding='utf-8-sig')))
