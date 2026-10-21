"""Saved terrain material sources; bounded node edits and CPU Cycles emission bakes."""
import argparse
import base64
import json
import math
import struct
import sys
from pathlib import Path
import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from terrain_pbr_contract import FAMILIES, MAPS, TEXTURES, TILES, binding, controls, digest, require
from pbr_png import write_png


def review_gltf(record, folder):
    """Editor-only flat metre patch; never substituted for the engine heightfield."""
    size = record['tileMetres']/2
    positions = [(-size,0,size),(size,0,size),(size,0,-size),(-size,0,-size)]
    # Same UV and tangent convention as Blender's +Z plane converted to glTF +Y.
    uv = [(0,1),(1,1),(1,0),(0,0)]
    raw = b''.join(struct.pack('<3f',*v) for v in positions)
    raw += b''.join(struct.pack('<3f',0,1,0) for _ in positions)
    raw += b''.join(struct.pack('<2f',*v) for v in uv)
    raw += struct.pack('<6H',0,1,2,0,2,3)
    materials = []
    for slot, m in record['materials'].items():
        materials.append({'name':'material.world.terrain.'+slot,
            'pbrMetallicRoughness':{'baseColorFactor':m['baseColorFactor'],'baseColorTexture':{'index':0},
                'roughnessFactor':m['roughnessFactor'],'metallicFactor':m['metallicFactor'],
                'metallicRoughnessTexture':{'index':2}},
            'normalTexture':{'index':1},'occlusionTexture':{'index':2}})
    d={'asset':{'version':'2.0','generator':'Terrain material swatches'},'scene':0,
       'scenes':[{'nodes':list(range(len(materials)))}],
       'nodes':[{'mesh':i,'name':m['name'],'translation':[i*record['tileMetres']*1.2,0,0]} for i,m in enumerate(materials)],
       'meshes':[{'primitives':[{'attributes':{'POSITION':0,'NORMAL':1,'TEXCOORD_0':2},'indices':3,'material':i}]} for i in range(len(materials))],
       'materials':materials,'samplers':[{'magFilter':9729,'minFilter':9987,'wrapS':10497,'wrapT':10497}],
       'images':[{'uri':record['family']+'_production_'+kind+'.png'} for kind in MAPS],
       'textures':[{'source':i,'sampler':0} for i in range(3)],
       'buffers':[{'byteLength':len(raw),'uri':'data:application/octet-stream;base64,'+base64.b64encode(raw).decode()}],
       'bufferViews':[{'buffer':0,'byteOffset':offset,'byteLength':length} for offset,length in [(0,48),(48,48),(96,32),(128,12)]],
       'accessors':[{'bufferView':0,'componentType':5126,'count':4,'type':'VEC3','min':[-size,0,-size],'max':[size,0,size]},
                    {'bufferView':1,'componentType':5126,'count':4,'type':'VEC3'},
                    {'bufferView':2,'componentType':5126,'count':4,'type':'VEC2'},
                    {'bufferView':3,'componentType':5123,'count':6,'type':'SCALAR'}],
       'extras':{'purpose':'Material inspection only; no gameplay terrain mesh','effectiveRepeatMetres':record['tileMetres']}}
    (folder/'surface.gltf').write_text(json.dumps(d,indent=2)+'\n',encoding='utf-8')


def plain(value):
    if isinstance(value, (str, int, float, bool)) or value is None:
        return value
    return list(value)


def graph(mat):
    nodes = []
    for n in mat.node_tree.nodes:
        d = {'name': n.name, 'type': n.bl_idname}
        for key in ('operation', 'blend_type', 'use_clamp', 'noise_dimensions', 'normalize',
                    'interpolation', 'extension', 'projection', 'mode', 'space'):
            if hasattr(n, key):
                d[key] = getattr(n, key)
        d['inputs'] = [(i, plain(s.default_value)) for i, s in enumerate(n.inputs) if hasattr(s, 'default_value')]
        d['outputs'] = [(i, plain(s.default_value)) for i, s in enumerate(n.outputs)
                        if hasattr(s, 'default_value') and n.type in ('RGB', 'VALUE') and
                        n.name not in ('Tint', 'RoughnessBias', 'DetailAmplitude')]
        if n.type == 'TEX_IMAGE':
            d['image'] = n.image.name if n.image else None
        nodes.append(d)
    return json.loads(json.dumps({'nodes': sorted(nodes, key=lambda n: n['name']),
            'links': sorted((l.from_node.name, list(l.from_node.outputs).index(l.from_socket),
                             l.to_node.name, list(l.to_node.inputs).index(l.to_socket)) for l in mat.node_tree.links)}))


