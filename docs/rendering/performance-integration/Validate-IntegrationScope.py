"""Validate the explicit serial integration write set without changing historical lane scopes."""
import argparse
import json
from pathlib import Path
import subprocess

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--repository-root',type=Path,required=True)
p.add_argument('--base',default='origin/master')
args=p.parse_args()
root=args.repository_root.resolve()
manifest=json.loads((root/'docs/rendering/performance-integration/Ownership.json').read_bytes())
def git(*arguments):
    return subprocess.check_output(['git',*arguments],cwd=root).decode().splitlines()
paths=set(git('diff','--name-only','--no-renames',args.base))
paths.update(git('ls-files','--others','--exclude-standard'))
allowed=set(manifest['writeExact'])
prefixes=tuple(manifest['writePrefixes'])
outside=sorted(x for x in paths if x not in allowed and not x.startswith(prefixes))
if outside: raise SystemExit('Outside the serial integration scope: '+', '.join(outside))
frozen=git('diff','--name-only',args.base,'--','src/ForgeLine.Graphics/RenderContracts/',
           'src/ForgeLine.Presentation/RenderContracts/','src/ForgeLine.Graphics/IGraphicsDevice.cs',
           'src/ForgeLine.Graphics/IGraphicsCommandContext.cs','docs/rendering/performance-foundation/Ownership.json')
if frozen: raise SystemExit('Frozen contract changed: '+', '.join(frozen))
print(f'VALID: {len(paths)} changed paths belong to the explicit serial integration scope; frozen contracts remain unchanged.')
