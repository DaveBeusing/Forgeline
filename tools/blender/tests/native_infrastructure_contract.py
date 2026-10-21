"""Real saved-master edit/rejection checks, independent of fixture-only tests."""
import copy
import json
import sys
from pathlib import Path
import bpy

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from asset_contract import Catalog, fixture_selection, read_geometry, require, write_glb, write_json
from infrastructure import unpack_preview
from infrastructure_contract import canonical_geometry, family_catalog, export_filename
from roundtrip import export_scene
from tank import restore_normals


def run(request):
    master,fixture,root= request['arguments']
    master=Path(master);output=Path(request['outputRoot'])
    catalog=family_catalog(request['inputRoot'],json.loads(Path(fixture).read_text(encoding='utf-8-sig')))
    ids,families=fixture_selection(catalog,fixture,[root])
    geometries={aid:read_geometry(catalog.source(aid),catalog.root,catalog.definitions[aid][1]) for aid in ids if catalog.definitions[aid][1]['type']=='mesh'}
    passed=[]
    def reopen():
        bpy.ops.wm.open_mainfile(filepath=str(master.resolve()))
        return next(o for o in bpy.context.scene.objects if o.get('fl_role')=='geometry' and o.get('fl_asset')==root)
    def export():
        unpack_preview(catalog,ids);restore_normals()
        return export_scene(catalog,ids,families,geometries,
            lambda aid,a,b:dict(delta=canonical_geometry(a,b,'.collision' in aid)[1]))[0]
    def reject(name,edit):
        obj=reopen();edit(obj)
        try:export()
        except (ValueError,KeyError):passed.append(name);return
        raise AssertionError('Invalid saved-master edit accepted: '+name)
    reject('axis',lambda o:setattr(o,'rotation_euler',(0,0,.1)))
    reject('origin',lambda o:setattr(o,'location',(1,0,0)))
    reject('units',lambda o:setattr(bpy.context.scene.unit_settings,'scale_length',100))
    reject('material slot',lambda o:o.data.materials.clear())
    reject('UV removal',lambda o:o.data.uv_layers.remove(o.data.uv_layers.active))
    reject('modifier',lambda o:o.modifiers.new('Unsupported displacement','DISPLACE'))
    reject('PBR factor',lambda o:setattr(next(n for n in bpy.data.materials[catalog.definitions[root][1]['materialReferences'][0]].node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Base Color'],'default_value',(1,0,0,1)))
    reject('large shape',lambda o:setattr(o.data.vertices[0],'co',o.data.vertices[0].co+( __import__('mathutils').Vector((.1,0,0)))))
    obj=reopen();source_vertex=obj.data.attributes['fl_vertex'].data[0].value
    old=geometries[root]['primitives'][obj['fl_primitive']]['attributes']['POSITION'][source_vertex]
    bounds=[p for primitive in geometries[root]['primitives'] for p in primitive['attributes']['POSITION']]
    delta=.001 if old[0]+.001<=max(p[0] for p in bounds) else -.001
    obj.data.vertices[0].co.x+=delta
    edited=output/'saved-edit.blend';bpy.ops.wm.save_as_mainfile(filepath=str(edited),check_existing=False,compress=True)
    bpy.ops.wm.open_mainfile(filepath=str(edited));results=export()
    changed=[];source=output/'edit-source';catalog.copy_to(ids,source)
    for aid,result in results.items():
        canonical,maximum=canonical_geometry(geometries[aid],result,'.collision' in aid)
        if maximum>1e-6:changed.append(aid)
        path,definition=catalog.definitions[aid];target=Path('_edited')/export_filename(aid)
        write_glb(source/path.relative_to(catalog.root).parent/target,canonical)
        write_json(source/path.relative_to(catalog.root),dict(definition,source=target.as_posix()))
    require(changed==[root],'Saved supported edit did not change exactly its intended mesh')
    write_json(output/'native-infrastructure-contracts.json',dict(success=True,rejected=passed,savedEditReopened=True,changedAssets=changed))


if __name__=='__main__':run(json.loads(Path(sys.argv[sys.argv.index('--request')+1]).read_text(encoding='utf-8-sig')))
