"""Ordinary CI checks for authoring recipes, images and immutable generations."""
import copy
import json
import math
from pathlib import Path
import struct
import sys
import tempfile
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from pbr_contract import validate, inputs, digest, generation_key, cache_valid
from pbr_png import read_png, write_png, inspect_png

ROOT=Path(__file__).resolve().parents[3]


class ProceduralPbrContractTests(unittest.TestCase):
    def setUp(self):
        self.config=json.loads((ROOT/'tools/blender/fixtures/pbr.json').read_text())

    def test_checked_in_master_identity_and_semantics(self):
        records=inputs(ROOT/'assets/source',self.config)
        self.assertEqual(6,len(records))
        self.assertEqual(8,len(validate(self.config)['recipes']))

    def test_editable_pilot_files_match_recorded_hashes(self):
        root=ROOT/'assets/source/materials/directorate/authoring/procedural'
        report=json.loads((root/'lineage.json').read_text(encoding='utf-8-sig'))
        self.assertEqual('pending',report['artApproval'])
        self.assertEqual(5,len(report['files']))
        for name,expected in report['files'].items():
            path=(root/name).resolve()
            self.assertTrue(path.is_relative_to(root.resolve()))
            self.assertEqual(expected,digest(path))

    def test_recipe_rejects_unknown_fields_and_unsupported_resolution(self):
        for key,value in [('unknown',1),('masterSize',2048),('seed',True),('schemaVersion',2),('runtimeCaps',{'base':4096,'normal':1024,'orm':512})]:
            with self.subTest(key=key):
                c=copy.deepcopy(self.config);c[key]=value
                with self.assertRaises(ValueError):validate(c)

    def test_lower_runtime_profiles_retain_master_resolution(self):
        self.config['runtimeCaps']={'base':512,'normal':512,'orm':512}
        self.assertEqual(4096,validate(self.config)['masterSize'])

    def test_recipe_rejects_missing_family(self):
        del self.config['recipes']['glass']
        with self.assertRaises(ValueError):validate(self.config)

    def test_physical_properties_and_uv_identity_are_bounded(self):
        for key,value in [('roughness',math.nan),('roughness',0),('heightMetres',.1),('frequency',129),('cell',1),('metallic',1),('baseLinear',[0,0]),('baseLinear',[0,math.inf,0]),('roughness',True)]:
            with self.subTest(key=key,value=value):
                c=copy.deepcopy(self.config);c['recipes']['painted_armor'][key]=value
                with self.assertRaises(ValueError):validate(c)

    def test_generation_key_tracks_seed_selection_source_toolchain_code(self):
        def key(c=self.config,s=['painted_armor'],r={'input':'a'},t={'version':'5.2.2'},code={'script':'a'}):return generation_key(c,s,r,t,code)
        baseline=key();c=copy.deepcopy(self.config);c['seed']+=1
        for changed in (key(c=c),key(s=['glass']),key(r={'input':'b'}),key(t={'version':'6'}),key(code={'script':'b'})):
            self.assertNotEqual(baseline,changed)
        self.assertEqual(key(s=['glass','canvas']),key(s=['canvas','glass']))

    def test_cache_rejects_missing_damaged_and_escaping_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);image=root/'image.png';image.write_bytes(b'complete')
            report={'key':'a','files':{'image.png':digest(image)}}
            (root/'generation.json').write_text(json.dumps(report))
            self.assertTrue(cache_valid(root,'a'));self.assertFalse(cache_valid(root,'b'))
            image.write_bytes(b'interrupted');self.assertFalse(cache_valid(root,'a'))
            report['files']={'../escape':'x'};(root/'generation.json').write_text(json.dumps(report))
            self.assertFalse(cache_valid(root,'a'))
            (root/'generation.json').write_text('{');self.assertFalse(cache_valid(root,'a'))

    def test_lossless_normal_orm_and_alpha_samples(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'samples.png';pixels=bytes([128,128,255,255,255,184,0,255])
            write_png(path,2,1,pixels)
            self.assertEqual((2,1,4),inspect_png(path))
            self.assertEqual((2,1,4,pixels),read_png(path))
            data=bytearray(path.read_bytes());data[-5]^=1;path.write_bytes(data)
            with self.assertRaises(ValueError):read_png(path)

    def test_truncated_or_non_image_data_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'bad.png'
            for content in (b'',b'not an image',b'\x89PNG\r\n\x1a\n',b'\x89PNG\r\n\x1a\n\xff\xff\xff\xffIDAT'):
                path.write_bytes(content)
                with self.assertRaises(ValueError):read_png(path)


if __name__=='__main__':unittest.main()
