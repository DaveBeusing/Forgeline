"""Archive original validation records without replacing failed attempts."""
import argparse,hashlib,json,zipfile
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--root',type=Path,required=True)
p.add_argument('--output',type=Path,required=True)
a=p.parse_args();root=a.root.resolve();out=a.output.resolve();out.mkdir(parents=True,exist_ok=False)
files={}
for name in ['baseline-restore.log','baseline-build.log','baseline-complete-build.log','candidate-build.log','recovery-build.log','particle-before-fix.log','particle-after-fix.log','baseline-fallback-failure.log','baseline-fallback-restoration.json','static-control-tests.log','native-surface-build.log','native-surface-test.log','client-exclusive-tests.log','solution-ci-equivalent.log','headless-smoke.json','headless-stress.json','match.json','match.progression-1.json','baseline-binaries.json','current-binaries.json','source-archive-verification.json','Invoke-Canonical.ps1']:
    file=root/name
    if file.exists():files[name]=file.read_bytes()
for file in sorted((root/'canonical').glob('*')):
    if file.is_file():files['canonical/'+file.name]=file.read_bytes()
records=[{'path':name,'bytes':len(raw),'sha256':hashlib.sha256(raw).hexdigest()} for name,raw in sorted(files.items())]
index={'schemaVersion':1,'policy':'Original attempts retained. The initial unbounded solution run failed two bounded waits; the separately retained full CI-equivalent serial run is the final solution gate. Debug/explicit optional skips remain skips. Canonical source is b232a6a7e3f06b9f10c46fe1c489d0c4a93e6b88; final serial source is 968e0db1683ff30e728e20560350ebd9ea0f0242, differing only in a native recovery test and CI-command documentation. Production code is identical.','records':records}
files['index.json']=(json.dumps(index,indent=2)+'\n').encode()
with zipfile.ZipFile(out/'validation-evidence.zip','w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
    for name,raw in sorted(files.items()):z.writestr(name,raw)
with zipfile.ZipFile(out/'validation-evidence.zip') as z:
    for r in records:
        assert hashlib.sha256(z.read(r['path'])).hexdigest()==r['sha256']
(out/'index.json').write_bytes(files['index.json'])
print(json.dumps({'records':len(records),'bytes':(out/'validation-evidence.zip').stat().st_size,'sha256':hashlib.sha256((out/'validation-evidence.zip').read_bytes()).hexdigest()}))
