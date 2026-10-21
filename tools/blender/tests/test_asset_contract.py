import copy
import json
import math
import struct
import sys
import tempfile
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'tools/blender'))
from asset_contract import (Catalog, blender_to_game, compare_geometry, fixture_selection,
    game_to_blender, geometry_stats, read_geometry, socket_world, write_glb, write_json)


def triangle():
    return {'document':{'asset':{'version':'2.0'},'nodes':[{'mesh':0}],
        'meshes':[{'name':'AsymmetricFixture','primitives':[{}]}]},'primitives':[{
        'mesh':0,'primitive':0,'name':'AsymmetricFixture','materialSlot':-1,'indices':[0,1,2],
        'attributes':{'POSITION':[(.1,.2,.3),(.8,.2,.3),(.1,.7,.3)],
            'NORMAL':[(0,0,1)]*3,'TEXCOORD_0':[(0,0),(1,0),(0,1)],'TANGENT':[(1,0,0,1)]*3}}]}


def change_json(path, edit):
    raw=path.read_bytes();size=struct.unpack_from('<I',raw,12)[0]
    document=json.loads(raw[20:20+size]);binary=raw[28+size:]
    edit(document)
    data=json.dumps(document,separators=(',',':')).encode();data+=b' '*(-len(data)%4)
    path.write_bytes(struct.pack('<III',0x46546c67,2,28+len(data)+len(binary))+struct.pack('<II',len(data),0x4e4f534a)+data+struct.pack('<II',len(binary),0x004e4942)+binary)


