"""Bounded edits for the saved Directorate tank rest project; no runtime code."""
import copy,json,math
from asset_contract import compare_geometry,require

ROOT='unit.directorate.main_battle_tank'
LIMITS={'':5528,'.turret':1736,'.lod1':892,'.lod2':244,'.damaged':88,'.critical':180,'.wreck':300}

def metadata(catalog,ids):
    return {aid:{k:v for k,v in catalog.definitions[aid][1].items() if k!='source'} for aid in ids}

def compare(aid,before,after,definition):
    if aid.endswith('.collision'):return compare_geometry(before,after)
    require(aid.startswith(ROOT),'Edit outside tank family')
    limit=LIMITS[aid[len(ROOT):]]
    require(sum(len(p['indices'])//3 for p in after['primitives'])<=limit,'Tank triangle budget exceeded')
    rest=copy.deepcopy(before);maximum=0
    # Topology, UV, explicit normals, material slots and rigid ranges stay fixed.
    for old,new in zip(rest['primitives'],after['primitives']):
        positions=new['attributes']['POSITION'];original=old['attributes']['POSITION']
        require(len(positions)==len(original),'Tank vertex count changed')
        for a,b in zip(original,positions):
            require(all(math.isfinite(x) for x in b),'Non-finite tank vertex')
            require(-.49001<=b[0]<=.49001 and -.50001<=b[1]<=.29001 and -.43001<=b[2]<=.64001,'Tank silhouette bounds exceeded')
            maximum=max(maximum,max(abs(x-y) for x,y in zip(a,b)))
        old['attributes']['POSITION']=positions
        for part in definition.get('mechanicalParts',[]):
            if part['motion']=='static':continue
            for index in new['indices'][part['firstIndex']:part['firstIndex']+part['indexCount']]:
                require(max(abs(x-y) for x,y in zip(original[index],positions[index]))<=1e-5,'Moving part geometry is locked to its mechanical anchor')
        for i in range(0,len(new['indices']),3):
            a,b,c=[positions[j] for j in new['indices'][i:i+3]]
            u=[b[j]-a[j] for j in range(3)];v=[c[j]-a[j] for j in range(3)]
            n=[u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]]
            require(sum(x*x for x in n)>1e-18,'Degenerate tank face')
            normal=new['attributes']['NORMAL'][new['indices'][i]]
            require(sum(x*y for x,y in zip(n,normal))/math.sqrt(sum(x*x for x in n))>.9999,'Tank face no longer matches its explicit normal')
    result=compare_geometry(rest,after);result['maximumAuthoredPositionEdit']=maximum
    return result
