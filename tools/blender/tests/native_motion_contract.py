"""Real Blender rest-pose editing and rejection cases."""
import json,sys
from pathlib import Path
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from asset_contract import Catalog,fixture_selection,read_geometry,write_json,require
from roundtrip import export_scene
from mechanical import author_definitions
request=json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig'))
blend,fixtures,profile=map(Path,request['arguments'])
catalog=Catalog(request['inputRoot']);ids,families=fixture_selection(catalog,fixtures,[])
geometries={a:read_geometry(catalog.source(a),catalog.root,catalog.definitions[a][1]) for a in ids if catalog.definitions[a][1]['type']=='mesh'}
profile=json.loads(profile.read_text(encoding='utf-8-sig'));passed=[]
def reset():bpy.ops.wm.open_mainfile(filepath=str(blend))
def wheel():return next(o for o in bpy.context.scene.objects if o.get('fl_motion')=='wheel')
def validate():
    export_scene(catalog,ids,families,geometries);author_definitions(catalog,geometries,profile)
def reject(name,edit):
    reset();edit()
    try:validate()
    except (ValueError,KeyError):passed.append(name);return
    raise AssertionError('Invalid rigid edit accepted: '+name)
reject('unknown motion',lambda:wheel().__setitem__('fl_motion','skeletal'))
reject('unbounded rate',lambda:wheel().__setitem__('fl_rate',21))
reject('unbounded pivot',lambda:wheel().__setitem__('fl_pivot_game',[10,0,0]))
reject('missing typed property',lambda:wheel().__delitem__('fl_rate'))
reject('unbaked frame transform',lambda:setattr(wheel(),'location',(10,0,0)))
reject('arbitrary actions',lambda:wheel().keyframe_insert(data_path='location',frame=1))
def move_cannon_pivot():
    obj=next(o for o in bpy.context.scene.objects if o.get('fl_motion')=='cannon');pivot=list(obj['fl_pivot_game']);pivot[0]+=.01;obj['fl_pivot_game']=pivot
def move_gameplay_socket():
    obj=next(o for o in bpy.context.scene.objects if o.get('fl_role')=='socket' and o.get('fl_logical_name')=='weapon_muzzle');obj.location.x+=.01
reject('gameplay-coupled cannon pivot',move_cannon_pivot)
reject('gameplay socket transform',move_gameplay_socket)
reset();o=wheel();o['fl_rate']=-1.75;p=list(o['fl_pivot_game']);p[0]+=.01;o['fl_pivot_game']=p
factory=next(o for o in bpy.context.scene.objects if o.get('fl_motion')=='door');factory['fl_travel']=.3
validate();bpy.ops.wm.save_as_mainfile(filepath=str(Path(request['outputRoot'])/'edited.blend'),check_existing=False)
write_json(Path(request['outputRoot'])/'native-motion-contracts.json',{'success':True,'rejected':passed,'editedRate':-1.75,'editedDoorTravel':.3,'editedPivotDeltaMetres':.01})
