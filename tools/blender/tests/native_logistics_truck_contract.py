"""Real saved-project rejection and positive component edit checks for both trucks."""
import json
import sys
from pathlib import Path
import bpy

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_contract import Catalog, compare_geometry, read_geometry, require, write_json
from logistics_truck import run as export_truck


def run(request):
    repository = Path(__file__).resolve().parents[3]
    output = Path(request['outputRoot'])
    fixture = repository / 'tools/blender/fixtures/logistics_trucks.json'
    catalog = Catalog(request['inputRoot'])
    reports = []
    for family in ('cargo_truck', 'supply_truck'):
        root = 'unit.directorate.' + family
        master = repository / 'assets/source/units/directorate/authoring' / (family + '.blend')
        rejected = []
        for case in ('socket', 'transform', 'modifier', 'action', 'untracked', 'material', 'wheel', 'silhouette', 'collision'):
            bpy.ops.wm.open_mainfile(filepath=str(master))
            obj = next(o for o in bpy.context.scene.objects if o.get('fl_role') == 'geometry' and
                       o.get('fl_asset') == root)
            if case == 'socket':
                next(o for o in bpy.context.scene.objects if o.get('fl_role') == 'socket').location.x += .01
            elif case == 'transform':
                obj.location.x += .01
            elif case == 'modifier':
                obj.modifiers.new('Unbaked bevel', 'BEVEL')
            elif case == 'action':
                obj.keyframe_insert(data_path='location', frame=1)
            elif case == 'untracked':
                bpy.context.scene.collection.objects.link(bpy.data.objects.new('Untracked', None))
            elif case == 'material':
                obj.data.materials[0].node_tree.nodes.clear()
            elif case == 'wheel':
                part = next(o for o in bpy.context.scene.objects if o.get('fl_role') == 'part' and
                            o.get('fl_asset') == root and o['fl_logical_name'].startswith('wheel_'))
                wheel = next(o for o in part.children if o.type == 'MESH')
                wheel.data.vertices[0].co.x += .001
            elif case == 'silhouette':
                obj.data.vertices[0].co.x = 2
            elif case == 'collision':
                collision = next(o for o in bpy.context.scene.objects if o.get('fl_role') == 'geometry' and
                                 o.get('fl_asset') == root + '.collision')
                collision.data.vertices[0].co.x += .001
            saved = output / (family + '-' + case + '.blend')
            bpy.ops.wm.save_as_mainfile(filepath=str(saved), check_existing=False)
            target = output / family / case
            target.mkdir(parents=True)
            try:
                export_truck(dict(inputRoot=request['inputRoot'], outputRoot=str(target), arguments=[
                    '--action', 'Export', '--family', family, '--fixtures', str(fixture), '--blend-file', str(saved)]))
            except ValueError as error:
                rejected.append(dict(case=case, error=str(error)))
            else:
                raise ValueError('Invalid truck master accepted: ' + family + '/' + case)
        bpy.ops.wm.open_mainfile(filepath=str(master))
        part = next(o for o in bpy.context.scene.objects if o.get('fl_role') == 'part' and
                    o.get('fl_asset') == root and o['fl_logical_name'] == 'armored_cab')
        for obj in part.children:
            for vertex in obj.data.vertices:
                vertex.co.x += .001
        saved = output / (family + '-edited.blend')
        bpy.ops.wm.save_as_mainfile(filepath=str(saved), check_existing=False)
        target = output / family / 'edited'
        target.mkdir(parents=True)
        export_truck(dict(inputRoot=request['inputRoot'], outputRoot=str(target), arguments=[
            '--action', 'Export', '--family', family, '--fixtures', str(fixture), '--blend-file', str(saved)]))
        candidate = Catalog(target / 'source')
        changed = []
        for suffix in ('', '.lod1', '.lod2', '.damaged', '.critical', '.wreck', '.collision'):
            aid = root + suffix
            before = read_geometry(catalog.source(aid), catalog.root, catalog.definitions[aid][1])
            after = read_geometry(candidate.source(aid), candidate.root, candidate.definitions[aid][1])
            try:
                compare_geometry(before, after)
            except ValueError:
                changed.append(aid)
        require(changed == [root], 'Saved component edit must change only its LOD0 asset')
        reports.append(dict(family=family, rejected=rejected, savedComponentEditChangesOnlyLod0=True))
    write_json(output / 'native-contracts.json', dict(success=True, families=reports))


if __name__ == '__main__':
    run(json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig')))
