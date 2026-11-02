"""Synthetic CPU journal contracts using the existing five-stage file fixture."""
import base64
import copy
import json
import unittest
from baseline_contracts import canonical, sha256_bytes
from stage_diagnostics import compare_series
import test_stage_diagnostics as stage_tests
from world_binding_journal import EVENT_FIELDS, MAX_DEFINITIONS, MAX_DRAWS, MAX_EVENTS, read_journal, reduce_events, validate_series, compare_lanes


class WorldBindingJournalTests(unittest.TestCase):
    def setUp(self):
        self.stages = stage_tests.StageDiagnosticsTests()
        self.stages.setUp()
        self.addCleanup(self.stages.doCleanups)
        self.root, self.refs = self.stages.root, self.stages.refs
        for i in range(8):
            self.put(i, self.body(self.stages.record(i)))

    @staticmethod
    def event(kind, **values):
        return dict({key: None for key in EVENT_FIELDS}, kind=kind, **values)

    @staticmethod
    def description():
        return dict(vertexShaderSha256='a'*64, pixelShaderSha256='b'*64, vertexEntryPoint='VSMain', pixelEntryPoint='PSMain',
            vertexSourceName='vertex.hlsl', pixelSourceName='pixel.hlsl',
            vertexElements=[dict(semanticName='POSITION', semanticIndex=0, format='Float3', offsetInBytes=0, inputSlot=0, inputRate='PerVertex', instanceStepRate=0)],
            vertexRootConstantCount=1, pixelTextureCount=1, primitiveTopology='TriangleList', cullMode='None',
            depthEnabled=True, depthAlwaysPass=False, alphaBlendEnabled=False, sceneDepthReceiver=False,
            vertexConstantsInBuffer=False, directionalShadowReceiver=False, targetFormat='Rgba16Float')

    def body(self, stage):
        constants = b'\x00\x00\x80\x3f'
        definitions = [
            dict(id=1, kind='pipeline', buffer=None, texture=None, descriptor=None, pipeline=self.description()),
            dict(id=2, kind='buffer', buffer=dict(sizeInBytes=128, memory='Upload'), texture=None, descriptor=None, pipeline=None),
            dict(id=3, kind='texture', buffer=None, texture=dict(width=4, height=4, format='Rgba8Unorm', colorSpace='Linear', mipCount=1, deferUploadWait=False), descriptor=9, pipeline=None)]
        events = [self.event('Viewport', viewport=[0,0,2,2]), self.event('Scissor', scissor=[0,0,2,2]),
            self.event('Pipeline', identityId=1, emitted=True),
            self.event('VertexBuffer', identityId=2, slot=0, stride=12, offset=0, rangeBytes=128),
            self.event('IndexBuffer', identityId=2, offset=0, rangeBytes=128, indexFormat='SixteenBit'),
            self.event('Texture', identityId=3, slot=0, emitted=True),
            self.event('Constants', constantBytesBase64=base64.b64encode(constants).decode(), constantSha256=sha256_bytes(constants), constantMode='root'),
            self.event('DrawIndexedInstanced', drawArguments=[6,1,0,0,0], drawOrdinal=0)]
        for i, event in enumerate(events): event['sequence'] = i
        journal = dict(schemaVersion=1, kind='opaque-world-binding-journal', complete=True, failure=None,
            deviceGeneration=stage['submission']['generation'], resourceGeneration=stage['submission']['resourceGeneration'],
            deviceSession=stage['deviceIdentity']['sessionId'], frameIndex=stage['frameIndex'], submission=stage['submission'],
            completedFence=stage['completedFence'], presentedBefore=stage['presentedBefore'], presentedAfter=stage['presentedAfter'],
            drawCount=1, accountedPayloadBytes=4096, definitions=definitions, events=events)
        profile = canonical(stage['effectiveProfile'])
        return dict(schemaVersion=1, kind='reference-world-binding-journal', qualified=False,
            **{k:stage[k] for k in ('sourceSha','producer','runtimeFingerprint','recipeFingerprint','recipeInputBytesBase64','repetition','camera','deviceIdentity')},
            profileDomain='effective-quality-profile', profileInputBytesBase64=base64.b64encode(profile).decode(),
            profileFingerprint=sha256_bytes(profile), bufferCount=3, journal=journal,
            scope='Synthetic producer-shaped CPU contract; no target execution or GPU content proof.')

    def put(self, i, body):
        name=f'{i}-world-bindings.json'; raw=canonical(body); (self.root/name).write_bytes(raw)
        stage=self.stages.record(i)
        stage.update(worldBindingJournalVersion=1, worldBindingJournal=dict(file=name, sha256=sha256_bytes(raw), bytes=len(raw), schemaVersion=1))
        self.stages.save(i,stage)

    def get(self, i):
        return json.loads((self.root/f'{i}-world-bindings.json').read_bytes())

    def test_all_eight_original_refs_and_reducer_shapes(self):
        result=validate_series(self.root,self.refs)
        self.assertTrue(result['available']);self.assertFalse(result['qualified'])
        self.assertEqual(len(result['comparisons']),8)
        self.assertTrue(all(c['semanticDescriptionsEqualToFirst'] for c in result['comparisons']))
        self.assertEqual(compare_series(self.root,self.refs,world_journal_version=1)['status'],'equal')

    def test_missing_selected_is_not_historical_downgrade(self):
        for i in range(8):
            stage=self.stages.record(i);stage.pop('worldBindingJournal');stage.pop('worldBindingJournalVersion');self.stages.save(i,stage)
        self.assertFalse(validate_series(self.root,self.refs,None)['available'])
        self.assertEqual(compare_series(self.root,self.refs)['status'],'equal')
        with self.assertRaises(ValueError):validate_series(self.root,self.refs,1)
        with self.assertRaises(ValueError):compare_series(self.root,self.refs,world_journal_version=1)

    def test_native_view_single_and_texture_source_ranges(self):
        original = self.get(0)['journal']
        for event_index in (3, 4):
            valid = copy.deepcopy(original)
            buffer = copy.deepcopy(valid['definitions'][1])
            buffer.update(id=4); buffer['buffer']['sizeInBytes'] = 2**32-1
            valid['definitions'].append(buffer)
            valid['events'][event_index].update(identityId=4, rangeBytes=2**32-1)
            reduce_events(valid)
            invalid = copy.deepcopy(valid)
            invalid['definitions'][-1]['buffer']['sizeInBytes'] = 2**32
            invalid['events'][event_index]['rangeBytes'] = 2**32
            with self.assertRaises(ValueError): reduce_events(invalid)
        from world_binding_journal import SINGLE_MAX
        valid = copy.deepcopy(original); valid['events'][0]['viewport'][0] = SINGLE_MAX
        reduce_events(valid)
        for value in (3.4028235e38, -3.4028235e38):
            valid = copy.deepcopy(original); valid['events'][0]['viewport'][0] = value
            reduce_events(valid)
        for value in (SINGLE_MAX * 2, -SINGLE_MAX * 2):
            invalid = copy.deepcopy(original); invalid['events'][0]['viewport'][0] = value
            with self.assertRaises(ValueError): reduce_events(invalid)
        for dimension in (2, 3):
            invalid = copy.deepcopy(original); invalid['events'][0]['viewport'][dimension] = 1e-100
            with self.assertRaises(ValueError): reduce_events(invalid)
        for changes in (dict(format='R8Unorm', colorSpace='Srgb'),
                        dict(format='Bc7Unorm', width=5), dict(format='Bc7Unorm', height=6)):
            invalid = copy.deepcopy(original); invalid['definitions'][2]['texture'].update(changes)
            with self.assertRaises(ValueError): reduce_events(invalid)
        for changes in (dict(format='R8Unorm', colorSpace='Linear'), dict(format='Bc7Unorm', colorSpace='Srgb')):
            valid = copy.deepcopy(original); valid['definitions'][2]['texture'].update(changes)
            reduce_events(valid)

    def test_sidecar_hash_path_and_version_cannot_be_recovered(self):
        original=self.stages.record(0)
        for mutate in (lambda s:s['worldBindingJournal'].update(file='../0-world-bindings.json'),
                       lambda s:s['worldBindingJournal'].update(sha256='0'*64),
                       lambda s:s.update(worldBindingJournalVersion=True),
                       lambda s:s['worldBindingJournal'].update(bytes=True)):
            stage=copy.deepcopy(original);mutate(stage);self.stages.save(0,stage)
            with self.assertRaises(ValueError):read_journal(self.root,self.refs[0])
        self.stages.save(0,original)

    def test_original_source_tuple_profile_and_nullable_state_tamper_rejects(self):
        original=self.get(1)
        mutations=(lambda b:b.update(sourceSha='f'*40),lambda b:b['journal'].update(frameIndex=2),
                   lambda b:b['journal']['submission'].update(submittedFence=1),
                   lambda b:b['journal'].update(deviceSession='f'*32),
                   lambda b:b.update(profileDomain='full-composition'),
                   lambda b:b['journal'].update(complete=False,failure='original failure',submission=None,completedFence=None,presentedAfter=None),
                   lambda b:b['journal']['events'][6].update(constantSha256='0'*64),
                   lambda b:b['journal']['definitions'][0]['pipeline'].update(vertexRootConstantCount=True),
                   lambda b:b['journal']['definitions'][2]['texture'].update(mipCount=True),
                   lambda b:b['journal']['definitions'][0]['pipeline'].update(vertexSourceName='x'*513))
        for mutate in mutations:
            body=copy.deepcopy(original);mutate(body);self.put(1,body)
            with self.assertRaises((ValueError,KeyError)):read_journal(self.root,self.refs[1])
        self.put(1,original)

    def test_inherited_ia_survives_root_reset_but_root_state_must_be_rebound(self):
        body=self.get(0);events=body['journal']['events'];constant=copy.deepcopy(events[6])
        events.extend([self.event('InvalidateRoot'),self.event('Pipeline',identityId=1,emitted=True),
                       self.event('Texture',identityId=3,slot=0,emitted=True),constant,
                       self.event('DrawIndexedInstanced',drawArguments=[9,2,4,-1,3],drawOrdinal=1)])
        for i,event in enumerate(events):event['sequence']=i
        body['journal']['drawCount']=2;self.put(0,body)
        item=read_journal(self.root,self.refs[0]);self.assertEqual(len(item['draws']),2)
        self.assertEqual(item['draws'][1]['state']['vertices'],item['draws'][0]['state']['vertices'])
        body['journal']['events'].pop(-3)
        for i,event in enumerate(events):event['sequence']=i
        self.put(0,body)
        with self.assertRaises(ValueError):read_journal(self.root,self.refs[0])

    def test_false_cache_hit_and_unknown_event_do_not_become_complete(self):
        for mutate in (lambda b:b['journal']['events'][5].update(emitted=False),
                       lambda b:b['journal']['events'][2].update(emitted=False),
                       lambda b:b['journal']['events'][4].update(kind='Unknown'),
                       lambda b:b['journal']['events'][4].update(sequence=0)):
            body=self.get(0);mutate(body)
            with self.assertRaises(ValueError):reduce_events(body['journal'])

    def test_same_id_redefinition_rejected_but_same_slot_swap_is_retained(self):
        original=self.get(1);body=copy.deepcopy(original)
        body['journal']['definitions'][1]['buffer']['sizeInBytes']=256
        body['journal']['events'][3]['rangeBytes']=256;body['journal']['events'][4]['rangeBytes']=256
        self.put(1,body)
        with self.assertRaises(ValueError):validate_series(self.root,self.refs)
        self.put(1,original)
        body=self.get(3);body['journal']['definitions'][1]['id']=99
        for event in body['journal']['events']:
            if event['identityId']==2:event['identityId']=99
        self.put(3,body)
        result=validate_series(self.root,self.refs)
        self.assertTrue(result['comparisons'][3]['semanticDescriptionsEqualToFirst'])
        self.assertNotEqual(result['comparisons'][0]['localDrawBindings'],result['comparisons'][3]['localDrawBindings'])
        self.assertEqual(result['comparisons'][3]['frameIndex'],result['comparisons'][0]['frameIndex'])
        self.assertFalse(result['comparisons'][3]['localBindingsEqualToPreviousSameSlot'])

    def test_all_structural_bounds_reject_without_trimming(self):
        original=self.get(0)['journal']
        for key,value in (('drawCount',MAX_DRAWS+1),('events',[original['events'][0]]*(MAX_EVENTS+1)),
                          ('definitions',[original['definitions'][0]]*(MAX_DEFINITIONS+1)),
                          ('accountedPayloadBytes',32*1024*1024+1)):
            body=copy.deepcopy(original);body[key]=value
            with self.assertRaises(ValueError):reduce_events(body)

    def test_implicit_shadow_is_epoch_slot_descriptor_not_resource_id(self):
        body=self.get(0);journal=body['journal'];p=journal['definitions'][0]['pipeline']
        p.update(vertexConstantsInBuffer=True,directionalShadowReceiver=True)
        frame=dict(kind='directional-shadow-frame',resourceGeneration=1,frameIndex=0,descriptor=None,rootParameter=None,textureRegister=None,constantByteOffset=0,textureRootParameter=None)
        receiver=dict(kind='directional-shadow-receiver',resourceGeneration=1,frameIndex=0,descriptor=23,rootParameter=2,textureRegister=15,constantByteOffset=0,textureRootParameter=3)
        buffered=dict(kind='buffered-vertex-constants',resourceGeneration=1,frameIndex=0,descriptor=None,rootParameter=0,textureRegister=None,constantByteOffset=256,textureRootParameter=None)
        raw=bytes(96)
        journal['events'].insert(2,self.event('ShadowFrame',constantBytesBase64=base64.b64encode(raw).decode(),constantSha256=sha256_bytes(raw),constantMode='implicit-shadow-frame',implicit=frame))
        journal['events'].insert(4,self.event('ShadowReceiver',implicit=receiver,emitted=True))
        journal['events'][-2].update(constantMode='buffered',implicit=buffered)
        for i,event in enumerate(journal['events']):event['sequence']=i
        self.put(0,body)
        self.assertEqual(read_journal(self.root,self.refs[0])['draws'][0]['state']['shadow']['descriptor'],23)
        body['journal']['events'][4]['implicit']['frameIndex']=1;self.put(0,body)
        with self.assertRaises(ValueError):read_journal(self.root,self.refs[0])

    def test_cross_lane_ids_and_descriptors_are_not_equalized_as_assets(self):
        host=self.stages.host_records()
        for i,ref in enumerate(host):
            stage=json.loads((self.root/ref['path']).read_bytes());body=self.body(stage)
            for definition in body['journal']['definitions']:definition['id']+=100
            body['journal']['definitions'][2]['descriptor']=104
            for event in body['journal']['events']:
                if event['identityId'] is not None:event['identityId']+=100
            name=f'host-{i}-world-bindings.json';raw=canonical(body);(self.root/name).write_bytes(raw)
            stage.update(worldBindingJournalVersion=1,worldBindingJournal=dict(file=name,sha256=sha256_bytes(raw),bytes=len(raw),schemaVersion=1))
            raw=canonical(stage);(self.root/ref['path']).write_bytes(raw);ref['sha256']=sha256_bytes(raw)
        result=compare_lanes(self.root,self.refs,host)
        self.assertTrue(all(c['semanticDescriptionsEqual'] for c in result['comparisons']))
        self.assertFalse(result['qualified'])


    def test_actual_wire_spans_preserve_escaping_and_nested_journal_text(self):
        from world_binding_journal import wire_bounds, ENVELOPE_BYTES
        raw=b'{"note":"escaped \\"journal\\": text","journal":{"definitions":[],"events":[]},"other":{"journal":123}}'
        journal_bytes,envelope_bytes=wire_bounds(raw)
        self.assertEqual(journal_bytes,len(b'{"definitions":[],"events":[]}'))
        self.assertEqual(envelope_bytes,len(raw)-journal_bytes+4)
        with self.assertRaises(ValueError):
            wire_bounds(canonical(dict(journal={},note='x'*ENVELOPE_BYTES)))

    def test_actual_journal_wire_bound_is_not_only_a_claimed_counter(self):
        from world_binding_journal import wire_bounds, JOURNAL_BYTES
        raw=canonical(dict(journal=dict(oversized='x'*JOURNAL_BYTES)))
        with self.assertRaises(ValueError):wire_bounds(raw)


    def test_replayed_capture_tuple_cannot_pass_standalone_selected_series(self):
        stage=self.stages.record(1);first=self.stages.record(0)
        stage['submission']=copy.deepcopy(first['submission']);self.stages.save(1,stage)
        body=self.get(1);body['journal']['submission']=copy.deepcopy(stage['submission']);self.put(1,body)
        with self.assertRaises(ValueError):validate_series(self.root,self.refs)


if __name__=='__main__':
    unittest.main()
