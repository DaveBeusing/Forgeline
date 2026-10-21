"""Preserve truck components and runtime articulation in saved Blender masters."""
import copy
import math
from asset_contract import POSITION_EPS, compare_geometry, require

FAMILIES = ('cargo_truck', 'supply_truck')
SUFFIXES = ('', '.lod1', '.lod2', '.damaged', '.critical', '.wreck', '.collision')


def metadata(catalog, ids):
    return {aid: {key: value for key, value in catalog.definitions[aid][1].items()
                  if key != 'source'} for aid in ids}


def canonical_export(before, after):
    """Remove editor round-off while retaining intentional rest-position edits."""
    result = copy.deepcopy(after)
    for original, exported in zip(before['primitives'], result['primitives']):
        for semantic, values in original['attributes'].items():
            if semantic != 'POSITION':
                exported['attributes'][semantic] = copy.deepcopy(values)
            else:
                exported['attributes'][semantic] = [tuple(a if abs(a-b) <= POSITION_EPS else b
                    for a, b in zip(old, new)) for old, new in
                    zip(values, exported['attributes'][semantic])]
    return result


def component_parts(definition, geometry):
    """Expose static components without increasing runtime draw/part counts."""
    components = geometry['document'].get('extras', {}).get('componentHierarchy', [])
    require(components, 'Truck source has no component hierarchy')
    moving = [copy.deepcopy(p) for p in definition.get('mechanicalParts', [])
              if p['motion'] != 'static']
    parts = []
    for component in components:
        first, count = component['firstIndex'], component['indexCount']
        owner = next((p for p in moving if p['firstIndex'] <= first and
                      first + count <= p['firstIndex'] + p['indexCount']), None)
        if owner:
            require(first == owner['firstIndex'] and count == owner['indexCount'],
                    'Moving component must retain its complete mechanical range')
            owner['parent'] = -1
            parts.append(owner)
        else:
            parts.append(dict(name=component['name'], firstIndex=first,
                              indexCount=count, parent=-1, motion='static'))
    end = 0
    for part in parts:
        require(part['firstIndex'] == end and part['indexCount'] > 0 and
                part['indexCount'] % 3 == 0, 'Invalid truck component partition')
        end += part['indexCount']
    require(len({p['name'] for p in parts}) == len(parts), 'Duplicate truck component')
    require(end == sum(len(p['indices']) for p in geometry['primitives']),
            'Incomplete truck component partition')
    return parts


def compare(aid, before, after, definition):
    if aid.endswith('.collision'):
        return compare_geometry(before, after)
    require(any(aid == 'unit.directorate.' + family + suffix
                for family in FAMILIES for suffix in SUFFIXES), 'Unknown truck asset')
    rest = copy.deepcopy(before)
    maximum = 0.0
    for old, new in zip(rest['primitives'], after['primitives']):
        original, positions = old['attributes']['POSITION'], new['attributes']['POSITION']
        require(len(original) == len(positions), 'Truck vertex count changed')
        low = [min(v[i] for v in original) for i in range(3)]
        high = [max(v[i] for v in original) for i in range(3)]
        for a, b in zip(original, positions):
            require(all(math.isfinite(x) for x in b), 'Non-finite truck vertex')
            require(all(low[i] - 1e-5 <= b[i] <= high[i] + 1e-5 for i in range(3)),
                    'Truck silhouette envelope exceeded')
            delta = max(abs(x-y) for x, y in zip(a, b))
            require(delta <= .005, 'Truck edit exceeds bounded rest-surface allowance')
            maximum = max(maximum, delta)
        for part in definition.get('mechanicalParts', []):
            if part['motion'] == 'static':
                continue
            for index in new['indices'][part['firstIndex']:part['firstIndex'] + part['indexCount']]:
                require(max(abs(x-y) for x, y in zip(original[index], positions[index])) <= 1e-5,
                        'Truck wheel geometry is locked to its mechanical anchor')
        for offset in range(0, len(new['indices']), 3):
            a, b, c = [positions[i] for i in new['indices'][offset:offset+3]]
            u, v = ([b[i]-a[i] for i in range(3)], [c[i]-a[i] for i in range(3)])
            cross = (u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0])
            length = math.sqrt(sum(x*x for x in cross))
            require(length > 1e-9, 'Degenerate truck face')
            normal = new['attributes']['NORMAL'][new['indices'][offset]]
            require(sum(x*y for x, y in zip(cross, normal))/length > .999,
                    'Truck edit invalidates explicit surface normals')
        old['attributes']['POSITION'] = positions
    result = compare_geometry(rest, after)
    result['maximumAuthoredPositionEdit'] = maximum
    return result
