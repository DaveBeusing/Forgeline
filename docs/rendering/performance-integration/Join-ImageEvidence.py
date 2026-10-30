"""Reassemble the original image ZIP from hash-bound GitHub-size parts."""
import argparse,hashlib,json
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--manifest',type=Path,required=True)
p.add_argument('--output',type=Path,required=True)
a=p.parse_args();m=json.loads(a.manifest.read_bytes());h=hashlib.sha256();total=0
with a.output.open('xb') as out:
    for part in m['parts']:
        data=(a.manifest.parent/part['path']).read_bytes()
        if len(data)!=part['bytes'] or hashlib.sha256(data).hexdigest()!=part['sha256']:raise ValueError('Image part integrity failed')
        out.write(data);h.update(data);total+=len(data)
if total!=m['bytes'] or h.hexdigest()!=m['sha256']:raise ValueError('Combined archive integrity failed')
print(f'Verified original image archive: {total} bytes, SHA-256 {h.hexdigest()}')
