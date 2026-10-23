"""Open every tracked Blender master without executing embedded scripts or saving."""
import hashlib
import json
import math
from pathlib import Path
import subprocess
import sys

import bpy

root=Path(sys.argv[sys.argv.index('--')+1]).resolve()
output=root/'artifacts/asset-audit/blender-masters.json'
output.parent.mkdir(parents=True,exist_ok=True)
expected=json.loads((root/'tools/blender/toolchain.json').read_text())['version']
if '.'.join(map(str,bpy.app.version))!=expected:
    raise RuntimeError('Blender does not match the repository-pinned version')
paths=subprocess.check_output(['git','ls-files','-z','*.blend'],cwd=root).decode().split('\0')
report={'schema':1,'blenderVersion':bpy.app.version_string,'sourceCommit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip(),
        'readOnly':True,'nativeVisualAcceptance':False,'files':[]}
for relative in filter(None,paths):
    path=root/relative
    record={'path':relative,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'errors':[],'warnings':[]}
    try:
        bpy.ops.wm.open_mainfile(filepath=str(path),load_ui=False,use_scripts=False)
        vertices=0;polygons=0;zero_area=0
        for mesh in bpy.data.meshes:
            vertices+=len(mesh.vertices);polygons+=len(mesh.polygons)
            for vertex in mesh.vertices:
                if not all(math.isfinite(x) for x in vertex.co):
                    record['errors'].append('Nonfinite vertex in '+mesh.name);break
            clone=mesh.copy()
            if clone.validate(verbose=False,clean_customdata=False):
                record['warnings'].append('Mesh validation would change '+mesh.name)
            bpy.data.meshes.remove(clone)
            zero_area+=sum(p.area==0 for p in mesh.polygons)
        if zero_area:record['warnings'].append(str(zero_area)+' zero-area authoring polygons require purpose review')
        for obj in bpy.data.objects:
            if not all(math.isfinite(v) for row in obj.matrix_world for v in row):
                record['errors'].append('Nonfinite object transform: '+obj.name)
        missing=[]
        for image in bpy.data.images:
            if image.source!='FILE' or image.packed_file or not image.filepath:continue
            resolved=Path(bpy.path.abspath(image.filepath))
            if not resolved.is_file():missing.append(image.filepath)
        record.update(vertices=vertices,polygons=polygons,images=len(bpy.data.images),unresolvedImagePaths=missing,status='opened')
    except Exception as error:
        record['status']='failed';record['errors'].append(type(error).__name__+': '+str(error))
    if hashlib.sha256(path.read_bytes()).hexdigest()!=record['sha256']:
        raise RuntimeError('Read-only inspection changed '+relative)
    report['files'].append(record)
    output.write_text(json.dumps(report,indent=2)+'\n')
    print(relative,record['status'],len(record['errors']),'errors',flush=True)
report['summary']={'masters':len(report['files']),'failed':sum(f['status']=='failed' for f in report['files']),
                   'errors':sum(len(f['errors']) for f in report['files']),
                   'warnings':sum(len(f['warnings']) for f in report['files']),
                   'unresolvedImageReferences':sum(len(f.get('unresolvedImagePaths',[])) for f in report['files'])}
output.write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report['summary']))
if report['summary']['errors']:raise RuntimeError('Native master inspection found structural errors')
