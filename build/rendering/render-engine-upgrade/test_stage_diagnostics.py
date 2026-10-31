import base64,copy,json,tempfile,unittest,shutil
from pathlib import Path
from baseline_contracts import canonical,sha256_bytes
from stage_diagnostics import STAGES,compare_series,read_capture,compare_lanes
from unittest.mock import patch

class StageDiagnosticsTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup);self.root=Path(self.tmp.name);self.refs=[]
        profile=dict(requested='Medium',active='Medium',linearScene=True,shadows=dict(quality='Low',enabled=True,resolution=1024),atmosphere='Medium',particles={},terrainDetail={},lodDiameterScale=1,postProcessing={},fallbackReason=None,resolutionScale=1,textures='Full',vegetationMotion=True,requestedSettings=dict(quality='Medium'),environment={},vegetation={},terrainResidency={})
        for i in range(8):
            stages=[]
            for name in STAGES:
                fmt='R16G16B16A16_Float' if name=='SceneLinear' else 'R8G8B8A8_UNorm' if name in ('ComposedColor','FinalColor') else 'R32_Typeless';bpp=8 if name=='SceneLinear' else 4
                width=height=1024 if name=='ShadowDepth' else 2;pitch=((width*bpp+255)//256)*256
                layout=dict(width=width,height=height,rowPitch=pitch,offset=512,format=fmt);layout_bytes=canonical(layout);raw=bytearray([i])*(512+(height-1)*pitch+width*bpp)
                texels=(bytes(range(256))*((width*height*bpp+255)//256))[:width*height*bpp]
                for y in range(height):raw[512+y*pitch:512+y*pitch+width*bpp]=texels[y*width*bpp:(y+1)*width*bpp]
                file=f'{i}-{name}.raw';(self.root/file).write_bytes(raw)
                stages.append(dict(stage=name,file=file,layout=layout,rawBytes=len(raw),bytesPerTexel=bpp,logicalBytes=len(texels),rawSha256=sha256_bytes(raw),logicalSha256=sha256_bytes(texels),layoutBytesBase64=base64.b64encode(layout_bytes).decode(),layoutSha256=sha256_bytes(layout_bytes)))
            record=dict(schemaVersion=1,kind='renderer-stage-readback',qualified=False,producer='native-reference',sourceSha='a'*40,runtimeFingerprint='b'*64,recipeFingerprint='c'*64,recipeInputBytesBase64='',outputRaster=dict(width=2,height=2,bufferCount=3),inputFingerprint='d'*64,repetition=i,frameIndex=i%3,submission=dict(submission=i+1,submittedFence=i*3+1,generation=1,resourceGeneration=1),completedFence=i*3+2,presentedBefore=i,presentedAfter=i+1,
                deviceIdentity=dict(schemaVersion=1,source='ID3D12Device.AdapterLuid',nodeCount=1,queueNodeMask=0,effectiveNodeMask=1,generation=1,processId=5,processStartedFileTimeUtc=10,sessionId='e'*32,adapterLuidHex='0000000000015520'),camera=dict(target=dict(x=0,y=0,z=0),yawRadians=.5,pitchRadians=.6,distance=50,hasPointerPosition=False,pointerPosition=dict(x=0,y=0)),effectiveProfile=profile,stages=stages)
            recipe=canonical(dict(display=dict(width=2,height=2),settings=profile['requestedSettings'],runtimeFingerprint='b'*64))
            record.update(recipeFingerprint=sha256_bytes(recipe),recipeInputBytesBase64=base64.b64encode(recipe).decode())
            self.save(i,record)
    def save(self,i,value):
        raw=canonical(value);path=f'{i}.json';(self.root/path).write_bytes(raw);ref=dict(path=path,sha256=sha256_bytes(raw))
        if i<len(self.refs):self.refs[i]=ref
        else:self.refs.append(ref)
    def record(self,i):return json.loads((self.root/f'{i}.json').read_bytes())
    def test_all_eight_logical_images_equal_despite_different_raw_padding(self):
        value=compare_series(self.root,self.refs);self.assertEqual(value['status'],'equal');self.assertFalse(value['qualified'])
    def test_one_lsb_stays_failed_and_first_divergent_stage_has_neighbor_bytes(self):
        r=self.record(3);stage=r['stages'][2];p=self.root/stage['file'];raw=bytearray(p.read_bytes());raw[512+2]+=1;p.write_bytes(raw)
        stage['rawSha256']=sha256_bytes(raw);logical=raw[512:528]+raw[768:784];stage['logicalSha256']=sha256_bytes(logical);self.save(3,r)
        value=compare_series(self.root,self.refs);self.assertEqual(value['status'],'mismatch');row=value['comparisons'][3];self.assertEqual(row['firstDivergentStage'],'SceneLinear');self.assertEqual(row['stages'][2]['differingBytes'],1);self.assertTrue(row['stages'][2]['neighborhood'])
    def test_no_missing_tail_duplicate_replay_or_reordered_record(self):
        for refs in (self.refs[:-1],self.refs[:7]+self.refs[:1],self.refs[1:]+self.refs[:1]):
            with self.assertRaises(ValueError):compare_series(self.root,refs)
    def test_raw_file_layout_hash_and_metadata_tampering_fail(self):
        original=self.record(1)
        for mutate in (lambda r:r.update(sourceSha='f'*40),lambda r:r.update(camera={}),lambda r:r.update(completedFence=0),lambda r:r['submission'].update(submittedFence=1),lambda r:r['stages'][0].update(rawSha256='0'*64),lambda r:r['stages'][0]['layout'].update(rowPitch=4),lambda r:r['deviceIdentity'].update(generation=True),lambda r:r.update(schemaVersion=True),lambda r:r['stages'].pop()):
            r=copy.deepcopy(original);mutate(r);self.save(1,r)
            with self.assertRaises((ValueError,KeyError)):compare_series(self.root,self.refs)
        self.save(1,original)
    def host_records(self):
        refs=[]
        for i in range(8):
            r=self.record(i);r['producer']='actual-render-host';r['deviceIdentity'].update(processId=7,processStartedFileTimeUtc=20,sessionId='f'*32,generation=3,adapterLuidHex='0000000000015521');r['submission'].update(submission=i+100,submittedFence=1000+i*5,generation=3,resourceGeneration=8);r.update(completedFence=1001+i*5,frameIndex=(i+1)%3,presentedBefore=200+i,presentedAfter=201+i)
            raw=canonical(r);path=f'host-{i}.json';(self.root/path).write_bytes(raw);refs.append(dict(path=path,sha256=sha256_bytes(raw)))
        return refs
    def sibling_lanes(self):
        host=self.host_records();nested=[]
        for name,refs in (('native',self.refs),('host',host)):
            folder=self.root/name/'capture';folder.mkdir(parents=True);lane=[]
            for i,ref in enumerate(refs):
                record=json.loads((self.root/ref['path']).read_bytes())
                for stage in record['stages']:shutil.copyfile(self.root/stage['file'],folder/stage['file'])
                raw=canonical(record);path=folder/f'{i}.json';path.write_bytes(raw);lane.append(dict(path=path.relative_to(self.root).as_posix(),sha256=sha256_bytes(raw)))
            nested.append(lane)
        return nested
    def test_real_producer_shaped_sibling_metadata_resolves_same_basenames_per_lane(self):
        native,host=self.sibling_lanes()
        with patch('stage_diagnostics.verified_lane_binding',return_value=('same-physical-uuid','same-full-pci')):
            value=compare_lanes(self.root,native,host,{},{});self.assertEqual(value['status'],'equal');self.assertFalse(value['qualified'])
        self.assertEqual(compare_series(self.root,native)['status'],'equal');self.assertEqual(compare_series(self.root,host)['status'],'equal')
    def test_sibling_raw_path_traversal_alias_and_wrong_hash_are_rejected(self):
        native,host=self.sibling_lanes();path=self.root/host[0]['path'];original=json.loads(path.read_bytes())
        for mutate in (lambda r:r['stages'][0].update(file='../0-ShadowDepth.raw'),lambda r:r['stages'][1].update(file=r['stages'][0]['file']),lambda r:r['stages'][0].update(rawSha256='f'*64)):
            record=copy.deepcopy(original);mutate(record);raw=canonical(record);path.write_bytes(raw);ref=dict(path=host[0]['path'],sha256=sha256_bytes(raw))
            with self.assertRaises(ValueError):read_capture(self.root,ref)
        path.write_bytes(canonical(original))
        # A metadata reference remains cohort-relative, never raw-file-relative.
        with self.assertRaises(ValueError):read_capture(self.root,dict(path='../host/capture/0.json',sha256=host[0]['sha256']))
    def test_cross_lane_uses_independent_process_fence_slot_identities(self):
        host=self.host_records()
        with patch('stage_diagnostics.verified_lane_binding',return_value=('actual-uuid','00000000:30:00.0')) as binding:
            result=compare_lanes(self.root,self.refs,host,{},{});self.assertEqual(result['status'],'equal');self.assertEqual(binding.call_count,2)
            self.assertNotEqual(result['comparisons'][0]['nativeSubmission'],result['comparisons'][0]['hostSubmission'])
    def test_cross_lane_missing_physical_proof_or_different_actual_adapter_rejects(self):
        host=self.host_records()
        with self.assertRaises((ValueError,KeyError)):compare_lanes(self.root,self.refs,host,{}, {})
        with patch('stage_diagnostics.verified_lane_binding',side_effect=[('uuid1','pci1'),('uuid2','pci2')]):
            with self.assertRaises(ValueError):compare_lanes(self.root,self.refs,host,{}, {})
    def test_cross_lane_different_pose_cannot_be_called_pixel_parity(self):
        host=self.host_records()
        for ref in host:
            r=json.loads((self.root/ref['path']).read_bytes());r['camera']['distance']=60;raw=canonical(r);(self.root/ref['path']).write_bytes(raw);ref['sha256']=sha256_bytes(raw)
        with patch('stage_diagnostics.verified_lane_binding',return_value=('uuid','pci')):
            with self.assertRaises(ValueError):compare_lanes(self.root,self.refs,host,{}, {})
    def test_unsupported_format_and_path_escape_cannot_be_accepted(self):
        r=self.record(1);r['stages'][0]['file']='../escape';self.save(1,r)
        with self.assertRaises(ValueError):compare_series(self.root,self.refs)
        r=self.record(0);r['stages'][0]['layout']['format']='R8_UNorm';raw=canonical(r['stages'][0]['layout']);r['stages'][0].update(layoutBytesBase64=base64.b64encode(raw).decode(),layoutSha256=sha256_bytes(raw));self.save(0,r)
        with self.assertRaises(ValueError):compare_series(self.root,self.refs)
if __name__=='__main__':unittest.main()
