"""Infantry atlas orientation and source-only packed material preview."""
from pathlib import Path
import json


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
    for channel, definition, color in (
        ('base', 'surface_atlas.asset.json', 'sRGB'),
        ('normal', 'surface_normal.asset.json', 'Non-Color'),
        ('orm', 'surface_orm.asset.json', 'Non-Color')):
        record = json.loads((surface / definition).read_text(encoding='utf-8-sig'))
        path = surface / record['source']
        image = bpy.data.images.load(str(path.resolve()), check_existing=True)
        image.colorspace_settings.name = color
        image.pack()
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
