"""Typed authoring rules for the existing 32-range rigid runtime metadata."""
import copy
import math
from asset_contract import require

KINDS=('static','turretYaw','cannon','wheel','fan','radar','door','crane','infantryStride')
AXES={'static':'none','turretYaw':'+Y','cannon':'-X / -Z recoil','wheel':'+X','fan':'+Y','radar':'+Y','door':'+Y translation','crane':'+Y','infantryStride':'+X'}


def validate_motion(definition,index_count):
    parts=definition.get('mechanicalParts',[]);sockets={x['name']:x for x in definition.get('sockets',[])}
    require(len(parts)<=32,'Rigid mesh exceeds 32 parts')
    cursor=0;names=set()
    for i,p in enumerate(parts):
        require(set(p)<= {'name','firstIndex','indexCount','parent','motion','socket','rate','travel'},'Unsupported rigid part field')
        require(isinstance(p.get('name'),str) and p['name'].strip() and p['name'] not in names,'Invalid/duplicate rigid part name');names.add(p['name'])
        require(type(p.get('firstIndex')) is int and p['firstIndex']==cursor and type(p.get('indexCount')) is int and p['indexCount']>0 and p['indexCount']%3==0,'Invalid rigid index partition')
        require(type(p.get('parent',-1)) is int and -1<=p.get('parent',-1)<i,'Invalid rigid hierarchy')
        require(p.get('motion','static') in KINDS,'Unsupported rigid motion kind')
        for key,limit,default in [('rate',20,1),('travel',2,0)]:
            v=p.get(key,default);require(type(v) in (int,float) and math.isfinite(v) and abs(v)<=limit,'Invalid bounded rigid '+key)
        require(p.get('motion','static')=='static' or p.get('socket') in sockets,'Moving part requires an existing socket')
        cursor+=p['indexCount']
    require(not parts or cursor==index_count,'Rigid parts must cover the complete static mesh')


def edited_definition(original,edits,max_pivot_delta,index_count):
    require(type(max_pivot_delta) in (int,float) and math.isfinite(max_pivot_delta) and max_pivot_delta>=0,'Explicit finite pivot delta limit required')
    updated=copy.deepcopy(original);parts=updated.get('mechanicalParts',[])
    require(set(edits)=={p['name'] for p in parts},'Authored part set changed')
    pivots={};original_sockets={s['name']:s for s in original.get('sockets',[])}
    for part in parts:
        edit=edits[part['name']]
        require(set(edit)=={'motion','rate','travel','pivot'},'Unknown/missing authored rigid property')
        for key,default in [('motion','static'),('rate',1),('travel',0)]:
            if edit[key]!=part.get(key,default):part[key]=edit[key]
        socket=next((s for s in updated.get('sockets',[]) if s['name']==part.get('socket')),None)
        old=[original_sockets[socket['name']][k] for k in 'xyz'] if socket else [0,0,0]
        pivot=edit['pivot'];require(len(pivot)==3 and all(type(x) in (float,int) and math.isfinite(x) for x in pivot),'Invalid authored pivot')
        delta=max(abs(a-b) for a,b in zip(pivot,old))
        if socket:
            canonical=list(pivot) if delta>1e-6 else old
            name=socket['name'];require(name not in pivots or max(abs(a-b) for a,b in zip(pivots[name],canonical))<=1e-6,'Shared socket has conflicting pivots');pivots[name]=canonical
        # Canonical source values survive float32 property storage unchanged.
        if delta>1e-6:
            require(socket and socket['name'].startswith('motion_') and next(p for p in original['mechanicalParts'] if p['name']==part['name']).get('motion','static') not in ('cannon','turretYaw'),'Gameplay/unbound sockets cannot be moved')
            require(delta<=max_pivot_delta,'Authored pivot exceeds reviewed delta limit')
            for key,value in zip('xyz',pivot):socket[key]=value
    validate_motion(updated,index_count)
    return updated
