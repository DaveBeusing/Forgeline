"""One-time migration of proven CRLF-derived source export checksums."""
import ast
from collections import defaultdict
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'assets/source'
OUTPUT = ROOT / 'artifacts/asset-repair'
AUTHORS = ('CommandCore', 'IndustrialBuildings', 'LogisticsBuildings', 'OperationalBuildings',
           'MainBattleTank', 'CombatSupportUnits', 'InfantryBattlefield', 'EnvironmentKit')


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    mapping = defaultdict(set)
    original_meshes = {}
    for path in SOURCE.rglob('*.gltf'):
        raw = path.read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        original_meshes[path] = digest
        mapping[digest].add(digest)
        legacy = raw.replace(b'\r\n', b'\n').replace(b'\n', b'\r\n')
        mapping[hashlib.sha256(legacy).hexdigest()].add(digest)
    changes, allowed = [], set()
    count = 0
    for path in sorted(SOURCE.rglob('*exports.json')):
        report = json.loads(path.read_text(encoding='utf-8'))
        dirty = False
        for key, entry in report.get('meshes', report).items():
            if not isinstance(entry, dict) or 'sha256' not in entry:
                continue
            count += 1
            old = entry['sha256']
            targets = mapping[old]
            if len(targets) != 1:
                raise ValueError(f'Checksum is not a demonstrated line-ending mismatch: {path}: {key}')
            actual = next(iter(targets))
            if actual != old:
                entry['sha256'] = actual
                changes.append(dict(path=path.relative_to(ROOT).as_posix(), key=key, before=old, after=actual))
                dirty = True
        if dirty:
            path.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n')
            allowed.add(path.relative_to(ROOT).as_posix())
    if count != 500 or len(changes) != 492:
        raise ValueError('Retained export report baseline has changed')
    writer_calls = 0
    for name in AUTHORS:
        path = ROOT / 'build' / f'Author-{name}.py'
        text = path.read_text(encoding='utf-8')
        offsets = [0]
        for line in text.splitlines(keepends=True):
            offsets.append(offsets[-1] + len(line))
        insertions = []
        for node in ast.walk(ast.parse(text)):
            if isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute) and node.func.attr == 'write_text':
                if any(k.arg == 'newline' for k in node.keywords):
                    raise ValueError(f'Authoring baseline already defines newline: {path}:{node.lineno}')
                insertions.append(offsets[node.end_lineno - 1] + node.end_col_offset - 1)
        if not insertions:
            raise ValueError('Missing authoring writer: ' + name)
        for offset in sorted(insertions, reverse=True):
            text = text[:offset] + ", newline='\\n'" + text[offset:]
        ast.parse(text)
        path.write_text(text, encoding='utf-8', newline='\n')
        writer_calls += len(insertions)
        allowed.add(path.relative_to(ROOT).as_posix())
    if original_meshes != {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in original_meshes}:
        raise ValueError('Checksum migration changed source mesh bytes')
    subprocess.run([sys.executable, '-m', 'unittest', 'discover', '-s', 'build/asset-audit', '-p', 'test_*.py', '-v'], cwd=ROOT, check=True)
    subprocess.run([sys.executable, 'build/asset-audit/source_audit.py', '--report', 'artifacts/asset-repair/after.json'], cwd=ROOT, check=True)
    evidence = dict(schema=1, checked=500, corrected=492, reportFiles=len(allowed) - len(AUTHORS),
                    authoringScripts=len(AUTHORS), explicitLfWriters=writer_calls, meshesModified=False,
                    texturesModified=False, changes=changes)
    (OUTPUT / 'export-checksums.json').write_text(json.dumps(evidence, indent=2) + '\n', encoding='utf-8')
    changed = set(subprocess.check_output(['git', 'diff', '--name-only'], cwd=ROOT, text=True).splitlines())
    if changed != allowed:
        raise ValueError('Unexpected changed paths: ' + repr(changed ^ allowed))
    (OUTPUT / 'allowed-paths.txt').write_text('\n'.join(sorted(allowed)) + '\n', encoding='utf-8')
    with zipfile.ZipFile(OUTPUT / 'review.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(allowed):
            archive.write(ROOT / path, path)
    print(json.dumps({key: value for key, value in evidence.items() if key != 'changes'}, sort_keys=True))


if __name__ == '__main__':
    main()
