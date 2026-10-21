"""Reject changes to persistent terrain identities and approved physical-map policy."""
import copy
import json
import math
from pathlib import Path
import shutil
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from terrain_pbr_contract import binding, controls, definition_name, FAMILIES, MAPS, MATERIALS, SLOTS, TEXTURES

ROOT = Path(__file__).resolve().parents[3]/'assets/source'


class TerrainPbrContracts(unittest.TestCase):
    def test_current_catalog_has_eight_slots_seven_sets_and_twenty_one_maps(self):
        records=[binding(ROOT,f) for f in FAMILIES]
        self.assertEqual(8,sum(len(r['materials']) for r in records))
        self.assertEqual(7,len(records))
        self.assertEqual(21,len({m['policy']['id'] for r in records for m in r['maps'].values()}))
        self.assertEqual('dark_ash',SLOTS['industrial_ground'])
        self.assertEqual('dark_ash',SLOTS['scorched'])
        ash=next(r for r in records if r['family']=='dark_ash')
        self.assertEqual([.65,.60,.55,1],ash['materials']['scorched']['baseColorFactor'])

    def test_persistent_compiler_contract_mutations_are_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            record=binding(ROOT,'dry_dirt')
            for item in record['maps'].values():
                p=root/item['definition'];p.parent.mkdir(parents=True,exist_ok=True)
                shutil.copyfile(ROOT/item['definition'],p)
                original=root/item['original'];original.write_bytes((ROOT/item['original']).read_bytes()[:29])
            (root/MATERIALS).mkdir(parents=True)
            shutil.copyfile(ROOT/MATERIALS/'dirt.material.json',root/MATERIALS/'dirt.material.json')
            target=root/TEXTURES/definition_name('dry_dirt','normal')
            initial=json.loads(target.read_text())
            cases=[('id','texture.world.terrain.new'),('type','material'),('textureUsage','baseColor'),
                   ('textureColorSpace','srgb'),('textureMaxDimension',1024),
                   ('textureGenerateMipmaps',False),('textureFormat','rgba8Unorm'),
                   ('textureCompressionQuality','best'),('source','../../outside.png'),
                   ('textureMaxMipLevels',1)]
            for key,value in cases:
                with self.subTest(key=key):
                    data=copy.deepcopy(initial);data[key]=value
                    target.write_text(json.dumps(data),encoding='utf-8')
                    with self.assertRaises(ValueError):binding(root,'dry_dirt')
            target.write_text(json.dumps(initial),encoding='utf-8')
            material=root/MATERIALS/'dirt.material.json';data=json.loads(material.read_text())
            data['normalTexture']='texture.world.terrain.bedrock_normal'
            material.write_text(json.dumps(data),encoding='utf-8')
            with self.assertRaises(ValueError):binding(root,'dry_dirt')

    def test_bounded_saved_material_controls(self):
        controls((1,1,1,1),0,0)
        controls((.8,1.2,1,1),-.05,.04)
        for tint,bias,detail in [((1,1,1,0),0,0),((1.21,1,1,1),0,0),
                                 ((math.nan,1,1,1),0,0),((1,1,1,1),.051,0),
                                 ((1,1,1,1),0,-.01),((1,1,1,1),0,.041),
                                 ((1,1,1,1),math.inf,0)]:
            with self.subTest(values=(tint,bias,detail)):
                with self.assertRaises(ValueError):controls(tint,bias,detail)


if __name__=='__main__':unittest.main()
