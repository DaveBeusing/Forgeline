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


def v2_fixture(root,mode='RetainedAttachmentsOnly',resolution_scale=1):
    """Handcrafted synthetic wire; no renderer, native device or GPU is invoked."""
    v,e=fixture(root);v.update(schemaVersion=2,kind='reference-post-final-color-causal-witness',coverageMode=mode,
        opaqueCoverage='not-selected' if mode==p.MODES[0] else 'source-closed-effective-opaque-state',opaqueWitness=None)
    e.update(coverageMode=mode,rasterWidth=2,rasterHeight=2);v['originalFrameReceipt'].update(schemaVersion=2,coverageMode=mode,invocation=1,worldDepthAliasesRetainedFinalDepth=resolution_scale==1)
    profile=dict(requestedSettings=dict(quality='Medium',resolutionScale=resolution_scale),linearScene=True,resolutionScale=resolution_scale,shadows=dict(quality='Medium',enabled=True,resolution=2048))
    profile_raw=encoded(profile);v['metadata'].update(profileInputBytesBase64=base64.b64encode(profile_raw).decode(),profileFingerprint=sha256_bytes(profile_raw));v['metadata']['observedComposition']['profile']=profile
    recipe=json.loads(base64.b64decode(v['metadata']['recipeInputBytesBase64']));recipe['settings']=profile['requestedSettings'];recipe_raw=encoded(recipe)
    v['metadata'].update(recipeInputBytesBase64=base64.b64encode(recipe_raw).decode(),recipeFingerprint=sha256_bytes(recipe_raw))
    e.update(profileFingerprint=v['metadata']['profileFingerprint'],recipeFingerprint=v['metadata']['recipeFingerprint'],observedComposition=copy.deepcopy(v['metadata']['observedComposition']))
    v['coverage']['included']+=list(p.ATTACHMENTS)
    v['coverage']['excluded']=['opaque-state-not-selected','particles','HUD','skin-caches','inactive-private-caches','earlier-pass-scratch','entity-root-associations']
    cursor=512
    for index,role in enumerate(p.ATTACHMENTS):
        scene=index==0;width=height=2048 if index==1 else p.scaled_dimension(2,resolution_scale) if scene else 2
        row_bytes=width*(8 if scene else 4);pitch=((row_bytes+255)//256)*256;padded=pitch*height;cursor=((cursor+511)//512)*512
        raw=bytes(row_bytes*height);file=f'attachment-{index}.raw';(root/file).write_bytes(raw)
        fmt='R16G16B16A16_Float' if scene else 'D32_Float' if index==2 and resolution_scale<1 else 'R32_Typeless'
        attachment=dict(role=role,nativeFormat=fmt,canonicalFormat='R16G16B16A16_Float' if scene else 'R32_Float',depthViewFormat=None if scene else 'D32_Float',
            shaderViewFormat='R16G16B16A16_Float' if scene else 'R32_Float' if index==1 else None,originalState='DepthWrite' if index==2 else 'PixelShaderResource',
            width=width,height=height,capturedSlot=0,resourceGeneration=2,meaning='Synthetic CPU mip0 payload; other backing mips unobserved; not a GPU capture',
            nativeMipLevels=p.full_mip_levels(width,height) if index==2 else 1,observedMipSlice=0,observedMipLevels=1,observedSubresource=0)
        layout=dict(mipIndex=0,width=width,height=height,rowCount=height,rowBytes=row_bytes,rowPitch=pitch,packedOffset=cursor,paddedBytes=padded,canonicalBytes=len(raw),format=fmt)
        desc=dict(localId=index+2,semanticName=role,kind='retained-attachment',lifetimeAvailable=False,buffer=None,texture=None,implicit=None,sourceOffset=0,observedBytes=len(raw),rangeMeaning='Synthetic retained target',mips=[layout],attachment=attachment)
        v['resources'].append(dict(description=desc,rawMips=[dict(file=file,sha256=sha256_bytes(raw),bytes=len(raw),mipIndex=0)]))
        cursor+=padded
    if mode==p.MODES[1]:
        v['coverage']['included']+=['source-closed-effective-opaque-state']
        v['coverage']['excluded']=['shadow-draw-state','particle-draw-state','composition-draw-state','HUD','skin-caches','inactive-private-caches','earlier-pass-scratch','entity-root-associations']
        opaque_fixture(v)
    return v,e


def native_row(field,index=0,*words):
    words=list(words)+[0]*(8-len(words));return dict(field=field,index=index,**{name:words[i*2]|(words[i*2+1]<<32) for i,name in enumerate('abcd')})


def identity_row(number,kind,**changes):
    row=dict(id=number,kind=kind,lifetimeAvailable=kind in ('Buffer','Texture','Pipeline','RootSignature'),epoch=0,slot=0,descriptor=0,offset=0,bytes=0,stride=0);row.update(changes);return row


def event(opcode,epoch=0,provenance='Unknown',**payload):
    return dict(sequence=0,opcode=opcode,provenance=provenance,passEpoch=epoch,binding=None,viewport=None,draw=None,rootWords=None,**{'pass':None})|payload


def binding_event(binding,identity,slot=0,operands=(0,0,0,0),epoch=0,owner=None,opcode=None):
    return event(opcode or ('OwnerPostcondition' if owner else 'NativeReturn'),epoch,'SourceProvedOwnerPostcondition' if owner else 'SetterRequested' if opcode=='Request' else 'CacheHitDecision' if opcode=='CacheHit' else 'Unknown' if opcode=='NativeFailure' else 'CpuNativeCallReturn',
        binding=dict(binding=binding,slot=slot,identity=identity,**dict(zip('abcd',operands)),ownerSource=owner))


def resequence(w):
    for index,row in enumerate(w['events']):row['sequence']=index


def opaque_fixture(v):
    """A source-shaped synthetic graph, with explicit 64-byte view / 8-byte dump."""
    v['resources'][0]['description'].update(buffer=dict(sizeInBytes=64,memory='Upload'),causalIdentityId=3)
    for row,identity in zip(v['resources'][1:],(4,5,6)):row['description']['causalIdentityId']=identity
    metadata=[native_row('RootFlags'),native_row('RootParameter',0,1,1),native_row('RootConstants',0,0,0,8),
        native_row('PipelineHeader',0,1),native_row('OptionalShaderStages'),native_row('StreamOutput'),native_row('CachedPipelineState'),
        native_row('PrimitiveTopology',0,3),native_row('PipelineFlags'),native_row('SampleMask',0,2**32-1),native_row('DepthFormat',0,40),native_row('Sample',0,1),native_row('RenderTargetFormat',0,10)]
    metadata+=[native_row('BlendTarget',i) for i in range(17)]+[native_row('Rasterizer',0),native_row('Rasterizer',1),native_row('DepthStencil',0,1,1,3),native_row('DepthStencil',1),native_row('InputElement',0,0,8,0,6,0,0,0,0)]
    creations=[dict(id=1,metadata=metadata,semanticNamesUtf8Base64=base64.b64encode(b'POSITION').decode(),pipelineIdentity=1,rootIdentity=2,
        requiredState=dict(vertexSlots=1,textureSlots=0,rootParameters=1,depthTarget=True,retainedTargetTextureSlots=0),vertexBytes=1,vertexSha256=sha256_bytes(b'\1'),pixelBytes=1,pixelSha256=sha256_bytes(b'\2'),creationRole='Ordinary')]
    identities=[identity_row(1,'Pipeline'),identity_row(2,'RootSignature'),identity_row(3,'Buffer',epoch=1,descriptor=1,bytes=64),
        identity_row(4,'RetainedTarget',epoch=2),identity_row(5,'RetainedTarget',epoch=2,descriptor=500),identity_row(6,'RetainedTarget',epoch=2,slot=2**64-1)]
    bits=lambda x:struct.unpack('<I',struct.pack('<f',x))[0]
    events=[event('Reset'),binding_event('Pipeline',1),binding_event('RootSignature',2),binding_event('Topology',4),
        binding_event('VertexBuffer',3,operands=(0,64,12,0)),binding_event('IndexBuffer',3,operands=(0,64,4,0)),
        event('NativeReturn',provenance='CpuNativeCallReturn',viewport=dict(xBits=0,yBits=0,widthBits=bits(2),heightBits=bits(2),minimumDepthBits=0,maximumDepthBits=bits(1),ownerSource=None)),
        binding_event('Scissor',0,operands=(0,0,2,2)),event('PassBoundary',1,'SourceProvedOwnerPostcondition',**{'pass':{'pass':'OpaqueWorld','sourceBranchProved':True}}),
        binding_event('RenderTarget',4,epoch=1,owner='InitialTargets'),binding_event('DepthTarget',6,epoch=1,owner='InitialTargets'),
        event('RootWords',1,'CpuNativeCallReturn',rootWords=dict(parameter=0,offsetWords=0,wordCount=8,buffered=False,arenaIdentity=0,arenaSlot=0,resourceEpoch=0,sourceOffset=0,ownerSource=None)),
        event('DrawIndexed',1,'CpuNativeCallReturn',draw=dict(indexed=True,count=3,instances=1,start=0,baseVertex=0,startInstance=0))]
    raw=bytes(32);o=v['originalFrameReceipt']
    v['opaqueWitness']=dict(schemaVersion=2,kind='post-final-color-causal-witness',complete=True,evidenceScope='source-closed-cpu-effective-opaque-state-and-after-PNG-retained-targets; not GPU shader fetch proof',
        deviceSession='eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',managedOwnerThreadId=1,repetition=0,invocation=1,refusal='None',
        originalFrame=dict(capturedSlot=0,submission=o['submission']['submission'],submittedFence=o['submission']['submittedFence'],completedFence=o['completedFence'],deviceGeneration=1,resourceGeneration=2,presentedBefore=o['presentedBefore'],presentedAfter=o['presentedAfter']),
        events=events,rootWordCount=8,rootByteCount=32,rootLittleEndianBase64=base64.b64encode(raw).decode(),rootSha256=sha256_bytes(raw),creations=creations,identities=identities)
    resequence(v['opaqueWitness'])


def add_synthetic_table(root,v,retained):
    w=v['opaqueWitness'];c=w['creations'][0]
    packed=((max(m['packedOffset']+m['paddedBytes'] for row in v['resources'] for m in row['description']['mips'])+511)//512)*512
    if retained:
        c['creationRole']='DirectionalShadowReceiver';c['requiredState'].update(rootParameters=3,retainedTargetTextureSlots=1<<15)
        c['metadata'] += [native_row('RootParameter',1,2,5),native_row('RootDescriptor',1,1),native_row('RootParameter',2,0,5),native_row('DescriptorRange',0,2,0,1,15,0,2**32-1)]
        w['identities'] += [identity_row(7,'ImplicitArena',epoch=2,bytes=4096*256,stride=4),identity_row(8,'DescriptorHeap',epoch=1)]
        raw=bytes(256);file='implicit.raw';(root/file).write_bytes(raw)
        d=dict(localId=5,semanticName='captured-slot-constant-prefix',kind='implicit-buffer',lifetimeAvailable=False,buffer=dict(sizeInBytes=4096*256,memory='Upload'),texture=None,
            implicit=dict(kind='captured-slot-shadow-vertex-constant-prefix',resourceGeneration=2,frameIndex=0,descriptor=None,rootParameter=None,textureRegister=None,constantByteOffset=0,textureRootParameter=None),
            sourceOffset=0,observedBytes=256,rangeMeaning='Synthetic used prefix',mips=[dict(mipIndex=-1,width=0,height=0,rowCount=1,rowBytes=256,rowPitch=256,packedOffset=packed,paddedBytes=256,canonicalBytes=256,format='Buffer')],causalIdentityId=7)
        extra=[binding_event('RootParameter',7,slot=1,operands=(0,256,2,0),epoch=1,owner='ShadowReceiverImplicit'),
            event('RootWords',1,'SourceProvedOwnerPostcondition',rootWords=dict(parameter=1,offsetWords=8,wordCount=24,buffered=True,arenaIdentity=7,arenaSlot=0,resourceEpoch=2,sourceOffset=0,ownerSource='ShadowReceiverImplicit')),
            binding_event('DescriptorHeap',8,epoch=1,owner='ShadowReceiverImplicit'),binding_event('TextureTable',5,slot=15,operands=(500,2,8,1),epoch=1,owner='ShadowReceiverImplicit')]
        words=bytes(128);w.update(rootWordCount=32,rootByteCount=128,rootLittleEndianBase64=base64.b64encode(words).decode(),rootSha256=sha256_bytes(words))
    else:
        c['requiredState']['textureSlots']=1;c['metadata'] += [native_row('RootParameter',1,0,5),native_row('DescriptorRange',0,1,0,1,0,0,2**32-1)]
        w['identities'] += [identity_row(7,'Texture',epoch=1,descriptor=17,offset=4|(4<<32),stride=1|(1<<8)|(1<<16)),identity_row(8,'DescriptorHeap',epoch=1)]
        raw=bytes(64);file='texture.raw';(root/file).write_bytes(raw)
        d=dict(localId=5,semanticName='retained-material-base',kind='texture',lifetimeAvailable=True,buffer=None,texture=dict(width=4,height=4,format='Rgba8Unorm',colorSpace='Linear',mipCount=1,deferUploadWait=False),implicit=None,
            sourceOffset=0,observedBytes=64,rangeMeaning='Synthetic material',mips=[dict(mipIndex=0,width=4,height=4,rowCount=4,rowBytes=16,rowPitch=256,packedOffset=packed,paddedBytes=1024,canonicalBytes=64,format='R8G8B8A8_UNorm')],causalIdentityId=7)
        extra=[binding_event('DescriptorHeap',8,epoch=1),binding_event('TextureTable',7,slot=0,operands=(17,1,8,257),epoch=1)]
    v['resources'].append(dict(description=d,rawMips=[dict(file=file,sha256=sha256_bytes(raw),bytes=len(raw),mipIndex=-1 if retained else 0)]))
    w['events'][-1:-1]=extra;resequence(w)


class PostFrameSnapshotV2Tests(unittest.TestCase):
    def test_backing_mips_are_not_exported_mips_and_strict_selection_refuses_tampering(self):
        for width,height,levels in ((1,1,1),(67,41,7),(176,41,8),(1920,1080,11)):
            self.assertEqual(p.full_mip_levels(width,height),levels)
        for key,bad in (('nativeMipLevels',0),('nativeMipLevels',1),('nativeMipLevels',True),('nativeMipLevels',33),
                        ('observedMipSlice',1),('observedMipSlice',False),('observedMipLevels',2),('observedMipLevels',True),('observedSubresource',1)):
            with self.subTest(key=key,bad=bad),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=v2_fixture(root);v['resources'][3]['description']['attachment'][key]=bad
                with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e,snapshot_version=2)
        for key in ('nativeMipLevels','observedMipSlice','observedMipLevels','observedSubresource'):
            with self.subTest(missing=key),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=v2_fixture(root);del v['resources'][3]['description']['attachment'][key]
                with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e,snapshot_version=2)
        for index in (1,2):
            with self.subTest(role=index),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=v2_fixture(root);v['resources'][index]['description']['attachment']['nativeMipLevels']=2
                with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e,snapshot_version=2)

    def test_attachment_only_has_explicit_absence_and_independent_mode(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);v,e=v2_fixture(root);ref=persist(root,v);r=p.read_capture(root,ref,e,snapshot_version=2)
            self.assertEqual(r['status'],'complete-retained-attachments');self.assertIsNone(r['opaqueState']);self.assertEqual(set(r['semanticAttachments']),set(p.ATTACHMENTS));self.assertEqual(len(r['semanticResources']),1)
            for changes in ({'coverageMode':p.MODES[1]},{'coverageMode':False},{'coverageMode':0},{'coverageMode':'retainedattachmentsonly'}):
                with self.subTest(changes=changes),self.assertRaises(ValueError):p.read_capture(root,ref,e|changes,snapshot_version=2)
            missing=dict(e);del missing['coverageMode']
            with self.assertRaises(ValueError):p.read_capture(root,ref,missing,snapshot_version=2)
            with self.assertRaises(ValueError):p.read_capture(root,ref,e,snapshot_version=1)

    def test_independent_raster_and_effective_quality_size_all_targets(self):
        self.assertEqual(p.scaled_dimension(10,.1),2)  # exact float32 promotion, not decimal ceil(10*.1)
        self.assertEqual(p.scaled_dimension(2,.5),1)
        quality=dict(linearScene=True,resolutionScale=.5,shadows=dict(quality='Medium',enabled=True,resolution=2048))
        original=dict(width=1,height=1,worldDepthAliasesRetainedFinalDepth=False)
        same_size=p.attachment_dimensions(quality,dict(rasterWidth=1,rasterHeight=1),original)
        self.assertEqual(same_size[p.ATTACHMENTS[0]],same_size[p.ATTACHMENTS[2]])
        with self.assertRaises(ValueError):p.attachment_dimensions(quality,dict(rasterWidth=1,rasterHeight=1),original|dict(worldDepthAliasesRetainedFinalDepth=True))
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);v,e=v2_fixture(root,resolution_scale=.5);r=p.read_capture(root,persist(root,v),e,snapshot_version=2)
            self.assertEqual(r['semanticAttachments'][p.ATTACHMENTS[0]]['attachment']['width'],1)
            self.assertEqual(r['semanticAttachments'][p.ATTACHMENTS[2]]['attachment']['width'],2)
            self.assertFalse(v['originalFrameReceipt']['worldDepthAliasesRetainedFinalDepth'])
            for expected in (e|{'rasterWidth':3},e|{'rasterHeight':False}):
                with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),expected,snapshot_version=2)
            v['originalFrameReceipt']['worldDepthAliasesRetainedFinalDepth']=True
            with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e,snapshot_version=2)
        for defect in ('scene-size','shadow-size','final-size','shadow-quality','shadow-disabled','alias','linear','scale','missing-raster'):
            with self.subTest(defect=defect),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=v2_fixture(root);profile=json.loads(base64.b64decode(v['metadata']['profileInputBytesBase64']))
                if defect.endswith('-size'):
                    index={'scene-size':1,'shadow-size':2,'final-size':3}[defect];v['resources'][index]['description']['attachment']['width']+=1
                elif defect=='shadow-quality':profile['shadows']['resolution']=1024
                elif defect=='shadow-disabled':profile['shadows'].update(quality='Disabled',enabled=False,resolution=0)
                elif defect=='alias':v['originalFrameReceipt']['worldDepthAliasesRetainedFinalDepth']=False
                elif defect=='linear':profile['linearScene']=False
                elif defect=='scale':profile['resolutionScale']=.1
                else:del e['rasterWidth']
                if defect in ('shadow-quality','shadow-disabled','linear','scale'):
                    raw=encoded(profile);v['metadata'].update(profileInputBytesBase64=base64.b64encode(raw).decode(),profileFingerprint=sha256_bytes(raw));v['metadata']['observedComposition']['profile']=profile;e.update(profileFingerprint=sha256_bytes(raw),observedComposition=copy.deepcopy(v['metadata']['observedComposition']))
                with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e,snapshot_version=2)

    def test_attachment_and_invocation_shapes_refuse_independently(self):
        for defect in ('missing','duplicate','mode','opaque-empty','coverage','slot','generation','format','state','layout','causal','invocation','bool-slot'):
            with self.subTest(defect=defect),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=v2_fixture(root);d=v['resources'][1]['description'];a=d['attachment']
                if defect=='missing':v['resources'].pop()
                elif defect=='duplicate':v['resources'][2]['description']['attachment']['role']=p.ATTACHMENTS[0]
                elif defect=='mode':v['coverageMode']=p.MODES[1]
                elif defect=='opaque-empty':v['opaqueWitness']={}
                elif defect=='coverage':v['opaqueCoverage']='complete'
                elif defect=='slot':a['capturedSlot']=1
                elif defect=='bool-slot':a['capturedSlot']=False
                elif defect=='generation':a['resourceGeneration']=1
                elif defect=='format':a['canonicalFormat']='R32_Float'
                elif defect=='state':a['originalState']='DepthWrite'
                elif defect=='layout':d['mips'][0]['rowPitch']=255
                elif defect=='causal':d['causalIdentityId']=1
                elif defect=='invocation':v['originalFrameReceipt']['invocation']=2
                with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e,snapshot_version=2)

    def test_role_specific_formats_follow_independent_source_branch_with_unchanged_raw_bytes(self):
        for branch in ('shadow','scaled-final','unscaled-atmosphere'):
            with self.subTest(branch=branch),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=v2_fixture(root,resolution_scale=.5 if branch=='scaled-final' else 1)
                if branch=='unscaled-atmosphere':
                    v['metadata']['observedComposition']['atmosphere']=dict(enabled=True);e['observedComposition']=copy.deepcopy(v['metadata']['observedComposition'])
                self.assertEqual(p.read_capture(root,persist(root,v),e,snapshot_version=2)['status'],'complete-retained-attachments')
                resource=v['resources'][2 if branch=='shadow' else 3];before=resource['rawMips'][0]['sha256'];file=root/resource['rawMips'][0]['file']
                tampered='R32_Typeless' if branch=='scaled-final' else 'D32_Float'
                resource['description']['attachment']['nativeFormat']=tampered;resource['description']['mips'][0]['format']=tampered
                with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e,snapshot_version=2)
                self.assertEqual(sha256_bytes(file.read_bytes()),before)
        for enabled in (None,False):
            for fmt in ('R32_Typeless','D32_Float'):
                with self.subTest(enabled=enabled,fmt=fmt),tempfile.TemporaryDirectory() as folder:
                    root=Path(folder);v,e=v2_fixture(root)
                    v['metadata']['observedComposition']['atmosphere']={} if enabled is None else dict(enabled=False);e['observedComposition']=copy.deepcopy(v['metadata']['observedComposition'])
                    d=v['resources'][3]['description'];d['attachment']['nativeFormat']=d['mips'][0]['format']=fmt
                    self.assertEqual(p.read_capture(root,persist(root,v),e,snapshot_version=2)['status'],'complete-retained-attachments')
        self.assertEqual(p.attachment_formats(p.ATTACHMENTS[2],dict(worldDepthAliasesRetainedFinalDepth=False),dict(atmosphere={})),('D32_Float',))
        # Even coincident rounded dimensions use the actual scaled-scene allocation predicate.
        quality=dict(linearScene=True,resolutionScale=.5,shadows=dict(quality='Medium',enabled=True,resolution=2048))
        original=dict(width=1,height=1,worldDepthAliasesRetainedFinalDepth=False)
        dims=p.attachment_dimensions(quality,dict(rasterWidth=1,rasterHeight=1),original);self.assertEqual(dims[p.ATTACHMENTS[0]],dims[p.ATTACHMENTS[2]])
        self.assertEqual(p.attachment_formats(p.ATTACHMENTS[2],original,dict(atmosphere=dict(enabled=True))),('D32_Float',))

    def test_opaque_replay_retains_partial_native_view_without_inventing_fetch_coverage(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);v,e=v2_fixture(root,p.MODES[1]);r=p.read_capture(root,persist(root,v),e,snapshot_version=2)
            self.assertEqual(r['status'],'complete-opaque-state-and-retained-attachments');self.assertEqual(len(r['opaqueState']['draws']),1)
            for view in r['opaqueState']['viewCoverage']:
                self.assertEqual(view['nativeBytes'],64);self.assertEqual(view['dumpBytes'],8);self.assertEqual(view['intersectionBytes'],8)
                self.assertEqual(view['uncapturedRanges'],[dict(offset=8,bytes=56)]);self.assertFalse(view['completeViewCaptured']);self.assertFalse(view['shaderFetchCoverageProved'])

    def test_opaque_closed_graph_mutations_refuse(self):
        for defect in ('absent','refused','pure','tuple','session','sequence','provenance','epoch','unknown-op','mask','semantic','root-hash','root-bool','wrong-id','missing-late','capacity','viewport-nan','catalog-orphan'):
            with self.subTest(defect=defect),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=v2_fixture(root,p.MODES[1]);w=v['opaqueWitness']
                if defect=='absent':v['opaqueWitness']=None
                elif defect=='refused':w['refusal']='InvalidTransition'
                elif defect=='pure':w['complete']=False;w['evidenceScope']='pure-contract-unhooked-no-late-copy-proof'
                elif defect=='tuple':w['originalFrame']['submission']+=1
                elif defect=='session':w['deviceSession']='11111111-1111-1111-1111-111111111111'
                elif defect=='sequence':w['events'][2]['sequence']=0
                elif defect=='provenance':w['events'][2]['provenance']='SetterRequested'
                elif defect=='epoch':w['events'][-1]['passEpoch']=0
                elif defect=='unknown-op':w['events'][2]['opcode']='GpuExecuted'
                elif defect=='mask':w['creations'][0]['requiredState']['rootParameters']=0
                elif defect=='semantic':w['creations'][0]['semanticNamesUtf8Base64']=base64.b64encode(b'POS\0TION').decode()
                elif defect=='root-hash':w['rootSha256']='f'*64
                elif defect=='root-bool':w['events'][-2]['rootWords']['arenaSlot']=False
                elif defect=='wrong-id':w['events'][4]['binding']['identity']=1
                elif defect=='missing-late':del v['resources'][0]['description']['causalIdentityId']
                elif defect=='capacity':w['events'][4]['binding']['b']=65
                elif defect=='viewport-nan':w['events'][6]['viewport']['widthBits']=0x7fc00000
                elif defect=='catalog-orphan':w['creations'][0]['metadata'].append(native_row('RootConstants',2,0,0,8))
                with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e,snapshot_version=2)

    def test_opaque_requests_and_cache_hits_preserve_effective_catalog_and_count(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);v,e=v2_fixture(root,p.MODES[1]);w=v['opaqueWitness'];second=copy.deepcopy(w['creations'][0]);second.update(id=2,pipelineIdentity=7,rootIdentity=8,vertexSha256='d'*64)
            w['creations'].append(second);w['identities'] += [identity_row(7,'Pipeline'),identity_row(8,'RootSignature')]
            w['events'][-1:-1]=[binding_event('Pipeline',7,epoch=1,opcode='Request'),binding_event('Pipeline',0,epoch=1,opcode='CacheHit'),binding_event('RootSignature',8,epoch=1,opcode='Request'),binding_event('RootSignature',0,epoch=1,opcode='CacheHit'),binding_event('RootParameter',0,operands=(0,4,0,0),epoch=1,opcode='Request')]
            resequence(w);r=p.read_capture(root,persist(root,v),e,snapshot_version=2)
            self.assertEqual(r['opaqueState']['draws'][0]['pipeline']['vertexSha256'],w['creations'][0]['vertexSha256'])
            self.assertEqual(r['opaqueState']['draws'][0]['roots']['0']['words'],8)
            w['events'][-1:-1]=[binding_event('Pipeline',0,epoch=1,opcode='NativeFailure')];resequence(w)
            with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e,snapshot_version=2)

    def test_opaque_short_root_refuses_but_full_write_before_draw_recovers(self):
        for recover in (False,True):
            with self.subTest(recover=recover),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=v2_fixture(root,p.MODES[1]);w=v['opaqueWitness'];first=w['events'][-2];first['rootWords']['wordCount']=4
                count=4
                if recover:
                    full=copy.deepcopy(first);full['rootWords'].update(offsetWords=4,wordCount=8);w['events'][-1:-1]=[full];count=12
                raw=bytes(count*4);w.update(rootWordCount=count,rootByteCount=len(raw),rootLittleEndianBase64=base64.b64encode(raw).decode(),rootSha256=sha256_bytes(raw));resequence(w)
                if recover:self.assertEqual(p.read_capture(root,persist(root,v),e,snapshot_version=2)['opaqueState']['draws'][0]['roots']['0']['words'],8)
                else:
                    with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e,snapshot_version=2)

    def test_ordinary_and_retained_tables_join_actual_effective_root_ordinal(self):
        for retained in (False,True):
            with self.subTest(retained=retained),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);v,e=v2_fixture(root,p.MODES[1]);add_synthetic_table(root,v,retained)
                self.assertEqual(len(p.read_capture(root,persist(root,v),e,snapshot_version=2)['opaqueState']['draws']),1)
                table=next(x['binding'] for x in reversed(v['opaqueWitness']['events']) if x['binding'] is not None and x['binding']['binding']=='TextureTable')
                table['b']-=1
                with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e,snapshot_version=2)

    def test_buffered_kind_and_source_payload_cannot_be_replaced_by_immediate_words(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);v,e=v2_fixture(root,p.MODES[1]);c=v['opaqueWitness']['creations'][0]
            c['metadata']=[native_row('RootParameter',0,2,1) if row['field']=='RootParameter' else native_row('RootDescriptor',0,0) if row['field']=='RootConstants' else row for row in c['metadata']]
            with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e,snapshot_version=2)

    def test_v2_series_compares_roles_separately_and_allows_real_attachment_difference(self):
        for mode in p.MODES:
            with self.subTest(mode=mode),tempfile.TemporaryDirectory() as folder:
                root=Path(folder);base,e=v2_fixture(root,mode);records=[]
                for repetition in range(8):
                    v=copy.deepcopy(base);expected=copy.deepcopy(e);v['metadata']['repetition']=expected['repetition']=repetition
                    o=v['originalFrameReceipt'];o['invocation']=repetition+1;o['submission']['submission']+=repetition;o['submission']['submittedFence']+=repetition;o['completedFence']+=repetition;o['presentedBefore']+=repetition;o['presentedAfter']+=repetition;expected['actualSubmission']=copy.deepcopy(o['submission'])
                    v['copyReceipt']['fenceDomain']='late-copy-fence-'+format(repetition,'032x')
                    for resource in v['resources']:
                        for raw in resource['rawMips']:
                            content=(root/raw['file']).read_bytes()
                            if repetition==1 and resource['description'].get('attachment',{}).get('role')==p.ATTACHMENTS[0]:content=b'\1'+content[1:]
                            name=f'repetition-{repetition}-'+raw['file'];(root/name).write_bytes(content);raw.update(file=name,bytes=len(content),sha256=sha256_bytes(content))
                    if mode==p.MODES[1]:
                        w=v['opaqueWitness'];w.update(repetition=repetition,invocation=repetition+1)
                        for key in ('submission','submittedFence','completedFence','presentedBefore','presentedAfter'):w['originalFrame'][key]+=repetition
                    records.append(p.read_capture(root,persist(root,v,f'v2-{repetition}.json'),expected,snapshot_version=2))
                result=p.validate_series(records);self.assertEqual(len(result['comparisons']),8);self.assertFalse(result['qualified']);self.assertFalse(result['wave2Ready'])
                changed=result['comparisons'][1];self.assertFalse(changed['retainedAttachmentBytesEqualToFirst'][p.ATTACHMENTS[0]])
                self.assertTrue(changed['retainedAttachmentBytesEqualToFirst'][p.ATTACHMENTS[1]]);self.assertTrue(changed['retainedAttachmentBytesEqualToFirst'][p.ATTACHMENTS[2]])
                self.assertTrue(changed['semanticResidentBytesEqualToFirst'])
                if mode==p.MODES[0]:self.assertIsNone(changed['sourceClosedOpaqueStateEqualToFirst'])
                else:self.assertTrue(changed['sourceClosedOpaqueStateEqualToFirst'])
                mixed=copy.deepcopy(records);mixed[1]['coverageMode']=p.MODES[1] if mode==p.MODES[0] else p.MODES[0]
                with self.assertRaises(ValueError):p.validate_series(mixed)

    def test_actual_csharp_attachment_fixture_round_trips_when_explicitly_provided(self):
        import os
        supplied=os.environ.get('FORGELINE_POST_FRAME_V2_TYPED_INPUT')
        if supplied is None:self.skipTest('Explicit source-pinned synthetic C# fixture not supplied')
        root=Path(supplied);ref=json.loads((root/'producer-reference.json').read_text());value=json.loads((root/ref['file']).read_text());m=value['metadata'];o=value['originalFrameReceipt']
        expected={k:copy.deepcopy(m[k]) for k in p.EXPECTED.split() if k in m};expected.update(deviceIdentity=o['deviceIdentity'],managedOwnerThreadId=o['managedOwnerThreadId'],frameIndex=o['frameIndex'],actualSubmission=o['submission'],coverageMode=p.MODES[0],rasterWidth=2,rasterHeight=2)
        result=p.read_capture(root,ref,expected,snapshot_version=2);self.assertEqual(result['status'],'complete-retained-attachments');self.assertEqual(result['canonicalBytes'],16_777_720)

    def test_actual_csharp_pure_wire_is_incomplete_and_only_synthetic_completion_can_exercise_replay(self):
        import os
        supplied=os.environ.get('FORGELINE_POST_FRAME_V2_TYPED_OPAQUE_INPUT')
        if supplied is None:self.skipTest('Explicit source-pinned C# PURE wire not supplied')
        directory=Path(supplied);raw=(directory/'opaque-pure-wire.json').read_bytes();wire=json.loads(raw);provenance=json.loads((directory/'synthetic-provenance.json').read_text())
        self.assertFalse(wire['complete']);self.assertIn('pure-contract-unhooked',wire['evidenceScope']);self.assertTrue(provenance['synthetic']);self.assertFalse(provenance['actualNativeCompletionProved']);self.assertFalse(provenance['nativeDeviceCreated'])
        self.assertEqual(len(p.opaque_catalog(wire['creations'],{i['id']:i for i in wire['identities']})),1)
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);v,e=v2_fixture(root,p.MODES[1]);v['opaqueWitness']=copy.deepcopy(wire)
            with self.assertRaises(ValueError):p.read_capture(root,persist(root,v),e,snapshot_version=2)
            # EXPLICIT SYNTHETIC TEST MUTATION ONLY. Original C# file remains incomplete.
            # These assertions exercise codec/replay, never actual copy/native completion.
            w=v['opaqueWitness'];w.update(complete=True,evidenceScope='source-closed-cpu-effective-opaque-state-and-after-PNG-retained-targets; not GPU shader fetch proof')
            o=v['originalFrameReceipt'];o['deviceIdentity']['sessionId']=w['deviceSession'].replace('-','');o['managedOwnerThreadId']=w['managedOwnerThreadId']
            f=w['originalFrame'];o.update(frameIndex=f['capturedSlot'],completedFence=f['completedFence'],presentedBefore=f['presentedBefore'],presentedAfter=f['presentedAfter'])
            o['submission']=dict(submission=f['submission'],submittedFence=f['submittedFence'],generation=f['deviceGeneration'],resourceGeneration=f['resourceGeneration'])
            e.update(deviceIdentity=copy.deepcopy(o['deviceIdentity']),managedOwnerThreadId=o['managedOwnerThreadId'],frameIndex=o['frameIndex'],actualSubmission=copy.deepcopy(o['submission']))
            self.assertEqual(len(p.read_capture(root,persist(root,v),e,snapshot_version=2)['opaqueState']['draws']),1)
        self.assertEqual((directory/'opaque-pure-wire.json').read_bytes(),raw)


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
