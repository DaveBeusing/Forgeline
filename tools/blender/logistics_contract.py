"""Family-local logistics preservation and physically bounded vertex editing."""
import copy
import math

from asset_contract import compare_geometry, geometry_stats, require

FAMILIES = ('storage_depot', 'supply_depot', 'logistics_hub')


def bindings(catalog, ids):
    result = {}
    for aid in ids:
        definition = copy.deepcopy(catalog.definitions[aid][1])
        definition.pop('source', None)
        result[aid] = definition
    return result


def compare(before, after, minimum, maximum):
    """Keep topology, UVs, normals, section count and all established bounds."""
    baseline = copy.deepcopy(before)
    require(geometry_stats(before)['triangles'] == geometry_stats(after)['triangles'],
            'Logistics topology changed')
    maximum_edit = 0.0
    for old, new in zip(baseline['primitives'], after['primitives']):
        positions = new['attributes']['POSITION']
        require(len(positions) == len(old['attributes']['POSITION']), 'Logistics topology changed')
        for original, point in zip(old['attributes']['POSITION'], positions):
            require(all(math.isfinite(v) for v in point), 'Nonfinite logistics position')
            require(all(minimum[i] - 1e-5 <= point[i] <= maximum[i] + 1e-5 for i in range(3)),
                    'Logistics geometry escapes retained bounds')
            maximum_edit = max(maximum_edit, max(abs(a-b) for a, b in zip(original, point)))
        for start in range(0, len(new['indices']), 3):
            a, b, c = (positions[i] for i in new['indices'][start:start+3])
            u, v = ([b[i]-a[i] for i in range(3)], [c[i]-a[i] for i in range(3)])
            normal = (u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0])
            length = math.sqrt(sum(x*x for x in normal))
            require(length > 1e-9, 'Degenerate logistics face')
            original_positions = old['attributes']['POSITION']
            oa, ob, oc = (original_positions[i] for i in old['indices'][start:start+3])
            ou, ov = ([ob[i]-oa[i] for i in range(3)], [oc[i]-oa[i] for i in range(3)])
            original_normal = (ou[1]*ov[2]-ou[2]*ov[1], ou[2]*ov[0]-ou[0]*ov[2], ou[0]*ov[1]-ou[1]*ov[0])
            original_length = math.sqrt(sum(x*x for x in original_normal))
            require(original_length > 1e-9, 'Degenerate baseline logistics face')
            expected = [v / original_length for v in original_normal]
            require(sum(x*y for x, y in zip(normal, expected))/length > .9999,
                    'Logistics edit no longer matches authored normals')
        old['attributes']['POSITION'] = positions
    result = compare_geometry(baseline, after)
    result['maximumAuthoredPositionEdit'] = maximum_edit
    return result
