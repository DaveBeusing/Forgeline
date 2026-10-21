"""Original bounded shrub clusters and anchored grass leaves; no additional runtime data."""
import copy
import math


def refine(family, baseline, reduced):
    geometry = copy.deepcopy(baseline)
    if len(geometry['primitives']) != 1:
        raise ValueError('Review changed plant primitive layout explicitly')
    primitive = geometry['primitives'][0]
    old = primitive['attributes']
    if set(old) != {'POSITION', 'NORMAL', 'TEXCOORD_0'}:
        raise ValueError('Review changed plant vertex channels explicitly')
    lo = [min(p[i] for p in old['POSITION']) for i in range(3)]
    hi = [max(p[i] for p in old['POSITION']) for i in range(3)]
    uvlo = [min(p[i] for p in old['TEXCOORD_0']) for i in range(2)]
    uvhi = [max(p[i] for p in old['TEXCOORD_0']) for i in range(2)]
    triangles = []
    if family == 'scrub':
        clusters = [((0,.5,0),(.36,.5,.5)), ((-.22,.44,.02),(.28,.32,.37)), ((.22,.45,.04),(.28,.33,.37))]
        if not reduced:
            clusters += [((0,.4,.22),(.25,.24,.28)), ((0,.4,-.22),(.26,.24,.28))]
        for center, radius in clusters:
            points = [(center[0]+radius[0],center[1],center[2]), (center[0],center[1],center[2]+radius[2]),
                      (center[0]-radius[0],center[1],center[2]), (center[0],center[1],center[2]-radius[2]),
                      (center[0],center[1]+radius[1],center[2]), (center[0],center[1]-radius[1],center[2])]
            for side in range(4):
                triangles.append((points[(side+1)%4], points[side], points[4]))
                triangles.append((points[side], points[(side+1)%4], points[5]))
    elif family == 'grass_clump':
        # Three dominant leaves retain their roots/tips across LOD. High inserts
        # a bent side point, rather than doubling broad intersecting flat cards.
        for i, (x,z) in enumerate(((.5,0),(-.5,.5),(-.5,-.5))):
            height = (1,.85,.9)[i]
            length = math.hypot(x,z)
            tx,tz = -z/length*.035, x/length*.035
            a = (.62*x-tx,0,.62*z-tz)
            b = (.62*x+tx,0,.62*z+tz)
            tip = (x,height,z)
            faces = [(a,b,tip)]
            if not reduced:
                mid = (.45*b[0]+.55*x+tx*.35,height*.58,.45*b[2]+.55*z+tz*.35)
                faces = [(a,b,mid),(a,mid,tip)]
            for face in faces:
                triangles.extend((face,tuple(reversed(face))))
    else:
        raise ValueError('Unsupported vegetation refinement')
    points = [p for tri in triangles for p in tri]
    newlo = [min(p[i] for p in points) for i in range(3)]
    newhi = [max(p[i] for p in points) for i in range(3)]
    def fit(point):
        return tuple(lo[i]+(point[i]-newlo[i])/(newhi[i]-newlo[i])*(hi[i]-lo[i]) for i in range(3))
    positions, normals, uvs = [], [], []
    for tri in triangles:
        a,b,c = [fit(p) for p in tri]
        u,v = [b[i]-a[i] for i in range(3)], [c[i]-a[i] for i in range(3)]
        normal = (u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0])
        length = math.sqrt(sum(x*x for x in normal))
        if length < 1e-9:
            raise ValueError('Degenerate plant triangle')
        normal = tuple(x/length for x in normal)
        positions.extend((a,b,c))
        normals.extend((normal,)*3)
        uvs.extend(tuple(uvlo[i]+uv[i]*(uvhi[i]-uvlo[i]) for i in range(2)) for uv in ((0,1),(1,1),(.5,0)))
    if len(positions) != len(old['POSITION']) or primitive['indices'] != list(range(len(positions))):
        raise ValueError('Vegetation topology/budget changed')
    primitive['attributes'] = {'POSITION':positions,'NORMAL':normals,'TEXCOORD_0':uvs}
    return geometry
