"""Infantry atlas orientation and source-only packed material preview."""
from pathlib import Path
import json
import hashlib

MAPS = (('base', 'surface_atlas.asset.json', 'sRGB'),
        ('normal', 'surface_normal.asset.json', 'Non-Color'),
        ('orm', 'surface_orm.asset.json', 'Non-Color'))


def blender_uv(uv):
    """glTF/D3D top-left coordinates to Blender's bottom-left image coordinates."""
    return uv[0], 1.0 - uv[1]


def packed_surface(material, source):
    import bpy
    surface = Path(source) / 'materials/directorate/surfaces'
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    nodes.clear()
    output = nodes.new('ShaderNodeOutputMaterial'); output.location = (650, 0)
    shader = nodes.new('ShaderNodeBsdfPrincipled'); shader.location = (350, 0)
    links.new(shader.outputs['BSDF'], output.inputs['Surface'])
    textures = {}
    for channel, definition, color in MAPS:
        record = json.loads((surface / definition).read_text(encoding='utf-8-sig'))
        path = surface / record['source']
        # Save As can remap paths for a staging directory. Packed bytes identify
        # the exact canonical map even when that staged master was relocated.
        canonical = path.read_bytes()
        image = next((item for item in bpy.data.images
                      if Path(bpy.path.abspath(item.filepath)).resolve() == path.resolve()
                      or item.packed_file and bytes(item.packed_file.data) == canonical), None)
        if image is None:
            image = bpy.data.images.load(str(path.resolve()), check_existing=False)
        if image.packed_file and bytes(image.packed_file.data) != canonical:
            raise ValueError('Packed infantry atlas differs from its canonical source')
        image.colorspace_settings.name = color
        image.pack()
        if bytes(image.packed_file.data) != canonical:
            raise ValueError('Packed infantry atlas bytes changed')
        # Relative to the canonical infantry master; packed bytes are portable.
        image.filepath = '//../../../../materials/directorate/surfaces/' + path.name
        texture = nodes.new('ShaderNodeTexImage'); texture.image = image
        texture.label = channel; texture.location = (-600, 300 - len(textures) * 300)
        textures[channel] = texture
    links.new(textures['base'].outputs['Color'], shader.inputs['Base Color'])
    normal = nodes.new('ShaderNodeNormalMap'); normal.location = (50, -250)
    links.new(textures['normal'].outputs['Color'], normal.inputs['Color'])
    links.new(normal.outputs['Normal'], shader.inputs['Normal'])
    split = nodes.new('ShaderNodeSeparateColor'); split.mode = 'RGB'; split.location = (-100, -550)
    links.new(textures['orm'].outputs['Color'], split.inputs['Color'])
    links.new(split.outputs['Green'], shader.inputs['Roughness'])
    links.new(split.outputs['Blue'], shader.inputs['Metallic'])
    material['runtime_material'] = 'material.directorate.unit.rifle_squad'
    material['orm_channels'] = 'R=AO,G=Roughness,B=Metallic; AO remains runtime lighting data'


def validate_packed_surface(material, source):
    import bpy
    surface = Path(source) / 'materials/directorate/surfaces'
    images = [node for node in material.node_tree.nodes if node.type == 'TEX_IMAGE']
    if len(images) != 3 or {node.label for node in images} != {item[0] for item in MAPS}:
        raise ValueError('Saved infantry preview requires three labelled atlas maps')
    records = []
    for channel, definition, color in MAPS:
        record = json.loads((surface / definition).read_text(encoding='utf-8-sig'))
        path = surface / record['source']
        image = next(node.image for node in images if node.label == channel)
        if not image or not image.packed_file or bytes(image.packed_file.data) != path.read_bytes():
            raise ValueError('Saved infantry atlas is missing or differs from canonical bytes')
        if image.colorspace_settings.name != color or Path(bpy.path.abspath(image.filepath)).resolve() != path.resolve():
            raise ValueError('Saved infantry atlas color space or relative path differs')
        records.append(dict(channel=channel, colorSpace=color, relativePath=image.filepath,
                            sha256=hashlib.sha256(bytes(image.packed_file.data)).hexdigest()))
    return records
