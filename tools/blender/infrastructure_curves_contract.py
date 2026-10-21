"""Continuous bounded road ribbon recipes over the existing source module envelopes."""
import math
from asset_contract import require


def centreline(aid,stats):
    lo,hi=stats['boundsMin'],stats['boundsMax']
    half=.17
    if aid.endswith('curve_short'):
        diagonal=half/math.sqrt(2)
        start=(lo[0],lo[2]+half)
        end=(hi[0]-diagonal,hi[2]-diagonal)
        return [start,(end[0]-(end[1]-start[1]),start[1]),end]
    require(aid.endswith('curve_long'),'Only the two audited curve modules may be rebuilt')
    return [(lo[0],lo[2]+half),(-.28,lo[2]+half),(.03,.02),(.30,hi[2]-half),(hi[0],hi[2]-half)]


def outline(path,half=.17):
    normals=[]
    for a,b in zip(path,path[1:]):
        dx,dz=b[0]-a[0],b[1]-a[1];length=math.hypot(dx,dz)
        require(length>1e-6,'Degenerate curve segment')
        normals.append((-dz/length,dx/length))
    left=[];right=[]
    for i,p in enumerate(path):
        if i in (0,len(path)-1):offset=tuple(v*half for v in normals[0 if i==0 else -1])
        else:
            a,b=normals[i-1],normals[i];dot=1+a[0]*b[0]+a[1]*b[1]
            require(dot>.5,'Road miter exceeds the bounded join angle')
            offset=((a[0]+b[0])*half/dot,(a[1]+b[1])*half/dot)
        left.append((p[0]+offset[0],p[1]+offset[1]));right.append((p[0]-offset[0],p[1]-offset[1]))
    return left+list(reversed(right))