class AssetContractTests(unittest.TestCase):
    def setUp(self):
        parent=ROOT/'artifacts/blender-python-contracts';parent.mkdir(parents=True,exist_ok=True)
        self.temp=tempfile.TemporaryDirectory(dir=parent)
        self.root=Path(self.temp.name).resolve()
        self.assertTrue(self.root.is_relative_to(parent.resolve()))
        self.addCleanup(self.temp.cleanup)
        self.geometry=triangle();self.path=self.root/'mesh.glb';write_glb(self.path,self.geometry)
        self.definition={'id':'mesh.test','type':'mesh','source':'mesh.glb','scale':1.0}
        write_json(self.root/'mesh.asset.json',self.definition)

    def read(self):return read_geometry(self.path,self.root,self.definition)

    def test_axis_mapping_has_positive_determinant_and_known_forward(self):
        self.assertEqual((0,-1,0),game_to_blender((0,0,1)))
        self.assertEqual((0,0,1),game_to_blender((0,1,0)))
        a,b=game_to_blender((1,0,0)),game_to_blender((0,1,0))
        cross=(a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
        self.assertEqual(cross,game_to_blender((0,0,1)))
        self.assertEqual((3,5,-7),blender_to_game(game_to_blender((3,5,-7))))

    def test_glb_round_trip_preserves_all_float_channels_and_indices(self):
        loaded=self.read();self.assertLess(compare_geometry(self.geometry,loaded)['POSITION'],1e-7)
        self.assertEqual(1,geometry_stats(loaded)['triangles'])
        self.assertEqual({'POSITION','NORMAL','TANGENT','TEXCOORD_0'},set(loaded['primitives'][0]['attributes']))

    def test_literal_unicode_paths(self):
        self.path=self.root/'Grüße 日本語 [a].glb';write_glb(self.path,self.geometry)
        self.assertEqual(3,geometry_stats(self.read())['vertices'])

    def test_reject_wrong_axis_mapping(self):
        changed=copy.deepcopy(self.geometry)
        changed['primitives'][0]['attributes']['POSITION']=[game_to_blender(v) for v in changed['primitives'][0]['attributes']['POSITION']]
        with self.assertRaisesRegex(ValueError,'POSITION deviation'):compare_geometry(self.geometry,changed)

    def test_reject_wrong_geometry_scale(self):
        changed=copy.deepcopy(self.geometry);changed['primitives'][0]['attributes']['POSITION'][1]=(80,20,30)
        with self.assertRaisesRegex(ValueError,'POSITION deviation'):compare_geometry(self.geometry,changed)

    def test_reject_winding_change(self):
        changed=copy.deepcopy(self.geometry);changed['primitives'][0]['indices']=[0,2,1]
        with self.assertRaisesRegex(ValueError,'indices'):compare_geometry(self.geometry,changed)

    def test_reject_uv_normal_and_tangent_deviations(self):
        for semantic in ['NORMAL','TEXCOORD_0','TANGENT']:
            with self.subTest(semantic=semantic):
                changed=copy.deepcopy(self.geometry);row=list(changed['primitives'][0]['attributes'][semantic][0]);row[0]+=.1
                changed['primitives'][0]['attributes'][semantic][0]=row
                with self.assertRaisesRegex(ValueError,'deviation'):compare_geometry(self.geometry,changed)

    def test_reject_tangent_handedness_even_below_direction_tolerance(self):
        changed=copy.deepcopy(self.geometry);changed['primitives'][0]['attributes']['TANGENT'][0]=(1,0,0,.9999999)
        with self.assertRaisesRegex(ValueError,'handedness'):compare_geometry(self.geometry,changed)

    def test_reject_lines_and_triangle_strips(self):
        for mode in [1,5]:
            with self.subTest(mode=mode):
                write_glb(self.path,self.geometry);change_json(self.path,lambda d:d['meshes'][0]['primitives'][0].update(mode=mode))
                with self.assertRaisesRegex(ValueError,'triangle primitives'):self.read()

    def test_reject_animation_skin_morph_extensions_and_cameras(self):
        changes=[lambda d:d.update(animations=[{}]),lambda d:d.update(skins=[{}]),
            lambda d:d['meshes'][0]['primitives'][0].update(targets=[{'POSITION':0}]),
            lambda d:d.update(extensionsUsed=['KHR_draco_mesh_compression']),lambda d:d['nodes'][0].update(camera=0)]
        for change in changes:
            with self.subTest(change=changes.index(change)):
                write_glb(self.path,self.geometry);change_json(self.path,change)
                with self.assertRaisesRegex(ValueError,'Unsupported|unsupported'):self.read()

    def test_reject_unbaked_node_transforms(self):
        change_json(self.path,lambda d:d['nodes'][0].update(scale=[100,100,100]))
        with self.assertRaisesRegex(ValueError,'Bake glTF node transforms'):self.read()

    def test_reject_duplicate_mesh_instances(self):
        change_json(self.path,lambda d:d['nodes'].append({'mesh':0}))
        with self.assertRaisesRegex(ValueError,'exactly once'):self.read()

    def test_reject_sparse_normalized_and_overrun_accessors(self):
        for values in [{'sparse':{}},{'normalized':True},{'count':5000}]:
            write_glb(self.path,self.geometry)
            change_json(self.path,lambda d:d['accessors'][0].update(values))
            with self.assertRaisesRegex(ValueError,'Sparse|exceeds'):self.read()

    def test_reject_unknown_vertex_channels(self):
        change_json(self.path,lambda d:d['meshes'][0]['primitives'][0]['attributes'].update(COLOR_0=0))
        with self.assertRaisesRegex(ValueError,'vertex attributes'):self.read()

    def test_reject_material_slot_mismatch(self):
        change_json(self.path,lambda d:d['meshes'][0]['primitives'][0].update(material=3))
        self.definition['materialReferences']=['material.test']
        with self.assertRaisesRegex(ValueError,'stable material reference'):self.read()

    def test_two_material_slots_keep_stable_order(self):
        geometry=triangle();geometry['document']['meshes'][0]['primitives']=[{'material':0},{'material':1}]
        geometry['primitives'][0]['materialSlot']=0
        second=copy.deepcopy(geometry['primitives'][0]);second.update(primitive=1,materialSlot=1)
        geometry['primitives'].append(second);write_glb(self.path,geometry)
        self.definition['materialReferences']=['material.first','material.second']
        self.assertEqual([0,1],[p['materialSlot'] for p in self.read()['primitives']])

    def test_multiple_materials_require_explicit_slots(self):
        self.definition['materialReferences']=['material.first','material.second']
        with self.assertRaisesRegex(ValueError,'stable material reference|explicit primitive slots'):self.read()

    def test_reject_source_and_buffer_path_escape(self):
        source=self.root/'source';source.mkdir();outside=self.root/'outside.bin';outside.write_bytes(b'abc')
        gltf=source/'mesh.gltf';write_json(gltf,{'asset':{'version':'2.0'},'buffers':[{'byteLength':3,'uri':'../outside.bin'}],'meshes':[{}]})
        with self.assertRaisesRegex(ValueError,'escapes'):read_geometry(gltf,source,self.definition)
        with self.assertRaisesRegex(ValueError,'escapes'):read_geometry(self.path,source,self.definition)

    def test_reject_identifier_that_could_escape_export_directory(self):
        self.definition['id']='../../outside'
        write_json(self.root/'mesh.asset.json',self.definition)
        with self.assertRaisesRegex(ValueError,'Invalid stable asset ID'):Catalog(self.root)

    def test_reject_nonfinite_exported_channel(self):
        changed=copy.deepcopy(self.geometry)
        changed['primitives'][0]['attributes']['TEXCOORD_0'][2]=(0,float('nan'))
        with self.assertRaisesRegex(ValueError,'Nonfinite exported'):compare_geometry(self.geometry,changed)

    def test_reject_nonunit_metadata_scale(self):
        self.definition['scale']=.01;write_json(self.root/'mesh.asset.json',self.definition)
        with self.assertRaisesRegex(ValueError,'scale must be 1.0'):Catalog(self.root).validate(['mesh.test'])

    def test_reject_missing_mechanical_socket(self):
        self.definition['mechanicalParts']=[{'name':'wheel','firstIndex':0,'indexCount':3,'motion':'wheel','socket':'missing'}]
        write_json(self.root/'mesh.asset.json',self.definition)
        with self.assertRaisesRegex(ValueError,'Missing mechanical socket'):Catalog(self.root).validate(['mesh.test'])

    def test_reject_invalid_ranges(self):
        self.definition['mechanicalParts']=[{'name':'body','firstIndex':3,'indexCount':3}]
        write_json(self.root/'mesh.asset.json',self.definition)
        with self.assertRaisesRegex(ValueError,'partition'):Catalog(self.root).validate(['mesh.test'])

    def test_reject_missing_damage_state(self):
        fixture=self.root/'fixture.json';write_json(fixture,{'schemaVersion':1,'families':[{'root':'mesh.test','assets':['mesh.test','mesh.test.wreck']}]})
        with self.assertRaisesRegex(ValueError,'Missing asset/state/reference'):fixture_selection(Catalog(self.root),fixture,[])

    def test_reject_cyclic_lods(self):
        self.definition['lods']=[{'level':1,'assetId':'mesh.test','maxDistance':10}]
        write_json(self.root/'mesh.asset.json',self.definition)
        with self.assertRaisesRegex(ValueError,'Cyclic'):Catalog(self.root).closure(['mesh.test'])

    def test_reject_invalid_lod_levels(self):
        self.definition['lods']=[{'level':2,'assetId':'mesh.test','maxDistance':10}]
        write_json(self.root/'mesh.asset.json',self.definition)
        with self.assertRaisesRegex(ValueError,'LOD chain'):Catalog(self.root).validate(['mesh.test'])

    def test_reject_missing_collision(self):
        self.definition['collisionRequired']=True;write_json(self.root/'mesh.asset.json',self.definition)
        with self.assertRaisesRegex(ValueError,'Missing collision'):Catalog(self.root).validate(['mesh.test'])

    def test_socket_world_transform_includes_scale_yaw_and_translation(self):
        family={'instanceScale':[2,3,4],'instanceTranslation':[5,6,7],'instanceYawDegrees':90}
        actual=socket_world((1,2,3),family)
        for a,b in zip(actual,[17,12,5]):self.assertAlmostEqual(a,b)

    def test_production_tank_and_building_fixture_graph(self):
        catalog=Catalog(ROOT/'assets/source');ids,families=fixture_selection(catalog,ROOT/'tools/blender/fixtures/production.json',[])
        self.assertEqual(30,len(ids));self.assertEqual(2,len(families))
        meshes=[read_geometry(catalog.source(a),catalog.root,catalog.definitions[a][1]) for a in ids if catalog.definitions[a][1]['type']=='mesh']
        self.assertEqual(19,len(meshes));self.assertEqual(13128,sum(geometry_stats(g)['triangles'] for g in meshes))
        self.assertIn('unit.directorate.main_battle_tank.wreck',ids);self.assertIn('building.directorate.command_core.destroyed',ids)


if __name__=='__main__':unittest.main()
