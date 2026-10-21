"""Explicit tank armor-atlas migration; geometry and shared images remain owned."""
import copy
import math
from asset_contract import compare_geometry, require

ROOT = 'unit.directorate.main_battle_tank'
ASSETS = (ROOT, ROOT + '.turret', ROOT + '.lod1')


def refine(aid, geometry, definition):
    result = copy.deepcopy(geometry)
    count = 0
    if aid not in ASSETS:
        return result, count
    for primitive in result['primitives']:
        locked = set()
        for part in definition.get('mechanicalParts', []):
            if part['motion'] != 'static':
                locked.update(primitive['indices'][part['firstIndex']:part['firstIndex'] + part['indexCount']])
        for index, uv in enumerate(primitive['attributes']['TEXCOORD_0']):
            # Cell 8 is weathered armor. Cell 0 is the existing painted armor.
            # Preserve the chart, inset and texel density by translating one cell row.
            if math.floor(uv[0] * 4) == 0 and math.floor(uv[1] * 4) == 2:
                require(index not in locked, 'Armor migration cannot change moving geometry')
                uv[1] -= .5
                count += 1
    return result, count


def compare(aid, before, after, definition):
    expected, count = refine(aid, before, definition)
    deviations = compare_geometry(expected, after)
    deviations['repaintedVertices'] = count
    return deviations