def settings(scene):
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 1
    scene.cycles.seed = 2026
    scene.cycles.use_adaptive_sampling = False
    scene.render.threads_mode = 'FIXED'
    scene.render.threads = 1
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.image_settings.color_depth = '8'
    scene.render.dither_intensity = 0
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    scene.view_settings.exposure = 0
    scene.view_settings.gamma = 1


def author(source, family, out):
    record = binding(source, family)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    settings(bpy.context.scene)
    bpy.ops.mesh.primitive_plane_add(size=TILES[family])
    obj = bpy.context.object
    obj.name = 'TerrainSwatch'
    obj.data.uv_layers.active.name = 'UV0'
    mat = bpy.data.materials.new('Terrain:'+family)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    nodes.clear()
    links = mat.node_tree.links
    def node(kind, name):
        n = nodes.new(kind)
        n.name = n.label = name
        return n
    def math_node(name, operation, a=None, b=None):
        n = node('ShaderNodeMath', name)
        n.operation = operation
        for i, v in enumerate((a, b)):
            if v is None: continue
            if isinstance(v, (int, float)): n.inputs[i].default_value = v
            else: links.new(v, n.inputs[i])
        return n.outputs[0]
    uv = node('ShaderNodeTexCoord', 'Coordinates')
    images = {}
    for kind, item in record['maps'].items():
        image = bpy.data.images.load(str(source/item['original']), check_existing=False)
        image.name = family+':'+kind
        image.colorspace_settings.name = 'sRGB' if kind == 'base' else 'Non-Color'
        image.use_fake_user = True
        image.pack()
        image.filepath = '//../../textures/terrain/'+Path(item['original']).name
        for packed in image.packed_files: packed.filepath = image.filepath
        n = node('ShaderNodeTexImage', 'Source:'+kind)
        n.image = image
        n.interpolation = 'Closest'
        n.extension = 'REPEAT'
        links.new(uv.outputs['UV'], n.inputs['Vector'])
        images[kind] = n
    tint = node('ShaderNodeRGB', 'Tint')
    tint.outputs[0].default_value = (1, 1, 1, 1)
    bias = node('ShaderNodeValue', 'RoughnessBias')
    bias.outputs[0].default_value = 0
    amplitude = node('ShaderNodeValue', 'DetailAmplitude')
    amplitude.outputs[0].default_value = 0
    # A 4D torus is exactly periodic in both UV directions; no edge clamping or duplicated border texel.
    separate = node('ShaderNodeSeparateXYZ', 'UVComponents')
    links.new(uv.outputs['UV'], separate.inputs[0])
    u = math_node('UAngle', 'MULTIPLY', separate.outputs['X'], math.tau)
    v = math_node('VAngle', 'MULTIPLY', separate.outputs['Y'], math.tau)
    vector = node('ShaderNodeCombineXYZ', 'TorusXYZ')
    links.new(math_node('CosU', 'COSINE', u), vector.inputs['X'])
    links.new(math_node('SinU', 'SINE', u), vector.inputs['Y'])
    links.new(math_node('CosV', 'COSINE', v), vector.inputs['Z'])
    noise = node('ShaderNodeTexNoise', 'PeriodicNoise')
    noise.noise_dimensions = '4D'
    noise.inputs['Scale'].default_value = 8
    noise.inputs['Detail'].default_value = 2
    noise.inputs['Roughness'].default_value = .5
    links.new(vector.outputs[0], noise.inputs['Vector'])
    links.new(math_node('SinV', 'SINE', v), noise.inputs['W'])
    centered = math_node('CenteredNoise', 'SUBTRACT', noise.outputs['Fac'], .5)
    strength = math_node('DetailStrength', 'MULTIPLY', amplitude.outputs[0], 2)
    gain = math_node('DetailGain', 'ADD', math_node('ScaledNoise', 'MULTIPLY', centered, strength), 1)
    color = node('ShaderNodeMixRGB', 'TintedReflectance')
    color.blend_type = 'MULTIPLY'
    color.inputs[0].default_value = 1
    links.new(images['base'].outputs['Color'], color.inputs[1])
    links.new(tint.outputs[0], color.inputs[2])
    detail = node('ShaderNodeMixRGB', 'BaseOutput')
    detail.blend_type = 'MULTIPLY'
    detail.inputs[0].default_value = 1
    links.new(color.outputs[0], detail.inputs[1])
    links.new(gain, detail.inputs[2])
    channels = node('ShaderNodeSeparateColor', 'ORMChannels')
    channels.mode = 'RGB'
    links.new(images['orm'].outputs['Color'], channels.inputs[0])
    rough = math_node('BiasedRoughness', 'ADD', channels.outputs['Green'], bias.outputs[0])
    nodes['BiasedRoughness'].use_clamp = True
    orm = node('ShaderNodeCombineColor', 'ORMOutput')
    orm.mode = 'RGB'
    links.new(channels.outputs['Red'], orm.inputs['Red'])
    links.new(rough, orm.inputs['Green'])
    links.new(channels.outputs['Blue'], orm.inputs['Blue'])
    normal = node('ShaderNodeNormalMap', 'TangentNormal')
    normal.space = 'TANGENT'
    normal.inputs['Strength'].default_value = 1
    links.new(images['normal'].outputs['Color'], normal.inputs['Color'])
    bsdf = node('ShaderNodeBsdfPrincipled', 'Preview')
    links.new(detail.outputs[0], bsdf.inputs['Base Color'])
    links.new(normal.outputs[0], bsdf.inputs['Normal'])
    links.new(rough, bsdf.inputs['Roughness'])
    links.new(channels.outputs['Blue'], bsdf.inputs['Metallic'])
    output = node('ShaderNodeOutputMaterial', 'Output')
    links.new(bsdf.outputs[0], output.inputs['Surface'])
    obj.data.materials.append(mat)
    for index, n in enumerate(nodes): n.location = ((index % 6)*260, -(index//6)*240)
    # Principled has no direct ORM AO input. ORM R remains an independent export channel.
    record['graph'] = graph(mat)
    bpy.context.scene['terrain_source'] = json.dumps(record, sort_keys=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(out/(family+'.blend')), compress=True)
    return record


def validate(source, family):
    scene = bpy.context.scene
    record = json.loads(scene['terrain_source'])
    expected = binding(source, family)
    require({k:v for k,v in record.items() if k != 'graph'} == expected, 'Terrain source/slot/compiler input changed')
    require(scene.unit_settings.scale_length == 1 and scene.unit_settings.system == 'METRIC', 'Metre units changed')
    require(len(bpy.data.objects) == 1 and 'TerrainSwatch' in bpy.data.objects and len(bpy.data.materials) == 1,
            'Terrain swatch topology/scene changed')
    obj = bpy.data.objects['TerrainSwatch']
    require(obj.type == 'MESH' and len(obj.data.vertices) == 4 and len(obj.data.polygons) == 1 and
            len(obj.modifiers) == len(obj.constraints) == 0 and tuple(obj.location) == (0,0,0) and
            tuple(obj.rotation_euler) == (0,0,0) and tuple(obj.scale) == (1,1,1), 'Swatch transform/geometry changed')
    require(obj.animation_data is None and obj.data.animation_data is None and obj.data.shape_keys is None and
            len(obj.data.materials) == 1 and obj.data.materials[0].name == 'Terrain:'+family,
            'Unsupported swatch animation/shape/material binding')
    require(all(abs(v.co.z) < 1e-6 and abs(abs(v.co.x)-TILES[family]/2) < 1e-5 and
                abs(abs(v.co.y)-TILES[family]/2) < 1e-5 for v in obj.data.vertices), 'Physical repeat changed')
    require(len(obj.data.uv_layers) == 1 and obj.data.uv_layers.active.name == 'UV0' and
            sorted(tuple(x.uv) for x in obj.data.uv_layers.active.data) == [(0,0),(0,1),(1,0),(1,1)], 'UV orientation changed')
    mat = bpy.data.materials['Terrain:'+family]
    require(mat.animation_data is None and mat.node_tree.animation_data is None and graph(mat) == record['graph'],
            'Unsupported material graph edit')
    n = mat.node_tree.nodes
    controls(tuple(n['Tint'].outputs[0].default_value), n['RoughnessBias'].outputs[0].default_value,
             n['DetailAmplitude'].outputs[0].default_value)
    require(len(bpy.data.images) == 3, 'Unexpected image source')
    for kind, item in record['maps'].items():
        image = n['Source:'+kind].image
        require(image.packed_file and digest(source/item['original']) == item['originalSha256'] and
                __import__('hashlib').sha256(image.packed_file.data).hexdigest() == item['originalSha256'],
                'Packed terrain source changed')
        require(tuple(image.size) == (4096,4096) and image.colorspace_settings.name ==
                ('sRGB' if kind == 'base' else 'Non-Color'), 'Packed source extent/color space changed')
    return record, obj, mat


def export(source, family, master, out):
    master_hash = digest(master)
    bpy.ops.wm.open_mainfile(filepath=str(master), load_ui=False)
    record, obj, mat = validate(source, family)
    settings(bpy.context.scene)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    emit = nodes.new('ShaderNodeEmission')
    emit.inputs['Strength'].default_value = 1
    target = nodes.new('ShaderNodeTexImage')
    result = {}
    for kind in MAPS:
        image = bpy.data.images.new('Baked:'+kind, width=4096, height=4096, alpha=False)
        image.colorspace_settings.name = 'sRGB' if kind == 'base' else 'Non-Color'
        target.image = image
        nodes.active = target
        socket = nodes['BaseOutput' if kind == 'base' else 'ORMOutput'].outputs[0] if kind != 'normal' else nodes['Source:normal'].outputs['Color']
        links.new(socket, emit.inputs['Color'])
        links.new(emit.outputs[0], nodes['Output'].inputs['Surface'])
        bpy.ops.object.bake(type='EMIT', use_clear=True, margin=0)
        path = out/'source'/TEXTURES/'blender'/family/(family+'_production_'+kind+'.png')
        path.parent.mkdir(parents=True, exist_ok=True)
        image.filepath_raw = str(path)
        image.file_format = 'PNG'
        image.save()
        # Verify decoded numeric data without applying a display transform.
        check = bpy.data.images.load(str(path), check_existing=False)
        check.colorspace_settings.name = 'Non-Color'
        data = np.empty(4096*4096*4, np.float32)
        check.pixels.foreach_get(data)
        pixels = np.rint(data.reshape(4096,4096,4)*255).astype(np.uint8)[::-1].copy()
        require(np.all(pixels[:,:,3] == 255), 'Baked alpha changed')
        if kind == 'normal':
            normals = pixels[:,:,:3].astype(np.float32)/127.5-1
            length = np.linalg.norm(normals, axis=2)
            require(np.all((length > .97) & (length < 1.03)) and np.min(normals[:,:,2]) > 0,
                    'Baked tangent normal length/orientation changed')
        if kind == 'orm': require(np.all(pixels[:,:,2] == 0), 'Terrain dielectric metallic changed')
        # Stable, lossless encoder, independent of image metadata or Blender path strings.
        write_png(path,4096,4096,pixels.tobytes())
        result[kind] = {'sha256':digest(path), 'source':path.relative_to(out/'source').as_posix()}
        d = dict(record['maps'][kind]['policy'])
        d['source'] = 'blender/'+family+'/'+family+'_production_'+kind+'.png'
        p = out/'source'/record['maps'][kind]['definition']
        p.write_text(json.dumps(d,indent=2)+'\n',encoding='utf-8')
        bpy.data.images.remove(check)
        bpy.data.images.remove(image)
    require(digest(master) == master_hash, 'Saved source changed during export')
    review_gltf(record, out/'source'/TEXTURES/'blender'/family)
    report = {'family':family, 'masterSha256':master_hash, 'maps':result,
              'method':'Blender 5.2.2 CPU Cycles EMIT, 4096 square, one sample/thread; no lighting or view transform baked',
              'controls':{'tint':list(nodes['Tint'].outputs[0].default_value),
                          'roughnessBias':nodes['RoughnessBias'].outputs[0].default_value,
                          'periodicDetail':nodes['DetailAmplitude'].outputs[0].default_value}}
    (out/'export.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    return report


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--request',required=True)
    request = json.loads(Path(p.parse_args(sys.argv[sys.argv.index('--')+1:]).request).read_text(encoding='utf-8-sig'))
    a = argparse.ArgumentParser()
    a.add_argument('--action',choices=['Author','Export','Check'],required=True)
    a.add_argument('--family',choices=FAMILIES,required=True)
    a.add_argument('--master',type=Path)
    args = a.parse_args(request['arguments'])
    require(bpy.app.version == (5,2,2), 'Pinned Blender 5.2.2 required')
    source, out = Path(request['inputRoot']), Path(request['outputRoot'])
    if args.action == 'Author': author(source,args.family,out)
    else:
        require(args.master and args.master.is_file(), 'Saved master required')
        if args.action == 'Export': export(source,args.family,args.master,out)
        else:
            bpy.ops.wm.open_mainfile(filepath=str(args.master),load_ui=False)
            validate(source,args.family)


if __name__ == '__main__': main()
