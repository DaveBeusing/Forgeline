"""Build the bundled UI distance-field atlas; never invoked by a player."""
import argparse, gzip, hashlib, json, unicodedata
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont, __version__ as pillow_version
from fontTools.ttLib import TTFont
from scipy.ndimage import distance_transform_edt

ROOT = Path(__file__).resolve().parents[1]
FONTS = ROOT / 'assets/source/ui/fonts'
SIZE, SPREAD, WIDTH, HEIGHT = 48, 6, 2048, 4096
CODEPOINTS = sorted(set(range(32,127)) | set(range(160,384)) | set(range(0x370,0x500)) | set(map(ord,'\u2013\u2014\u2018\u2019\u201c\u201d\u2022\u2026\u2190\u2191\u2192\u2193\u21b5\u2713\ufffd')))

def build():
    atlas = np.zeros((HEIGHT, WIDTH), dtype=np.uint8)
    glyphs, kern, files = [], [], []
    x = y = row_height = 0
    for face, name in enumerate(('DejaVuSans.ttf','DejaVuSans-Bold.ttf')):
        path = FONTS / name
        tt = TTFont(path)
        cmap = tt.getBestCmap()
        upem = tt['head'].unitsPerEm
        font = ImageFont.truetype(str(path), SIZE, layout_engine=ImageFont.Layout.BASIC)
        files.append(dict(file=name,sha256=hashlib.sha256(path.read_bytes()).hexdigest(),version=tt['name'].getDebugName(5)))
        ids = {}
        for cp in CODEPOINTS:
            if cp not in cmap: continue
            character = chr(cp)
            left, top, right, bottom = font.getbbox(character, anchor='ls')
            advance = tt['hmtx'][cmap[cp]][0] * SIZE / upem
            ids[cmap[cp]] = cp
            if right == left or bottom == top or character.isspace():
                glyphs.append(dict(face=face,codepoint=cp,x=0,y=0,width=0,height=0,left=0,top=0,advance=round(advance,6)))
                continue
            w,h = right-left+2*SPREAD,bottom-top+2*SPREAD
            if x+w > WIDTH: x=0; y+=row_height+2; row_height=0
            if y+h > HEIGHT: raise RuntimeError('Atlas capacity exceeded')
            image=Image.new('L',(w,h)); ImageDraw.Draw(image).text((SPREAD-left,SPREAD-top),character,font=font,fill=255,anchor='ls')
            mask=np.asarray(image)>=128
            distance=distance_transform_edt(mask)-distance_transform_edt(~mask)
            field=np.clip(np.rint(128+distance*127/SPREAD),0,255).astype(np.uint8)
            atlas[y:y+h,x:x+w]=field
            glyphs.append(dict(face=face,codepoint=cp,x=x,y=y,width=w,height=h,left=left-SPREAD,top=top-SPREAD,advance=round(advance,6)))
            x+=w+2; row_height=max(row_height,h)
        if 'kern' in tt:
            pairs={}
            for table in tt['kern'].kernTables:
                for (a,b),value in table.kernTable.items():
                    if a in ids and b in ids and value: pairs[(ids[a],ids[b])]=value
            for (a,b),value in sorted(pairs.items()): kern.append(dict(face=face,left=a,right=b,adjustment=round(value*SIZE/upem,6)))
        tt.close()
    alpha=atlas.tobytes()
    metadata=dict(version=1,resourceId='font.ui.dejavu_sans',width=WIDTH,height=HEIGHT,pixelSize=SIZE,spread=SPREAD,
                  ascent=44.554688,lineHeight=55.875,alphaSha256=hashlib.sha256(alpha).hexdigest(),
                  compositions=[dict(base=ord(parts[0]),mark=ord(parts[1]),composed=cp) for cp in CODEPOINTS if len(parts:=unicodedata.normalize("NFD",chr(cp)))==2],sourceRevision='8d64f03a1f501ba0019279bf2f8db3930d1fe33f',pillow=pillow_version,sources=files,glyphs=glyphs,kerning=kern)
    rgba=np.full((HEIGHT,WIDTH,4),255,dtype=np.uint8);rgba[:,:,3]=atlas
    return alpha, metadata, Image.fromarray(rgba)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--verify',action='store_true');args=parser.parse_args()
    alpha, metadata, image=build()
    encoded=(json.dumps(metadata,ensure_ascii=False,sort_keys=True,separators=(',',':'))+'\n').encode('utf-8')
    compressed=gzip.compress(alpha,compresslevel=9,mtime=0)
    if args.verify:
        assert (FONTS/'atlas.alpha.gz').read_bytes()==compressed,'Atlas differs'
        assert (FONTS/'metrics.json').read_bytes()==encoded,'Metrics differ'
        assert Image.open(FONTS/'atlas.png').convert('RGBA').tobytes()==image.tobytes(),'PNG differs'
    else:
        (FONTS/'atlas.alpha.gz').write_bytes(compressed);(FONTS/'metrics.json').write_bytes(encoded)
        image.save(FONTS/'atlas.png',optimize=False,compress_level=9)
    print(f'{len(metadata["glyphs"])} glyphs, {len(metadata["kerning"])} kerning pairs; {len(compressed)} compressed bytes')
if __name__=='__main__': main()
