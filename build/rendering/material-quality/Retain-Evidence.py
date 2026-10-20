"""Retain native material evidence with explicit hashes and redacted local roots."""
from __future__ import annotations
import argparse
import gzip
import hashlib
import json
from pathlib import Path
import re


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def main() -> None:
    parser=argparse.ArgumentParser()
    parser.add_argument('artifacts',type=Path)
    parser.add_argument('destination',type=Path)
    parser.add_argument('--pending-tests',action='store_true')
    args=parser.parse_args()
    root=args.artifacts.resolve();destination=args.destination.resolve()
    objects=destination/'objects';objects.mkdir(parents=True,exist_ok=True)
    selected=set(root.glob('*.log'))|set(root.glob('*.json'))
    for name in ('native-final','native-qualified','native-pairs','native-isolated','world-isolated'):
        directory=root/name
        if directory.exists():
            selected.update(p for p in directory.rglob('*') if p.is_file() and p.suffix in ('.json','.jsonl','.csv','.log','.png'))
    for name in ('sequences','sequences-conservative'):
        directory=root/name
        selected.update(directory.glob('*.json'));selected.update(directory.glob('*.png'))
    for name in ('world-baseline-final','world-candidate-final'):
        path=root/name/'build.log'
        if path.exists():selected.add(path)
    if args.pending_tests:
        selected.discard(root/'final-solution-tests.log')
    rows=[]
    for path in sorted(selected):
        raw=path.read_bytes();retained=raw
        if path.suffix!='.png':
            text=raw.decode('utf-8-sig')
            for old in ('D:\\\\Code\\\\Forgeline','D:\\Code\\Forgeline','D:/Code/Forgeline'):
                text=text.replace(old,'<repository>')
            text=re.sub(r'C:(?:\\\\|\\|/)Users(?:\\\\|\\|/)[^\\/\s"<>]+','<profile>',text,flags=re.IGNORECASE)
            retained=text.encode('utf-8')
        digest=sha(retained)
        suffix='.png' if path.suffix=='.png' else '.gz'
        target=objects/(digest+suffix)
        stored=retained if suffix=='.png' else gzip.compress(retained,compresslevel=9,mtime=0)
        if target.exists():assert target.read_bytes()==stored
        else:target.write_bytes(stored)
        rows.append(dict(artifact=path.relative_to(root).as_posix(),originalSha256=sha(raw),retainedSha256=digest,
                         storedSha256=sha(stored),object='objects/'+target.name,originalBytes=len(raw),retainedBytes=len(retained),
                         redacted=raw!=retained))
    index=dict(schemaVersion=1,method='Native PNG bytes preserved. UTF-8 logs/JSON remove BOM and replace local repository/profile roots only; original and retained hashes remain distinct. gzip mtime=0. Lossless raw sequence packs have their own verified indexes.',records=rows)
    (destination/'index.json').write_text(json.dumps(index,indent=2)+'\n',encoding='utf-8')
    for row in rows:
        stored=(destination/row['object']).read_bytes()
        assert sha(stored)==row['storedSha256']
        retained=stored if row['object'].endswith('.png') else gzip.decompress(stored)
        assert sha(retained)==row['retainedSha256']
    print(f'Retained and verified {len(rows)} material evidence records.')


if __name__=='__main__':
    main()
