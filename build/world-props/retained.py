"""Bind qualified prop exports and retain editable sources with immutable lineage."""
import json
import shutil
import sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from prop_contract import PropCatalog
from pipe_contract import align_straight_pipe
from asset_contract import Catalog, compare_geometry, fixture_selection, read_geometry, require, sha, write_json
FIXTURE = Path(__file__).with_name('fixture.json')
ORIGINAL = Path(__file__).with_name('original-bindings.json')
TARGET = ROOT / 'assets/staged/world-props'
FAMILIES = json.loads(FIXTURE.read_text(encoding='utf-8'))['families']

def qualified(output):
    q = json.loads((output / 'qualification.json').read_text(encoding='utf-8-sig'))
    require(all(q.get(k) is True for k in ('success', 'deterministicSourceAndRuntime',
        'sourceLessIntegrity', 'typedCompiledComparison', 'canonicalSourcesUnchanged')), 'Native qualification incomplete')
    return q

def bind(output, pipe_axis):
    qualified(output)
    axis = qualified(pipe_axis)
    require(axis.get('gameRotation') == 'positive-Y-90-degrees' and axis.get('boundsAndTopologyUnchanged'), 'Incomplete qualified pipe correction')
    catalog = PropCatalog(ROOT / 'assets/source')
    pending = []
    for family in FAMILIES:
        root = family['root']; name = root.removeprefix('mesh.world.prop.')
        ids, _ = fixture_selection(catalog, FIXTURE, [root])
        first = output / name / 'first'
        report = json.loads((first / 'roundtrip-report.json').read_text(encoding='utf-8'))
        require(report['sourceSignature'] == catalog.signature(ids)[0], 'Canonical source drift')
        is_pipe = name == 'pipe_section'
        if is_pipe:
            require(axis['sourceSignature'] == catalog.signature(ids)[0], 'Pipe baseline changed')
        exports = Catalog((pipe_axis if is_pipe else first.parent) / 'first/source')
        for aid in family['assets']:
            path, definition = catalog.definitions[aid]
            target = ROOT / 'assets/source/world/meshes/sources/blender/props' / name / (aid + '.glb')
            require(not target.exists(), 'Never overwrite an existing production export')
            expected = read_geometry(catalog.source(aid), catalog.root, definition)
            if is_pipe:
                expected = align_straight_pipe(expected)
            compare_geometry(expected, read_geometry(exports.source(aid), exports.root, definition))
            updated = dict(definition, source='../sources/blender/props/' + name + '/' + aid + '.glb')
            pending.append((path, updated, exports.source(aid), target))
    for path, definition, source, target in pending:
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
        write_json(path, definition)
    (ROOT / 'assets/source/world/meshes/sources/blender/props/.gitattributes').write_text('*.glb binary\n', encoding='utf-8')
    print('Bound 44 qualified High/Reduced GLBs; all other metadata retained')

def retain(output):
    q = qualified(output)
    require(not TARGET.exists(), 'Retained master directories are never overwritten')
    catalog = PropCatalog(ROOT / 'assets/source')
    for family in FAMILIES:
        root = family['root']; name = root.removeprefix('mesh.world.prop.')
        ids, _ = fixture_selection(catalog, FIXTURE, [root])
        first = output / name / 'first'
        report = json.loads((first / 'roundtrip-report.json').read_text(encoding='utf-8'))
        require(report['sourceSignature'] == catalog.signature(ids)[0], 'Bound source drift')
        exports = Catalog(first / 'source')
        target = TARGET / name; target.mkdir(parents=True, exist_ok=False)
        masters = []
        for source, filename in [('authoring.blend', name + '.blend'), ('inspection.blend', name + '_inspection.blend')]:
            shutil.copyfile(first / source, target / filename)
            masters.append({'path': filename, 'sha256': sha(target / filename)})
        bindings = []
        for aid in family['assets']:
            require(sha(catalog.source(aid)) == sha(exports.source(aid)), 'Native refresh changed the bound GLB')
            bindings.append({'id': aid, 'sha256': sha(catalog.source(aid)),
                'definition': catalog.definitions[aid][0].relative_to(catalog.root).as_posix()})
        write_json(target / 'lineage.json', {'schemaVersion': 1, 'status': 'review-bound-not-accepted',
            'sourceSha': q['sourceSha'], 'sourceSignature': catalog.signature(ids)[0], 'signaturePolicy': 'lf-json-v1',
            'fixtureSha256': sha(FIXTURE), 'blenderVersion': '5.2.2', 'masters': masters, 'exports': bindings,
            'humanArtApproval': 'pending', 'physicalPerformance': 'pending'})

    (TARGET / '.gitattributes').write_text('*.blend binary\n*.json text eol=lf\n', encoding='utf-8')

def validate():
    catalog = PropCatalog(ROOT / 'assets/source')
    expected = json.loads(ORIGINAL.read_text(encoding='utf-8'))['bindings']
    require(len(FAMILIES) == 22 and len(expected) == 44, 'Scoped prop roster changed')
    require({aid for aid in catalog.definitions if aid.startswith('mesh.world.prop.')} ==
        {aid for family in FAMILIES for aid in family['assets']}, 'Uncovered prop added to the canonical roster')
    for entry in expected:
        original = dict(entry['definition']); current = dict(catalog.definitions[original['id']][1])
        original.pop('source'); current.pop('source')
        require(original == current, 'Metadata, material, LOD or collision reference changed')
    for family in FAMILIES:
        name = family['root'].removeprefix('mesh.world.prop.')
        ids, _ = fixture_selection(catalog, FIXTURE, [family['root']])
        target = TARGET / name
        lineage = json.loads((target / 'lineage.json').read_text(encoding='utf-8'))
        require(lineage['status'] == 'review-bound-not-accepted', 'Unreviewed acceptance claim')
        require(lineage['sourceSignature'] == catalog.signature(ids)[0] and lineage['fixtureSha256'] == sha(FIXTURE), 'Source contract changed')
        require(len(lineage['masters']) == 2 and {m['path'] for m in lineage['masters']} == {name + '.blend', name + '_inspection.blend'}, 'Missing native masters')
        for master in lineage['masters']:
            require(sha(target / master['path']) == master['sha256'], 'Native master bytes changed')
        require(len(lineage['exports']) == 2 and {e['id'] for e in lineage['exports']} == set(family['assets']), 'Missing High/Reduced exports')
        for entry in lineage['exports']:
            require(catalog.definitions[entry['id']][1]['source'] == '../sources/blender/props/' + name + '/' + entry['id'] + '.glb', 'Binding changed')
            require(sha(catalog.source(entry['id'])) == entry['sha256'], 'Export changed')
    print('22 native rest/inspection master pairs and 44 bound High/Reduced GLBs match current source contracts')

if __name__ == '__main__':
    if len(sys.argv) == 5 and sys.argv[1] == '--bind' and sys.argv[3] == '--pipe-axis':
        bind(Path(sys.argv[2]).resolve(), Path(sys.argv[4]).resolve())
    elif len(sys.argv) == 3 and sys.argv[1] == '--retain':
        retain(Path(sys.argv[2]).resolve()); validate()
    else:
        require(len(sys.argv) == 1, 'Use --bind ROOT --pipe-axis ROOT, --retain ROOT, or no arguments')
        validate()
