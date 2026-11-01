import base64,copy,json,struct,tempfile,unittest,zlib
from pathlib import Path
import post_frame_snapshot as p
from baseline_contracts import sha256_bytes


def encoded(value):return json.dumps(value,separators=(',',':')).encode()
def png(pixels):
    def chunk(k,v):return struct.pack('>I',len(v))+k+v+struct.pack('>I',zlib.crc32(k+v)&0xffffffff)
    return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',2,2,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(b'\0'+pixels[:8]+b'\0'+pixels[8:]))+chunk(b'IEND',b'')


def fixture(root):
    pixels=bytes(16);image=png(pixels);(root/'first.png').write_bytes(image);raw=bytes(range(8));(root/'buffer.raw').write_bytes(raw)
    profile={'requestedSettings':{'quality':'Medium'}};profile_raw=encoded(profile)
    recipe=encoded({'display':{'width':2,'height':2},'settings':profile['requestedSettings'],'runtimeFingerprint':'b'*64})
    camera={'target':dict(x=0,y=0,z=0),'yawRadians':.5,'pitchRadians':.6,'distance':50,'hasPointerPosition':False,'pointerPosition':dict(x=0,y=0)}
    device=dict(adapterLuidHex='0000000000000001',nodeCount=1,queueNodeMask=0,effectiveNodeMask=1,generation=1,processId=42,processStartedFileTimeUtc=1,sessionId='e'*32,schemaVersion=1,source='actual synthetic source shape')
    m=dict(sourceSha='a'*40,producer='native-reference',runtimeFingerprint='b'*64,recipeFingerprint=sha256_bytes(recipe),recipeInputBytesBase64=base64.b64encode(recipe).decode(),repetition=0,camera=camera,
        profileDomain='effective-quality-profile',profileInputBytesBase64=base64.b64encode(profile_raw).decode(),profileFingerprint=sha256_bytes(profile_raw),observedComposition={'profile':profile,'lighting':{},'atmosphere':{}},
        pixelSha256=sha256_bytes(pixels),legacyFingerprint='c'*64,image=dict(file='first.png',sha256=sha256_bytes(image),bytes=len(image)),qualified=False)
    original=dict(schemaVersion=1,fenceDomain='original-device-frame-fence',deviceIdentity=device,managedOwnerThreadId=1,frameIndex=0,submission=dict(submission=578,submittedFence=700,generation=1,resourceGeneration=2),completedFence=700,presentedBefore=10,presentedAfter=11,width=2,height=2,resizeGeneration=1,appliedResizeGeneration=1)
    layout=dict(mipIndex=-1,width=0,height=0,rowCount=1,rowBytes=8,rowPitch=8,packedOffset=0,paddedBytes=8,canonicalBytes=8,format='Buffer')
    d=dict(localId=1,semanticName='terrain-resident-vertex',kind='buffer',lifetimeAvailable=True,buffer=dict(sizeInBytes=8,memory='Upload'),texture=None,implicit=None,sourceOffset=0,observedBytes=8,rangeMeaning='retained only',mips=[layout])
    value=dict(schemaVersion=1,kind='reference-post-final-color-resident-resources',complete=True,metadata=m,originalFrameReceipt=original,
        copyReceipt=dict(schemaVersion=1,fenceDomain='late-copy-fence-'+('f'*32),queueRelationship='Same DIRECT queue ordered after original frame completion',submittedFence=1,completedFence=1,mayHaveExecuted=True,completionProved=True),
        coverage=dict(meaning='Late subset',included=['resident-terrain','final-retained-main-scratch','final-retained-decoration-scratch','captured-slot-constant-arena-used-prefix-if-present'],excluded=['render-targets','depth-shadow-targets','root-constants','particles','HUD','skin-caches','inactive-private-caches','earlier-pass-scratch','entity-root-associations'],unknown='Not shader contents'),
        resources=[dict(description=d,rawMips=[dict(file='buffer.raw',sha256=sha256_bytes(raw),bytes=8,mipIndex=-1)])],intent=dict(terrainChunks=[],mainBatches=[],decorationBatches=[]))
    expected={k:copy.deepcopy(m[k]) for k in p.EXPECTED.split() if k in m};expected.update(deviceIdentity=copy.deepcopy(device),managedOwnerThreadId=1,frameIndex=0,actualSubmission=copy.deepcopy(original['submission']))
    return value,expected


