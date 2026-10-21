"""Exercise real saved-project edits and rejection without altering production sources."""
import argparse,json,sys
from pathlib import Path
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from tank import load_project,restore_normals,run as export_tank
from tank_contract import ROOT,compare
from asset_contract import Catalog,fixture_selection,read_geometry,write_json
from roundtrip import export_scene

def run(request):
    root=Path(__file__).resolve().parents[3];catalog=Catalog(request['inputRoot']);fixture=root/'tools/blender/fixtures/tank.json';ids,families=fixture_selection(catalog,fixture,[]);output=Path(request['outputRoot']);blend=root/'assets/source/units/directorate/authoring/main_battle_tank.blend'
    parser=argparse.ArgumentParser();parser.add_argument('--blend-file',type=Path,default=blend);args=parser.parse_args(request.get('arguments',[]));blend=args.blend_file.resolve()
    geometries={aid:read_geometry(catalog.source(aid),catalog.root,catalog.definitions[aid][1]) for aid in ids if catalog.definitions[aid][1]['type']=='mesh'}
    load_project(catalog,ids,families,blend);restore_normals()
    export_scene(catalog,ids,families,geometries,lambda aid,a,b:compare(aid,a,b,catalog.definitions[aid][1]))
    rejected=[]
    for case in ('socket','transform','modifier','action','untracked','material','silhouette','collision','cannon'):
        load_project(catalog,ids,families,blend)
        obj=next(o for o in bpy.context.scene.objects if o.get('fl_role')=='geometry' and o.get('fl_asset')==ROOT+'.damaged')
        if case=='socket':next(o for o in bpy.context.scene.objects if o.get('fl_role')=='socket').location.x+=.1
        elif case=='transform':obj.location.x=.01
        elif case=='modifier':obj.modifiers.new('Unbaked edit','BEVEL')
        elif case=='action':obj.keyframe_insert(data_path='location',frame=1)
        elif case=='untracked':bpy.context.scene.collection.objects.link(bpy.data.objects.new('Untracked',None))
        elif case=='material':obj.data.materials.clear()
        elif case=='silhouette':obj.data.vertices[0].co.x=2
        else:
            suffix='.collision' if case=='collision' else '.turret'
            obj=next(o for o in bpy.context.scene.objects if o.get('fl_role')=='geometry' and o.get('fl_asset')==ROOT+suffix and (case!='cannon' or o.get('fl_part')==1));obj.data.vertices[0].co.x+=.001
        restore_normals()
        try:export_scene(catalog,ids,families,geometries,lambda aid,a,b:compare(aid,a,b,catalog.definitions[aid][1]))
        except ValueError as error:rejected.append({'case':case,'error':str(error)})
        else:raise ValueError('Invalid saved project was accepted: '+case)
    load_project(catalog,ids,families,blend)
    obj=next(o for o in bpy.context.scene.objects if o.get('fl_role')=='geometry' and o.get('fl_asset')==ROOT+'.damaged')
    for v in obj.data.vertices:v.co.x+=.001
    bpy.ops.wm.save_as_mainfile(filepath=str(output/'edited.blend'),check_existing=False)
    edited=output/'edited';edited.mkdir();export_tank({'inputRoot':request['inputRoot'],'outputRoot':str(edited),'arguments':['--action','Export','--fixtures',str(fixture),'--blend-file',str(output/'edited.blend')]})
    candidate=Catalog(edited/'source');aid=ROOT+'.damaged';result=read_geometry(candidate.source(aid),candidate.root,candidate.definitions[aid][1])
    for old,new in zip(geometries[aid]['primitives'],result['primitives']):
        for a,b in zip(old['attributes']['POSITION'],new['attributes']['POSITION']):
            if abs(b[0]-a[0]-.001)>1e-6 or max(abs(b[i]-a[i]) for i in (1,2))>1e-6:raise ValueError('Saved shape edit was lost in GLB output')
    write_json(output/'native-contracts.json',{'success':True,'rejected':rejected,'savedRestShapeEditExported':True})

if __name__=='__main__':run(json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig')))
