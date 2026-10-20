"""Retain verifiable material sequence pixels with inter-frame XOR compression."""
from __future__ import annotations
import argparse
import gzip
import hashlib
import json
from pathlib import Path
import struct
import numpy as np

MAGIC=b'FLMQXOR1'


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def pack(root: Path, destination: Path) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    cohorts=[]
    for directory in sorted(root.glob('frequency-*-*-1')):
        frames=sorted(directory.glob('*.rgba.gz'))
        if not frames:
            continue
        hashes=[]
        previous=np.zeros(768*576*4,dtype=np.uint8)
        target=destination/(directory.name+'.rgba-xor.gz')
        if target.exists():
            raise FileExistsError(target)
        with target.open('wb') as output:
            with gzip.GzipFile(filename='',mode='wb',fileobj=output,mtime=0,compresslevel=9) as stream:
                stream.write(MAGIC+struct.pack('<III',768,576,len(frames)))
                for frame in frames:
                    raw=gzip.decompress(frame.read_bytes())
                    assert len(raw)==len(previous)
                    current=np.frombuffer(raw,dtype=np.uint8)
                    stream.write(np.bitwise_xor(current,previous).tobytes())
                    hashes.append(digest(raw));previous=current
        cohorts.append(dict(name=directory.name,file=target.name,width=768,height=576,frames=hashes,
                            packedSha256=digest(target.read_bytes())))
    index=dict(schemaVersion=1,encoding='gzip of FLMQXOR1 + little-endian width,height,frameCount (uint32), then fixed RGBA8 frames XOR previous decoded frame; initial previous all zero',
               scope='One of each exact repeated pair; report preserves hashes for both original cohorts.',cohorts=cohorts)
    (destination/'index.json').write_text(json.dumps(index,indent=2)+'\n',encoding='utf-8')
    verify(destination)


def verify(destination: Path) -> None:
    index=json.loads((destination/'index.json').read_text(encoding='utf-8'))
    for cohort in index['cohorts']:
        path=destination/cohort['file']
        assert digest(path.read_bytes())==cohort['packedSha256']
        with gzip.open(path,'rb') as stream:
            assert stream.read(8)==MAGIC
            width,height,count=struct.unpack('<III',stream.read(12))
            assert (width,height,count)==(cohort['width'],cohort['height'],len(cohort['frames']))
            previous=np.zeros(width*height*4,dtype=np.uint8)
            for expected in cohort['frames']:
                delta=np.frombuffer(stream.read(len(previous)),dtype=np.uint8)
                assert len(delta)==len(previous)
                previous=np.bitwise_xor(previous,delta)
                assert digest(previous.tobytes())==expected
            assert stream.read(1)==b''
    print(f'Verified {len(index["cohorts"])} lossless sequence packs.')


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('destination',type=Path)
    parser.add_argument('--source',type=Path)
    args=parser.parse_args()
    if args.source:
        pack(args.source,args.destination)
    else:
        verify(args.destination)
