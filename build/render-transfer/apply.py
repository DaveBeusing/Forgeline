"""One-time source-pinned transfer; no program, test or benchmark execution."""
from pathlib import Path
import hashlib
import json
import subprocess

ALLOWED = {
    'src/ForgeLine.Client/ClientApplication.cs',
    'src/ForgeLine.Client/ClientRenderHost.cs',
    'src/ForgeLine.Client/ClientRenderPublication.cs',
    'src/ForgeLine.Presentation/RenderAssetSession.cs',
    'src/ForgeLine.Presentation/SimpleInstanceRenderer.PreparationReuse.cs',
}

def git(*args):
    return subprocess.check_output(['git', *args])

def blob(data):
    return hashlib.sha1(f'blob {len(data)}\0'.encode() + data).hexdigest()

def main():
    if git('status', '--porcelain').strip():
        raise RuntimeError('Refuse to modify a dirty checkout.')
    changes = json.loads(Path('build/render-transfer/edits.json').read_text(encoding='utf-8'))
    if len(changes) != len(ALLOWED) or {c['path'] for c in changes} != ALLOWED:
        raise RuntimeError('Unexpected source transfer scope.')
    prepared = []
    for change in changes:
        path = Path(change['path'])
        if change['before'] is None:
            if path.exists():
                raise RuntimeError(f'New source already exists: {path}')
            original = b''
        else:
            original = git('show', 'HEAD:' + change['path'])
            if blob(original) != change['before']:
                raise RuntimeError(f'Source revision changed: {path}')
        lines = original.decode('utf-8').splitlines(keepends=True)
        last_end = 0
        for start, end, replacement in change['edits']:
            if not 0 <= last_end <= start <= end <= len(lines):
                raise RuntimeError(f'Invalid edit ranges: {path}')
            last_end = end
        for start, end, replacement in reversed(change['edits']):
            lines[start:end] = replacement.splitlines(keepends=True)
        result = ''.join(lines).encode('utf-8')
        if blob(result) != change['after']:
            raise RuntimeError(f'Result differs from reviewed source: {path}')
        prepared.append((path, result))
    for path, result in prepared:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(result)
    output = Path('artifacts/render-transfer')
    output.mkdir(parents=True, exist_ok=True)
    report = {'checkout': git('rev-parse', 'HEAD').decode().strip(),
              'validation': 'Source identity only; Release compilation follows. No runtime acceptance.',
              'sources': [{k: c[k] for k in ('path', 'before', 'after')} for c in changes]}
    (output / 'source-blobs.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))

if __name__ == '__main__':
    main()
