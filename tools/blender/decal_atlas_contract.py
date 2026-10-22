"""Portable read-only validation of original straight-alpha decal bake outputs."""
import hashlib
from pathlib import Path
import struct
import zlib
from asset_contract import read_geometry

FAMILIES=('tire_tracks','tracked_vehicle_marks','road_wear','oil_stain','blast_mark','shell_impact','scorch_mark','concrete_crack')


def read_png(path):
    raw=Path(path).read_bytes();assert raw[:8]==b'\x89PNG\r\n\x1a\n'
    cursor=8;compressed=bytearray();width=height=0
    while cursor<len(raw):
        length=struct.unpack_from('>I',raw,cursor)[0];kind=raw[cursor+4:cursor+8];data=raw[cursor+8:cursor+8+length]
        assert zlib.crc32(kind+data)&0xffffffff==struct.unpack_from('>I',raw,cursor+8+length)[0]
        if kind==b'IHDR':
            width,height,depth,color,compression,filtering,interlace=struct.unpack('>IIBBBBB',data)
            assert (depth,color,compression,filtering,interlace)==(8,6,0,0,0),(depth,color,interlace)
        if kind==b'IDAT':compressed.extend(data)
        cursor+=length+12
    source=zlib.decompress(compressed);stride=width*4;result=bytearray();previous=bytearray(stride);offset=0
    def paeth(a,b,c):
        p=a+b-c;pa,pb,pc=abs(p-a),abs(p-b),abs(p-c)
        return a if pa<=pb and pa<=pc else b if pb<=pc else c
    for _ in range(height):
        kind=source[offset];row=bytearray(source[offset+1:offset+1+stride]);offset+=stride+1
        assert kind<=4
        for i in range(stride):
            a=row[i-4] if i>=4 else 0;b=previous[i];c=previous[i-4] if i>=4 else 0
            row[i]=(row[i]+(0 if kind==0 else a if kind==1 else b if kind==2 else (a+b)//2 if kind==3 else paeth(a,b,c)))&255
        result.extend(row);previous=row
    assert offset==len(source)
    return width,height,result


def inspect(path):
    width,height,pixels=read_png(path);result=[]
    assert width%4==0 and height%2==0
    for index,family in enumerate(FAMILIES):
        x0=index%4*(width//4);y0=index//4*(height//2);values=[];opaque=[];rgb=[]
        for y in range(y0,y0+height//2):
            for x in range(x0,x0+width//4):
                k=(y*width+x)*4;alpha=pixels[k+3];values.append(alpha)
                if alpha>0:opaque.append((x-x0,y-y0))
                rgb.extend(pixels[k:k+3])
        result.append(dict(family=family,alphaRange=[min(values),max(values)],nonzeroAlpha=len(opaque),
            bounds=None if not opaque else [min(p[0] for p in opaque),min(p[1] for p in opaque),max(p[0] for p in opaque),max(p[1] for p in opaque)],
            rgbRange=[min(rgb),max(rgb)],alphaSha256=hashlib.sha256(bytes(values)).hexdigest()))
    return dict(width=width,height=height,families=result)


def validate_atlas(path):
    result=inspect(path)
    assert (result['width'],result['height'])==(1024,1024),'Source atlas dimensions changed'
    hashes=[]
    for family in result['families']:
        assert family['alphaRange'][0]==0 and family['alphaRange'][1]>=64,family
        assert 1000<family['nonzeroAlpha']<120000,family
        left,top,right,bottom=family['bounds']
        assert left>=19 and right<=236 and top>=19 and bottom<=492,('Unsafe atlas inset',family)
        hashes.append(family['alphaSha256'])
    assert len(set(hashes))==8,'Decal family footprints must remain distinct'
    return result


def validate_geometry(original, candidate):
    assert len(original['primitives'])==len(candidate['primitives'])==1
    a,b=original['primitives'][0],candidate['primitives'][0]
    assert a['indices']==b['indices'] and a['materialSlot']==b['materialSlot']==-1
    for key in ('POSITION','NORMAL','TEXCOORD_0'):
        assert len(a['attributes'][key])==len(b['attributes'][key])==4
        assert max(abs(x-y) for p,q in zip(a['attributes'][key],b['attributes'][key]) for x,y in zip(p,q))<1e-6,key
    p=b['attributes']['POSITION'];uv=b['attributes']['TEXCOORD_0'];a0,a1,a2=b['indices'][:3]
    e1=[q-r for q,r in zip(p[a1],p[a0])];e2=[q-r for q,r in zip(p[a2],p[a0])]
    du1,dv1=uv[a1][0]-uv[a0][0],uv[a1][1]-uv[a0][1];du2,dv2=uv[a2][0]-uv[a0][0],uv[a2][1]-uv[a0][1]
    den=du1*dv2-du2*dv1
    expected_t=[(x*dv2-y*dv1)/den for x,y in zip(e1,e2)]
    expected_b=[(y*du1-x*du2)/den for x,y in zip(e1,e2)]
    for n,t in zip(b['attributes']['NORMAL'],b['attributes']['TANGENT']):
        assert abs(sum(x*x for x in t[:3])-1)<1e-6 and abs(sum(x*y for x,y in zip(n,t)))<1e-6
        bitangent=[(n[1]*t[2]-n[2]*t[1])*t[3],(n[2]*t[0]-n[0]*t[2])*t[3],(n[0]*t[1]-n[1]*t[0])*t[3]]
        assert sum(x*y for x,y in zip(t,expected_t))>0 and sum(x*y for x,y in zip(bitangent,expected_b))>0,'Reflected tangent basis'


def validate_carrier(source):
    root=Path(source);definition={'materialReferences':[]}
    original=read_geometry(root/'world/meshes/sources/world_quad.gltf',root,definition)
    candidate=read_geometry(root/'world/authoring/decals/decal_quad.glb',root,definition)
    validate_geometry(original,candidate)
    return original,candidate


if __name__=='__main__':
    import argparse,json
    from asset_contract import Catalog
    parser=argparse.ArgumentParser();parser.add_argument('png',nargs='?');parser.add_argument('--source',type=Path);parser.add_argument('--scope',type=Path);parser.add_argument('--rebakes',type=Path)
    args=parser.parse_args()
    if args.source:
        result=validate_atlas(args.source/'world/textures/decals/decal_atlas.png');validate_carrier(args.source)
        if args.scope:
            assert not args.scope.exists()
            catalog=Catalog(args.source);ids=catalog.closure(['mesh.world.decal.quad',*['material.world.decal.'+family for family in FAMILIES]])
            catalog.validate(ids);catalog.copy_to(ids,args.scope);result['scopedAssetIds']=ids
        if args.rebakes:
            checks=[]
            for channel in ('base','normal','orm'):
                name='decal_atlas'+('' if channel=='base' else '_'+channel)+'.png'
                width,height,canonical=read_png(args.source/'world/textures/decals'/name)
                for index,family in enumerate(FAMILIES):
                    w,h,baked=read_png(args.rebakes/family/(family+'-'+channel+'.png'));assert (w,h)==(width,height)
                    for y in range(index//4*(height//2),(index//4+1)*(height//2)):
                        start=(y*width+index%4*(width//4))*4;end=start+width
                        assert canonical[start:end]==baked[start:end],('Saved master bake differs from canonical atlas',family,channel,y)
                    checks.append(dict(family=family,channel=channel,exactPixels=True))
            result['nativeRebakeChecks']=checks
    else:result=inspect(args.png)
    print(json.dumps(result,indent=2))
