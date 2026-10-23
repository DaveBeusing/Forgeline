"""One-time, exact-baseline migration; normal authoring remains the source owner."""
from pathlib import Path
import hashlib
import importlib
import json
import shutil
import subprocess
import sys
import zipfile

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'assets/source'
OUT=ROOT/'artifacts/asset-repair'
BASE='78fe033802595dd838713132527f161e9d3b7a17'
SMELTER='buildings/directorate/sources/industrial/smelter'
ARTILLERY='units/directorate/sources/combat_support/self_propelled_artillery'
GEOMETRY=[SMELTER+'/'+name for name in ('kit/exhaust.gltf','lod0.gltf','lod1.gltf')]+[ARTILLERY+'/'+name for name in ('kit/track_-1.gltf','kit/track_1.gltf','lod0.gltf','lod1.gltf','lod2.gltf')]


def run(*args):
    subprocess.run(args,cwd=ROOT,check=True)


def replace(path, old, new):
    p=ROOT/path
    text=p.read_text(encoding='utf-8')
    if text.count(old)!=1:
        raise ValueError('Expected one exact migration anchor: '+path)
    p.write_text(text.replace(old,new),encoding='utf-8')


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    # Refuse an unknown generator baseline instead of overwriting concurrent edits.
    for path,sha in [('build/Author-IndustrialBuildings.py','a57f199359d46b1f1be2f39a1c753542468cf5ef'),
                     ('build/Author-MainBattleTank.py','f8f2d748778073c4f10f1ccad72dda71b3e8a833')]:
        actual=subprocess.check_output(['git','hash-object',path],cwd=ROOT,text=True).strip()
        if actual!=sha:
            raise ValueError('Generator baseline changed: '+path)
    replace('build/asset-audit/source_audit.py','from PIL import Image\n','from PIL import Image\nfrom catalog_contracts import animation_stats\n')
    replace('build/asset-audit/source_audit.py',"('normal', 'orm', 'genericdata', 'terraincontrol')","('normal', 'orm', 'genericdata', 'terraincontrol', 'displayimage')")
    replace('build/asset-audit/source_audit.py','                stats, issues = mesh_stats(*load_mesh(path, root))',
        "                document, buffers = load_mesh(path, root)\n                if document.get('meshes'):\n                    stats, issues = mesh_stats(document, buffers)\n                    if document.get('animations'):\n                        animation_stats(document, buffers, accessor)\n                else:\n                    record['kind'] = 'animation'\n                    stats, issues = animation_stats(document, buffers, accessor)")
    sys.path.insert(0,str(ROOT/'build/asset-audit'))
    import source_audit as audit
    baseline=audit.audit(SOURCE)
    (OUT/'before.json').write_text(json.dumps(baseline,indent=2)+'\n')
    if baseline['summary']['errors'] or {x['path'] for x in baseline['findings']}!=set(GEOMETRY):
        raise ValueError('Source census differs from the eight reviewed geometry findings')
    # First establish that the retained exports can be reproduced by their owners.
    for script,family,folder in [('Author-IndustrialBuildings.py','smelter',SMELTER),('Author-CombatSupportUnits.py','self_propelled_artillery',ARTILLERY)]:
        dest=OUT/'baseline'/family
        run(sys.executable,'build/'+script,'--family',family,'--output',str(dest.parent))
        for path in dest.rglob('*.gltf'):
            original=SOURCE/folder/path.relative_to(dest)
            if original.read_bytes()!=path.read_bytes():
                raise ValueError('Original authoring is not byte-reproducible: '+str(original))
    import_block="\n_geometry_spec = importlib.util.spec_from_file_location('forgeline_source_geometry', Path(__file__).with_name('source_geometry.py'))\nsource_geometry = importlib.util.module_from_spec(_geometry_spec)\n_geometry_spec.loader.exec_module(source_geometry)\n"
    for script in ('build/Author-IndustrialBuildings.py','build/Author-MainBattleTank.py'):
        replace(script,'ROOT = Path(__file__).resolve().parents[1]\n','ROOT = Path(__file__).resolve().parents[1]\n'+import_block)
    replace('build/Author-IndustrialBuildings.py',
        "    for i, point in enumerate(points):\n        tangent = points[min(i+1,len(points)-1)] - points[max(0,i-1)]\n        tangent /= np.linalg.norm(tangent)\n        u = np.cross(tangent, reference)\n        if np.linalg.norm(u) < 1e-6:\n            u = np.cross(tangent, [1,0,0])\n        u /= np.linalg.norm(u)\n        v = np.cross(tangent,u)",
        "    frames = source_geometry.pipe_frames(points, reference)\n    for point, (u, v) in zip(points, frames):")
    replace('build/Author-MainBattleTank.py',
        '        inner=outer-np.sign(outer-centre)*np.asarray(inset)',
        '        inner=source_geometry.inset_profile(outer,inset)')
    changes=[]
    for script,family,folder in [('Author-IndustrialBuildings.py','smelter',SMELTER),('Author-CombatSupportUnits.py','self_propelled_artillery',ARTILLERY)]:
        dest=OUT/'corrected'/family
        run(sys.executable,'build/'+script,'--family',family,'--output',str(dest.parent))
        for path in dest.rglob('*.gltf'):
            relative=folder+'/'+path.relative_to(dest).as_posix()
            original=SOURCE/relative
            if original.read_bytes()==path.read_bytes():
                continue
            if relative not in GEOMETRY:
                raise ValueError('Unexpected geometry change: '+relative)
            old,oldissues=audit.mesh_stats(*audit.load_mesh(original,SOURCE))
            new,newissues=audit.mesh_stats(*audit.load_mesh(path,dest))
            for key in ('vertices','triangles','primitives','boundsMin','boundsMax'):
                if old[key]!=new[key]:
                    raise ValueError('Geometry contract changed: '+relative+' '+key)
            if newissues:
                raise ValueError('Repaired geometry still has findings: '+relative+repr(newissues))
            og=json.loads(original.read_text());ng=json.loads(path.read_text())
            if og.get('extras')!=ng.get('extras'):
                raise ValueError('Mechanical ranges/anchors changed: '+relative)
            changes.append({'path':'assets/source/'+relative,'beforeSha256':hashlib.sha256(original.read_bytes()).hexdigest(),
                            'afterSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'before':old,'after':new})
            shutil.copyfile(path,original)
        old_report=json.loads((SOURCE/folder/'exports.json').read_text())
        new_report=json.loads((dest/'exports.json').read_text())
        if old_report.get('bindings')!=new_report.get('bindings'):
            raise ValueError('Mechanical binding metadata changed')
        shutil.copyfile(dest/'exports.json',SOURCE/folder/'exports.json')
    if {x['path'].removeprefix('assets/source/') for x in changes}!=set(GEOMETRY):
        raise ValueError('Expected all eight repairs')
    run(sys.executable,'-m','unittest','discover','-s','build/asset-audit','-p','test_*.py','-v')
    after=audit.audit(SOURCE)
    (OUT/'after.json').write_text(json.dumps(after,indent=2)+'\n')
    if after['summary']['errors'] or after['summary']['warnings']:
        raise ValueError('Repaired source catalog has unresolved integrity findings')
    evidence={'schema':1,'baseline':BASE,'repairs':changes,'beforeSummary':baseline['summary'],'afterSummary':after['summary'],
              'texturesModified':False,'blendMastersOpened':False,'nativeAcceptance':False}
    (OUT/'repairs.json').write_text(json.dumps(evidence,indent=2)+'\n')
    allowed={'build/Author-IndustrialBuildings.py','build/Author-MainBattleTank.py','build/asset-audit/source_audit.py'}
    allowed.update('assets/source/'+p for p in GEOMETRY)
    allowed.update('assets/source/'+p+'/exports.json' for p in (SMELTER,ARTILLERY))
    changed=set(subprocess.check_output(['git','diff','--name-only'],cwd=ROOT,text=True).splitlines())
    if changed!=allowed:
        raise ValueError('Changed-file allowlist mismatch: '+repr(changed^allowed))
    (OUT/'allowed-paths.txt').write_text('\n'.join(sorted(allowed))+'\n')
    with zipfile.ZipFile(OUT/'review.zip','w',zipfile.ZIP_DEFLATED) as z:
        for path in sorted(ROOT.glob('build/*.py')):
            z.write(path,path.relative_to(ROOT))
        for path in sorted((ROOT/'build/asset-audit').glob('*.py')):
            z.write(path,path.relative_to(ROOT))
        for path in sorted(allowed):
            z.write(ROOT/path,path)
    print(json.dumps(evidence,indent=2))


if __name__=='__main__':
    main()
