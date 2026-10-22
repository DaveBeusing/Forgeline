"""Bounded static infrastructure edits; topology and surface ownership stay fixed."""
import copy
import json
import math
from pathlib import Path
from asset_contract import Catalog, contained, compare_geometry, geometry_stats, require


def export_filename(aid):
    if aid.startswith('infrastructure.directorate.bridge.road.'):
        return 'bridge_'+aid.rsplit('.',1)[1]+'.glb'
    if aid.startswith('infrastructure.directorate.road.'):
        return 'road_'+aid.rsplit('.',1)[1]+'.glb'
    return 'world_'+aid.rsplit('.',1)[1]+'.glb'


def family_catalog(root, fixture):
    """Read the audited closure, avoiding a full catalog walk for every bpy reopen."""
    catalog=Catalog.__new__(Catalog)
    catalog.root=Path(root).resolve();catalog.definitions={}
    for aid,relative in fixture['definitions'].items():
        path=contained(catalog.root,catalog.root/relative)
        definition=json.loads(path.read_text(encoding='utf-8-sig'))
        require(definition['id']==aid,'Infrastructure definition path changed identity')
        catalog.definitions[aid]=(path,definition)
    return catalog


def canonical_geometry(before, after, collision=False):
    locked = copy.deepcopy(after)
    for original, candidate in zip(before['primitives'], locked['primitives']):
        candidate['attributes']['POSITION'] = original['attributes']['POSITION']
    compare_geometry(before, locked)
    result = copy.deepcopy(before)
    maximum = 0.0
    bounds = geometry_stats(before)
    for original, candidate, output in zip(before['primitives'], after['primitives'], result['primitives']):
        old = original['attributes']['POSITION']
        new = candidate['attributes']['POSITION']
        require(len(old) == len(new), 'Infrastructure vertex count changed')
        for index, (a, b) in enumerate(zip(old, new)):
            require(len(b) == 3 and all(math.isfinite(x) for x in b), 'Invalid infrastructure position')
            delta = max(abs(x-y) for x,y in zip(a,b))
            require(delta <= (1e-6 if collision else .01), 'Infrastructure shape edit exceeds normalized 0.01 bound')
            require(all(bounds['boundsMin'][k]-1e-6 <= b[k] <= bounds['boundsMax'][k]+1e-6 for k in range(3)),
                    'Infrastructure edit expands the existing culling bounds')
            if delta > 1e-6:
                output['attributes']['POSITION'][index] = tuple(b)
            maximum = max(maximum, delta)
    return result, maximum
