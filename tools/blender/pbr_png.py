"""Lossless PNG checks for authored RGB/RGBA8 masters; no runtime reader."""
import struct
import zlib
from pathlib import Path
from asset_contract import require


def read_png(path):
    data=Path(path).read_bytes();require(data[:8]==b'\x89PNG\r\n\x1a\n',f'{path}: invalid PNG')
    position=8;payload=bytearray();header=None
    while position<len(data):
        require(position+12<=len(data),f'{path}: truncated PNG')
        size=int.from_bytes(data[position:position+4],'big');kind=data[position+4:position+8];chunk=data[position+8:position+8+size]
        require(len(chunk)==size and position+size+12<=len(data),f'{path}: truncated chunk')
        crc=int.from_bytes(data[position+8+size:position+12+size],'big')
        require(zlib.crc32(kind+chunk)==crc,f'{path}: corrupt PNG checksum')
        if kind==b'IHDR':header=struct.unpack('>IIBBBBB',chunk)
        if kind==b'IDAT':payload.extend(chunk)
        position+=size+12
        if kind==b'IEND':break
    require(header is not None,f'{path}: missing header')
    width,height,depth,color,compression,filtering,interlace=header
    require(0<width<=4096 and 0<height<=4096 and depth==8 and color in (2,6) and compression==filtering==interlace==0,f'{path}: unsupported PNG layout')
    channels=3 if color==2 else 4;stride=width*channels
    decoder=zlib.decompressobj();raw=decoder.decompress(payload,(stride+1)*height+1)
    require(len(raw)==(stride+1)*height and decoder.eof,f'{path}: invalid pixel extent')
    rows=[];previous=bytearray(stride)
    for y in range(height):
        mode=raw[y*(stride+1)];row=bytearray(raw[y*(stride+1)+1:(y+1)*(stride+1)])
        require(mode in range(5),f'{path}: invalid row filter')
        if mode:
            for x in range(stride):
                a=row[x-channels] if x>=channels else 0;b=previous[x];c=previous[x-channels] if x>=channels else 0
                if mode==1:p=a
                elif mode==2:p=b
                elif mode==3:p=(a+b)//2
                else:
                    target=a+b-c;dist=(abs(target-a),abs(target-b),abs(target-c));p=(a,b,c)[dist.index(min(dist))]
                row[x]=(row[x]+p)&255
        rows.append(bytes(row));previous=row
    return width,height,channels,b''.join(rows)


def write_png(path,width,height,pixels):
    require(len(pixels)==width*height*4,'RGBA extent differs')
    def chunk(kind,content):return struct.pack('>I',len(content))+kind+content+struct.pack('>I',zlib.crc32(kind+content))
    rows=b''.join(b'\0'+pixels[y*width*4:(y+1)*width*4] for y in range(height))
    Path(path).write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(rows,6))+chunk(b'IEND',b''))


def inspect_png(path):
    """Check every chunk before using Blender's native image decoder for large maps."""
    data=Path(path).read_bytes();require(data[:8]==b'\x89PNG\r\n\x1a\n',f'{path}: invalid PNG')
    pos=8;header=None;ended=False;idat=False
    while pos+12<=len(data):
        size=int.from_bytes(data[pos:pos+4],'big');kind=data[pos+4:pos+8];chunk=data[pos+8:pos+8+size]
        require(pos+size+12<=len(data) and zlib.crc32(kind+chunk)==int.from_bytes(data[pos+size+8:pos+size+12],'big'),f'{path}: corrupt PNG chunk')
        if kind==b'IHDR':
            require(header is None and pos==8 and size==13,f'{path}: invalid header order');header=struct.unpack('>IIBBBBB',chunk)
        if kind==b'IDAT':idat=True
        pos+=size+12
        if kind==b'IEND':ended=True;break
    require(header is not None and ended and idat and pos==len(data),f'{path}: incomplete PNG')
    w,h,d,c,z,f,i=header
    require(0<w<=4096 and 0<h<=4096 and d==8 and c in (2,6) and z==f==i==0,f'{path}: unsupported PNG layout')
    return w,h,3 if c==2 else 4
