"""Deterministic surface/LOD measurements; no Blender or player dependency."""
import copy
import math
from asset_contract import geometry_stats, require

VIEWS = ((45,55),(135,55),(225,55),(315,55),(0,0),(90,0),(0,90))


def validate_surface(geometry):
    for p in geometry['primitives']:
        a=p['attributes']
        require({'POSITION','NORMAL','TEXCOORD_0'}<=a.keys(),'Surface needs position, normal and UV channels')
        require(all(math.isfinite(v) for rows in a.values() for row in rows for v in row),'Nonfinite surface channel')
        require(all(abs(sum(v*v for v in n)-1)<.002 for n in a['NORMAL']),'Non-unit surface normal')
        for t in a.get('TANGENT',[]):
            require(abs(sum(v*v for v in t[:3])-1)<.002 and t[3] in (-1,1),'Invalid tangent frame')
        for i in range(0,len(p['indices']),3):
            uv=[a['TEXCOORD_0'][j] for j in p['indices'][i:i+3]]
            area=(uv[1][0]-uv[0][0])*(uv[2][1]-uv[0][1])-(uv[2][0]-uv[0][0])*(uv[1][1]-uv[0][1])
            require(abs(area)>1e-12,'Degenerate UV triangle')


def silhouette(geometry, reference, yaw, pitch, resolution=96):
    """Orthographic triangle coverage, reference framing shared by both meshes."""
    yaw,pitch=map(math.radians,(yaw,pitch))
    right=(math.cos(yaw),0,-math.sin(yaw))
    up=(-math.sin(yaw)*math.sin(pitch),math.cos(pitch),-math.cos(yaw)*math.sin(pitch))
    def project(p):return tuple(sum(p[i]*axis[i] for i in range(3)) for axis in (right,up))
    ref=[project(v) for p in reference['primitives'] for v in p['attributes']['POSITION']]
    lo=[min(v[i] for v in ref) for i in range(2)];hi=[max(v[i] for v in ref) for i in range(2)]
    span=max(hi[i]-lo[i] for i in range(2));require(span>0,'Empty silhouette bounds')
    center=[(hi[i]+lo[i])/2 for i in range(2)]
    def pixel(p):return tuple((v-center[i])/span*(resolution-6)+resolution/2 for i,v in enumerate(project(p)))
    mask=set()
    for p in geometry['primitives']:
        pts=[pixel(v) for v in p['attributes']['POSITION']]
        for i in range(0,len(p['indices']),3):
            a,b,c=[pts[j] for j in p['indices'][i:i+3]]
            area=(b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
            if abs(area)<1e-10:continue
            sign=1 if area>0 else -1
            for y in range(max(0,math.floor(min(a[1],b[1],c[1]))),min(resolution,math.ceil(max(a[1],b[1],c[1])))):
                for x in range(max(0,math.floor(min(a[0],b[0],c[0]))),min(resolution,math.ceil(max(a[0],b[0],c[0])))):
                    if all(sign*((v[0]-u[0])*(y+.5-u[1])-(v[1]-u[1])*(x+.5-u[0]))>=-1e-8 for u,v in ((a,b),(b,c),(c,a))):mask.add(y*resolution+x)
    return mask


def measure(reference,candidate):
    before,after=geometry_stats(reference),geometry_stats(candidate)
    extent=max(before['boundsMax'][i]-before['boundsMin'][i] for i in range(3))
    require(extent>0,'Empty geometry bounds')
    error=max(abs(before[k][i]-after[k][i])/extent for k in ('boundsMin','boundsMax') for i in range(3))
    overlap=[]
    for yaw,pitch in VIEWS:
        a=silhouette(reference,reference,yaw,pitch);b=silhouette(candidate,reference,yaw,pitch)
        overlap.append(len(a&b)/len(a|b) if a|b else 1.0)
    return {'baseline':before,'candidate':after,'relativeBoundsError':error,'silhouetteIou':overlap,'minimumSilhouetteIou':min(overlap)}


def chart_bounds(primitive):
    uv=primitive['attributes']['TEXCOORD_0'];parent={tuple(v):tuple(v) for v in uv}
    def root(v):
        while parent[v]!=v:
            parent[v]=parent[parent[v]];v=parent[v]
        return v
    for i in range(0,len(primitive['indices']),3):
        values=[tuple(uv[j]) for j in primitive['indices'][i:i+3]]
        for v in values[1:]:parent[root(v)]=root(values[0])
    groups={}
    for v in parent:groups.setdefault(root(v),[]).append(v)
    return [(tuple(min(v[i] for v in group) for i in range(2)),tuple(max(v[i] for v in group) for i in range(2))) for group in groups.values()]


def gate(reference,candidate,limits):
    require(set(limits)=={'maxTriangles','maxSections','maxGeometryBytes','minSilhouetteIou','maxRelativeBoundsError'},'Explicit geometry limits required')
    require(all(isinstance(v,(int,float)) and not isinstance(v,bool) and math.isfinite(v) for v in limits.values()),'Nonfinite geometry limits')
    require(limits['maxTriangles']>=1 and limits['maxSections']>=1 and 0<=limits['minSilhouetteIou']<=1 and 0<=limits['maxRelativeBoundsError']<=1,'Invalid geometry limits')
    require(limits['maxGeometryBytes']>=1,'Invalid geometry memory limit')
    validate_surface(candidate)
    result=measure(reference,candidate)
    result['estimatedCompiledGeometryBytes']=result['candidate']['vertices']*48+result['candidate']['triangles']*12
    require(result['estimatedCompiledGeometryBytes']<=limits['maxGeometryBytes'],'Geometry memory budget exceeded')
    require(result['candidate']['triangles']<=limits['maxTriangles'],'Triangle budget exceeded')
    require(result['candidate']['primitives']<=limits['maxSections'],'Draw section budget exceeded')
    require(result['minimumSilhouetteIou']>=limits['minSilhouetteIou'],'Protected silhouette threshold exceeded')
    require(result['relativeBoundsError']<=limits['maxRelativeBoundsError'],'Protected role-defining bounds exceeded')
    for old,new in zip(reference['primitives'],candidate['primitives']):
        require(old['materialSlot']==new['materialSlot'],'Ordered material slot changed')
        require(old['name']==new['name'],'Mesh identity changed')
        charts=chart_bounds(old)
        uv=new['attributes']['TEXCOORD_0']
        for start in range(0,len(new['indices']),3):
            triangle=[uv[i] for i in new['indices'][start:start+3]]
            require(any(all(lo[axis]-1e-6<=point[axis]<=hi[axis]+1e-6 for point in triangle for axis in range(2)) for lo,hi in charts),'Atlas chart boundary crossed')
        for axis in range(2):
            lo=min(v[axis] for v in old['attributes']['TEXCOORD_0']);hi=max(v[axis] for v in old['attributes']['TEXCOORD_0'])
            require(all(lo-1e-6<=v[axis]<=hi+1e-6 for v in new['attributes']['TEXCOORD_0']),'Atlas UV extent escaped')
    require(len(reference['primitives'])==len(candidate['primitives']),'Draw sections changed')
    return result


def texture_cost(width,height,format_name,mips):
    require(width>0 and height>0 and mips>0,'Invalid texture extent')
    require(format_name in ('BC7','Rgba8'),'Unsupported texture cost format')
    total=0
    for _ in range(mips):
        total+=((width+3)//4)*((height+3)//4)*16 if format_name=='BC7' else width*height*4
        width=max(1,width//2);height=max(1,height//2)
    return total


def retarget_geometry(target, generated):
    """Keep target LOD document/names/slots while replacing only vertex/index streams."""
    require(len(target['primitives'])==len(generated['primitives']),'Generated LOD section count differs')
    result=copy.deepcopy(target)
    for old,new in zip(result['primitives'],generated['primitives']):
        require(old['materialSlot']==new['materialSlot'],'Generated LOD material order differs')
        old['attributes']=copy.deepcopy(new['attributes']);old['indices']=list(new['indices'])
    return result