def persist(root,value,name='snapshot.json'):
    raw=encoded(value);(root/name).write_bytes(raw);return dict(file=name,sha256=sha256_bytes(raw),bytes=len(raw),schemaVersion=1)


class PostFrameSnapshotTests(unittest.TestCase):
    def test_signed_part_index_preserves_ordinary_skin_and_int32_limits(self):
        row=dict(order=0,meshKey='mesh',materialKey=None,partIndex=-1,instanceCount=1,instanceOffset=0,instanceStride=112,vertexStride=48,
            firstIndex=0,indexCount=3,indexFormat='ThirtyTwoBit',passMeaning='Retained scratch only',resources=[dict(resourceId=1,role='captured-slot-instance-prefix'),dict(resourceId=2,role='mesh-vertex'),dict(resourceId=3,role='mesh-index')])
        resources={1:dict(kind='buffer',sourceOffset=0,observedBytes=112),2:dict(kind='buffer',sourceOffset=0,observedBytes=144),3:dict(kind='buffer',sourceOffset=0,observedBytes=12)}
        for part in (-1,-2,-17,-2**31,2**31-1):
            with self.subTest(part=part):
                row['partIndex']=part;p.intent(dict(terrainChunks=[],mainBatches=[row],decorationBatches=[]),resources)
        for part in (True,False,-2**31-1,2**31,1.0,None):
            with self.subTest(part=part),self.assertRaises(ValueError):
                row['partIndex']=part;p.intent(dict(terrainChunks=[],mainBatches=[row],decorationBatches=[]),resources)
        row['partIndex']=-1
        for key in ('instanceOffset','firstIndex'):
            bad=copy.deepcopy(row);bad[key]=-1
            with self.subTest(key=key),self.assertRaises(ValueError):p.intent(dict(terrainChunks=[],mainBatches=[bad],decorationBatches=[]),resources)
    def test_historical_absence_unavailable_selected_missing_fails(self):
        self.assertEqual(p.read_capture('.',None,None,snapshot_version=0)['status'],'unavailable-historical')
        for version in (False,True,1.0,2,None,'1'):
            with self.subTest(version=version),self.assertRaises(ValueError):p.read_capture('.',None,None,snapshot_version=version)
        with self.assertRaises(ValueError):p.read_capture('.',None,None)
    def test_actual_original_png_and_separate_fence_domains(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);v,e=fixture(root);r=p.read_capture(root,persist(root,v),e)
            self.assertEqual(r['canonicalBytes'],8);self.assertFalse(r['qualified'])
            self.assertLess(v['copyReceipt']['completedFence'],v['originalFrameReceipt']['submission']['submittedFence'])
    def test_nested_source_tuple_and_completion_mutations_fail(self):
        changes=[('complete',False),('schemaVersion',True),('copyReceipt.completionProved',False),('copyReceipt.mayHaveExecuted',False),
            ('copyReceipt.completedFence',0),('copyReceipt.fenceDomain','original-device-frame-fence'),('originalFrameReceipt.managedOwnerThreadId',True),
            ('originalFrameReceipt.frameIndex',2),('originalFrameReceipt.deviceIdentity.processId',43),('originalFrameReceipt.submission.resourceGeneration',3),
            ('originalFrameReceipt.completedFence',699),('originalFrameReceipt.presentedAfter',12),('metadata.sourceSha','d'*40),('metadata.repetition',True),
            ('metadata.profileDomain','full-ClientRenderConfiguration'),('metadata.pixelSha256','e'*64),('metadata.camera.distance',False),('metadata.qualified',True)]
        for path,value in changes:
            with self.subTest(path=path),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=fixture(root);target=v;parts=path.split('.')
                for key in parts[:-1]:target=target[key]
                target[parts[-1]]=value
                with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e)
    def test_raw_shape_hash_alias_and_traversal_are_independent_refusals(self):
        for defect in ('hash','bytes','path','alias','overlap','row','bool','missing','overflow'):
            with self.subTest(defect=defect),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=fixture(root);r=v['resources'][0];d=r['description'];m=d['mips'][0]
                if defect=='hash':r['rawMips'][0]['sha256']='f'*64
                elif defect=='bytes':r['rawMips'][0]['bytes']=7
                elif defect=='path':r['rawMips'][0]['file']='../buffer.raw'
                elif defect=='row':m['rowBytes']=7
                elif defect=='bool':d['localId']=True
                elif defect=='missing':del d['rangeMeaning']
                elif defect=='overflow':m['packedOffset']=p.RAW_BYTES
                else:
                    other=copy.deepcopy(r);other['description']['localId']=2;other['description']['mips'][0]['packedOffset']=512 if defect=='alias' else 0;v['resources'].append(other)
                    if defect=='overlap':other['rawMips'][0]['file']='second.raw';(root/'second.raw').write_bytes(bytes(range(8)))
                with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e)
    def test_original_ref_hash_and_selector_types_fail_closed(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);v,e=fixture(root);ref=persist(root,v)
            for changed in (dict(ref,sha256='f'*64),dict(ref,schemaVersion=True),dict(ref,bytes=True)):
                with self.assertRaises(ValueError):p.read_capture(root,changed,e)
    def test_series_requires_all_eight_original_progressions(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);v,e=fixture(root);records=[]
            for i in range(8):
                x=copy.deepcopy(v);expected=copy.deepcopy(e);x['metadata']['repetition']=expected['repetition']=i
                x['copyReceipt']['fenceDomain']='late-copy-fence-'+format(i,'032x')
                o=x['originalFrameReceipt'];o['frameIndex']=expected['frameIndex']=i%3;o['submission']['submission']+=i;o['submission']['submittedFence']+=i;o['completedFence']+=i;o['presentedBefore']+=i;o['presentedAfter']+=i;expected['actualSubmission']=copy.deepcopy(o['submission'])
                records.append(p.read_capture(root,persist(root,x,f'snapshot-{i}.json'),expected))
            result=p.validate_series(records);self.assertEqual(len(result['comparisons']),8);self.assertTrue(all(r['semanticResidentBytesEqualToFirst'] for r in result['comparisons']))
            with self.assertRaises(ValueError):p.validate_series(records[:-1])
            shifted=copy.deepcopy(records);shifted[1]['original']['originalFrameReceipt']['submission']['submittedFence']=699
            with self.assertRaises(ValueError):p.validate_series(shifted)
            for defect in ('managedOwner','completedRegression','copyDomainReuse'):
                bad=copy.deepcopy(records)
                if defect=='managedOwner':bad[1]['original']['originalFrameReceipt']['managedOwnerThreadId']=2
                elif defect=='completedRegression':bad[0]['original']['originalFrameReceipt']['completedFence']=9999
                else:bad[1]['original']['copyReceipt']['fenceDomain']=bad[0]['original']['copyReceipt']['fenceDomain']
                with self.subTest(defect=defect),self.assertRaises(ValueError):p.validate_series(bad)
    def test_strings_and_escaped_wire_have_structural_bounds(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);v,e=fixture(root);v['resources'][0]['description']['semanticName']='x'*4097
            with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e)
        huge=encoded(dict(resources=[],intent={},metadata='x'*(1024*1024+1)))
        with self.assertRaises(ValueError):p.bounds(huge)
    def test_texture_source_schema_rejects_illegal_combinations(self):
        for fmt,color,w in [('R8Unorm','Srgb',4),('Bc7Unorm','Linear',5)]:
            with self.subTest(fmt=fmt),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=fixture(root);d=v['resources'][0]['description'];d.update(kind='texture',buffer=None,texture=dict(width=w,height=4,format=fmt,colorSpace=color,mipCount=1,deferUploadWait=False))
                with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e)

    def test_all_actual_wire_versions_reject_boolean_aliases(self):
        for path in ('schemaVersion','originalFrameReceipt.schemaVersion','originalFrameReceipt.deviceIdentity.schemaVersion','copyReceipt.schemaVersion'):
            with self.subTest(path=path),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=fixture(root);target=v;parts=path.split('.')
                for key in parts[:-1]:target=target[key]
                target[parts[-1]]=True
                with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e)
    def test_copy_fence_domain_actual_guid_format_and_utf8_capacity(self):
        for domain in ('late-copy-fence-old','late-copy-fence-'+('g'*32),'late-copy-fence-'+('f'*33),'late-copy-fence-'+('x'*4096),True):
            with self.subTest(domain=str(domain)[:40]),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=fixture(root);v['copyReceipt']['fenceDomain']=domain
                with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e)
    @staticmethod
    def graph(root,value):
        def buffer(identity,size,offset):
            raw=bytes(size);file=f'resource-{identity}.raw';(root/file).write_bytes(raw)
            d=dict(localId=identity,semanticName='retained',kind='buffer',lifetimeAvailable=True,buffer=dict(sizeInBytes=size,memory='Upload'),texture=None,implicit=None,sourceOffset=0,observedBytes=size,rangeMeaning='retained only',
                mips=[dict(mipIndex=-1,width=0,height=0,rowCount=1,rowBytes=size,rowPitch=size,packedOffset=offset,paddedBytes=size,canonicalBytes=size,format='Buffer')])
            return dict(description=d,rawMips=[dict(file=file,sha256=sha256_bytes(raw),bytes=size,mipIndex=-1)])
        value['resources'].extend([buffer(2,112,512),buffer(3,144,1024),buffer(4,12,1536)])
        value['intent']['mainBatches']=[dict(order=0,meshKey='mesh',materialKey=None,partIndex=-1,instanceCount=1,instanceOffset=0,instanceStride=112,vertexStride=48,firstIndex=0,indexCount=3,indexFormat='ThirtyTwoBit',passMeaning='Retained only',
            resources=[dict(resourceId=2,role='captured-slot-instance-prefix'),dict(resourceId=3,role='mesh-vertex'),dict(resourceId=4,role='mesh-index')])]
    def test_required_batch_roles_kinds_and_positive_ranges_bind_actual_raw_prefix(self):
        for defect in ('positive','missing','duplicate','wrong-kind','material-missing-textures','instance-offset','instance-count','instance-stride','first-index','index-count','vertex-size'):
            with self.subTest(defect=defect),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=fixture(root);self.graph(root,v);batch=v['intent']['mainBatches'][0]
                if defect=='missing':batch['resources'].pop()
                elif defect=='duplicate':batch['resources'].append(copy.deepcopy(batch['resources'][0]))
                elif defect=='wrong-kind':
                    raw=b'\0';(root/'wrong-kind.raw').write_bytes(raw)
                    d=dict(localId=5,semanticName='actual-texture',kind='texture',lifetimeAvailable=True,buffer=None,texture=dict(width=1,height=1,format='R8Unorm',colorSpace='Linear',mipCount=1,deferUploadWait=False),implicit=None,sourceOffset=0,observedBytes=1,rangeMeaning='retained',
                        mips=[dict(mipIndex=0,width=1,height=1,rowCount=1,rowBytes=1,rowPitch=256,packedOffset=2048,paddedBytes=256,canonicalBytes=1,format='R8_UNorm')])
                    v['resources'].append(dict(description=d,rawMips=[dict(file='wrong-kind.raw',sha256=sha256_bytes(raw),bytes=1,mipIndex=0)]));batch['resources'][0]['resourceId']=5
                elif defect=='material-missing-textures':batch['materialKey']='actual-material'
                elif defect=='instance-offset':batch['instanceOffset']=1
                elif defect=='instance-count':batch['instanceCount']=2
                elif defect=='instance-stride':batch['instanceStride']=113
                elif defect=='first-index':batch['firstIndex']=1
                elif defect=='index-count':batch['indexCount']=4
                elif defect=='vertex-size':batch['vertexStride']=145
                if defect=='positive':self.assertEqual(p.read_capture(root,persist(root,v),e)['status'],'complete-late-resident-subset')
                else:
                    with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e)
    def test_terrain_shared_ib_lod_bounds_and_required_roles_are_not_labels(self):
        for defect in ('positive','range','lod','bounds','missing','duplicate'):
            with self.subTest(defect=defect),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=fixture(root);self.graph(root,v)
                raw=bytes(16);(root/'control.raw').write_bytes(raw)
                d=dict(localId=5,semanticName='control',kind='texture',lifetimeAvailable=True,buffer=None,texture=dict(width=4,height=4,format='R8Unorm',colorSpace='Linear',mipCount=1,deferUploadWait=False),implicit=None,sourceOffset=0,observedBytes=16,rangeMeaning='retained',
                    mips=[dict(mipIndex=0,width=4,height=4,rowCount=4,rowBytes=4,rowPitch=256,packedOffset=2048,paddedBytes=1024,canonicalBytes=16,format='R8_UNorm')])
                v['resources'].append(dict(description=d,rawMips=[dict(file='control.raw',sha256=sha256_bytes(raw),bytes=16,mipIndex=0)]))
                t=dict(order=0,chunkIndex=0,chunkX=0,chunkZ=0,boundsMinimum=dict(x=0,y=0,z=0),boundsMaximum=dict(x=1,y=1,z=1),storedMainLod=0,lodMeaning='Stored history',paletteKey=0,materialKeys=[],indexRanges=[dict(firstIndex=0,indexCount=3,step=1)],
                    resources=[dict(resourceId=3,role='vertex'),dict(resourceId=4,role='shared-indices'),dict(resourceId=5,role='control')]);v['intent']['terrainChunks']=[t]
                if defect=='range':t['indexRanges'][0]['firstIndex']=1
                elif defect=='lod':t['storedMainLod']=1
                elif defect=='bounds':t['boundsMinimum']['x']=2
                elif defect=='missing':t['resources'].pop()
                elif defect=='duplicate':t['resources'].append(copy.deepcopy(t['resources'][0]))
                if defect=='positive':self.assertEqual(p.read_capture(root,persist(root,v),e)['status'],'complete-late-resident-subset')
                else:
                    with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e)
    def test_implicit_prefix_one_actual_arena_alignment_capacity_epoch_and_slot(self):
        for defect in ('positive256','positive512','positiveMax','unaligned','oversize','wrong-capacity','duplicate','epoch','slot'):
            with self.subTest(defect=defect),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=fixture(root);size=512 if defect=='positive512' else 1048576 if defect=='positiveMax' else 257 if defect=='unaligned' else 1048832 if defect=='oversize' else 256
                raw=bytes(size);(root/'arena.raw').write_bytes(raw)
                d=dict(localId=2,semanticName='captured-slot-constant-prefix',kind='implicit-buffer',lifetimeAvailable=False,buffer=dict(sizeInBytes=1048576,memory='Upload'),texture=None,
                    implicit=dict(kind='captured-slot-shadow-vertex-constant-prefix',resourceGeneration=2,frameIndex=0,descriptor=None,rootParameter=None,textureRegister=None,constantByteOffset=0,textureRootParameter=None),
                    sourceOffset=0,observedBytes=size,rangeMeaning='Reserved/padding shader relevance unknown',mips=[dict(mipIndex=-1,width=0,height=0,rowCount=1,rowBytes=size,rowPitch=size,packedOffset=512,paddedBytes=size,canonicalBytes=size,format='Buffer')])
                v['resources'].append(dict(description=d,rawMips=[dict(file='arena.raw',sha256=sha256_bytes(raw),bytes=size,mipIndex=-1)]))
                if defect=='wrong-capacity':d['buffer']['sizeInBytes']=2097152
                elif defect=='epoch':d['implicit']['resourceGeneration']=3
                elif defect=='slot':d['implicit']['frameIndex']=1
                elif defect=='duplicate':
                    other=copy.deepcopy(v['resources'][-1]);other['description']['localId']=3;other['description']['mips'][0]['packedOffset']=1024;other['rawMips'][0]['file']='arena2.raw';(root/'arena2.raw').write_bytes(raw);v['resources'].append(other)
                if defect.startswith('positive'):self.assertEqual(p.read_capture(root,persist(root,v),e)['status'],'complete-late-resident-subset')
                else:
                    with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e)

if __name__=='__main__':unittest.main()
