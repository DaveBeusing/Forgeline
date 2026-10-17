"""Create or export the editable tank rest project through existing strict contracts."""
import argparse,copy,json,sys
from pathlib import Path
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
from asset_contract import Catalog,fixture_selection,read_geometry,write_glb,write_json,require,sha
from roundtrip import import_scene,export_scene
from tank_contract import metadata,compare
from mechanical import layers,inspection


def load_project(catalog,ids,families,path):
    require(path and path.is_file(),'Export requires a saved tank project');bpy.ops.wm.open_mainfile(filepath=str(path.resolve()))
    require(json.loads(bpy.context.scene.get('fl_tank_metadata','null'))==metadata(catalog,ids),'Tank definitions changed; regenerate or migrate the rest project explicitly')
    # Only source paths/hashes may advance after a reviewed geometry export.
    contract=json.loads(bpy.context.scene['fl_contract']);require(contract['ids']==ids and contract['families']==families,'Tank family changed')
    contract['sourceSignature']=catalog.signature(ids)[0];bpy.context.scene['fl_contract']=json.dumps(contract,sort_keys=True)


def restore_normals():
    # Vertex-only edits keep the explicit normal owner. Re-encode Blender's
    # compressed preview after opening/saving, as for mechanical pivot rebasing.
    for obj in bpy.context.scene.objects:
        if obj.get('fl_role')!='geometry':continue
        mesh=obj.data
        if 'fl_normal' not in mesh.attributes:continue
        mesh.update();mesh.normals_split_custom_set([tuple(x.vector) for x in mesh.attributes['fl_normal'].data])
        # Snapshot before writing an attribute, which invalidates Blender's cache.
        previews=[tuple(n.vector) for n in mesh.corner_normals]
        for index,value in enumerate(previews):mesh.attributes['fl_normal_preview'].data[index].vector=value


def run(request):
    output=Path(request['outputRoot']);report={'success':False,'errors':[]}
    try:
        parser=argparse.ArgumentParser();parser.add_argument('--action',choices=['Author','Export'],required=True);parser.add_argument('--fixtures',type=Path,required=True);parser.add_argument('--blend-file',type=Path);parser.add_argument('--render',action='store_true');args=parser.parse_args(request['arguments'])
        catalog=Catalog(request['inputRoot']);ids,families=fixture_selection(catalog,args.fixtures,[])
        original=catalog.signature(ids)[0];geometries={aid:read_geometry(catalog.source(aid),catalog.root,catalog.definitions[aid][1]) for aid in ids if catalog.definitions[aid][1]['type']=='mesh'}
        if args.action=='Author':
            require(not args.blend_file,'Author does not accept a saved project');import_scene(catalog,ids,families,geometries)
            bpy.context.scene['fl_tank_metadata']=json.dumps(metadata(catalog,ids),sort_keys=True);layers(families)
        else:
            load_project(catalog,ids,families,args.blend_file)
        restore_normals()
        results,meshes,deviations=export_scene(catalog,ids,families,geometries,lambda aid,a,b:compare(aid,a,b,catalog.definitions[aid][1]))
        if args.action=='Author':bpy.ops.wm.save_as_mainfile(filepath=str(output/'main_battle_tank.blend'),check_existing=False)
        source=output/'source';catalog.copy_to(ids,source)
        for aid,geometry in results.items():
            if aid.endswith('.collision'):continue
            path,definition=catalog.definitions[aid];relative=path.relative_to(catalog.root);updated=copy.deepcopy(definition)
            target=Path('../sources/main_battle_tank/blender')/(aid+'.glb');write_glb(source/relative.parent/target,geometry);updated['source']=target.as_posix();write_json(source/relative,updated)
        Catalog(source).validate(ids)
        samples,images=inspection(catalog,ids,families,results,output,args.render,preview_resolution=(768,432))
        require(catalog.signature(ids)[0]==original,'Input sources changed during export')
        report.update(success=True,sourceSignature=original,blendSha256=sha(args.blend_file) if args.blend_file else sha(output/'main_battle_tank.blend'),meshes=meshes,deviations=deviations,poseSamples=samples,previewImages=images)
    except Exception as error:report['errors'].append(str(error));raise
    finally:write_json(output/'tank-report.json',report)

if __name__=='__main__':run(json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig')))
