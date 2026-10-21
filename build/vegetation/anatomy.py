"""Editable selection groups for the actual baked plant anatomy."""
import bpy
from asset_contract import require


def add_groups():
    rows = []
    for obj in bpy.context.scene.objects:
        aid = obj.get('fl_asset', '')
        if obj.get('fl_role') != 'geometry' or not aid.startswith('mesh.world.vegetation.'):
            continue
        name = aid.split('.')[-1].removesuffix('_lod1')
        low = aid.endswith('_lod1')
        if name == 'conifer':
            step = 36 if low else 60
            ranges = [('trunk',0,step)] + [('crown'+str(i+1),step*(i+1),step) for i in range(3)]
        elif name == 'broadleaf':
            trunk, branch, crown, count = (36,36,72,3) if low else (72,72,216,5)
            ranges = [('trunk',0,trunk)]
            for i in range(count):
                start = trunk + i*(branch+crown)
                ranges.extend([('branch'+str(i+1),start,branch),('crown'+str(i+1),start+branch,crown)])
        elif name == 'scrub':
            ranges = [('cluster'+str(i+1),24*i,24) for i in range(3 if low else 5)]
        elif name == 'grass_clump':
            step = 6 if low else 12
            ranges = [('blade'+str(i+1),step*i,step) for i in range(3)]
        else:
            raise ValueError('Unexpected plant anatomy')
        identities = obj.data.attributes['fl_vertex'].data
        require(len(identities) == sum(n for _,_,n in ranges), 'Review changed anatomical source partition')
        for label, first, count in ranges:
            vertices = [v.index for v in obj.data.vertices if first <= identities[v.index].value < first+count]
            require(len(vertices) == count, 'Incomplete anatomical selection group')
            group = obj.vertex_groups.get(label) or obj.vertex_groups.new(name=label)
            group.add(vertices, 1, 'REPLACE')
            rows.append({'asset': aid, 'group': label, 'vertices': len(vertices)})
    return rows
