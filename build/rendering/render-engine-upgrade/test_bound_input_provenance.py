"""Pure tests from retained synthetic actual C# DTO serialization; no native/copy execution."""
import base64
import copy
import gzip
import json
import tempfile
import unittest
from pathlib import Path
from baseline_contracts import canonical,sha256_bytes
import bound_input_provenance as p

# Explicitly synthetic C# exporter fixture01, dirty build; IDs and source stamp are data only.
COMPANION='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'
JOURNAL='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'

class BoundInputProvenanceTests(unittest.TestCase):
    def setUp(self):
        temp=tempfile.TemporaryDirectory();self.addCleanup(temp.cleanup);self.root=Path(temp.name)
        self.value=json.loads(gzip.decompress(base64.b64decode(COMPANION)))
        self.journal=json.loads(gzip.decompress(base64.b64decode(JOURNAL)))
        self.save()

    def save(self,suffix=""):
        def put(name,value):
            raw=canonical(value);(self.root/name).write_bytes(raw);return dict(file=name,sha256=sha256_bytes(raw),bytes=len(raw),schemaVersion=1)
        self.journal['metadata']=copy.deepcopy(self.value['metadata'])
        self.value['journalReference']=put('journal'+suffix+'.json',self.journal)
        self.ref=put('companion'+suffix+'.json',self.value)
        m=self.value['metadata'];self.row=dict(repetition=m['repetition'],frameIndex=m['frameIndex'],actualSubmission=m['submission'],pixelSha256=m['pixelSha256'],fingerprint=m['legacyFingerprint'],boundInputProvenance=self.ref,stageDiagnostics=None,worldInputDiagnostics=None,shadowFile=None,shadowLayout=None,shadowFingerprint=None)

    def read(self):return p.read_capture(self.root,self.ref,self.row)

    def test_actual_nonempty_typed_source_copy_and_journal_graph(self):
        result=self.read();self.assertEqual(len(result['draws']),1);self.assertFalse(result['qualified'])
        self.assertEqual(result['transfers'][2]['signaledFence'],650)

    def test_hash_and_original_reference_tamper(self):
        (self.root/'journal.json').write_bytes(b'{}')
        with self.assertRaisesRegex(ValueError,'hash'):self.read()

    def test_selected_missing_or_foreign_reference_never_downgrades(self):
        for ref in (None,dict(self.ref,file='../journal.json'),dict(self.ref,schemaVersion=True)):
            with self.subTest(ref=ref),self.assertRaises(ValueError):p.read_capture(self.root,ref)

    def test_source_destination_and_unknown_holes_are_independently_recomputed(self):
        for mutate in ('source','destination','hole','watermark','ordinal','retired','resource'):
            original=copy.deepcopy(self.value)
            d=self.value['provenance'];a=d['events'][1]['draw']['bindings'][0]
            if mutate=='source':a['sourceWriteOrders']=[99]
            elif mutate=='destination':a['offset']=1
            elif mutate=='hole':a['unknownRanges']=[dict(start=0,length=1)]
            elif mutate=='watermark':d['events'][1]['draw']['writeWatermark']=2
            elif mutate=='ordinal':d['events'][1]['draw']['journalDrawOrdinal']=1
            elif mutate=='retired':d['baseline'][0]['committed']=False
            else:a['resourceId']=99
            self.save()
            with self.subTest(mutate=mutate),self.assertRaises(ValueError):self.read()
            self.value=original

    def test_header_tuple_and_boolean_alias_tamper(self):
        for field in ('resourceGeneration','deviceGeneration','deviceSession','frameIndex','completedFence','presentedAfter'):
            original=copy.deepcopy(self.value)
            self.value['provenance'][field]='f'*32 if field=='deviceSession' else 99
            self.save()
            with self.subTest(field=field),self.assertRaises(ValueError):self.read()
            self.value=original
        self.value['provenance']['ownerThreadId']=True;self.save()
        with self.assertRaises(ValueError):self.read()

    def test_whole_source_slice_preserves_unknown_tail(self):
        r=self.value['provenance']['baseline'][0];r['segments'][0]['length']=64
        a=self.value['provenance']['events'][1]['draw']['bindings'][0];a['unknownRanges']=[dict(start=64,length=64)]
        self.save();self.assertEqual(len(self.read()['draws']),1)
        r['segments'][0]['sourceOffset']=1;self.save()
        with self.assertRaises(ValueError):self.read()

    def test_incomplete_prefix_and_missing_eight_are_unavailable(self):
        self.value['provenance']['complete']=False;self.save()
        with self.assertRaises(ValueError):self.read()
        with self.assertRaises(ValueError):p.validate_series(self.root,[self.ref]*7,[self.row]*7)

    def test_original_capture_pixel_and_submission_joins(self):
        for key in ('fingerprint','pixelSha256','actualSubmission'):
            original=copy.deepcopy(self.row);self.row[key]=None
            with self.subTest(key=key),self.assertRaises(ValueError):self.read()
            self.row=original

    def test_transfer_fence_and_mip_metadata_are_not_labels(self):
        for mutate in ('queued','fence','width','pitch','padding'):
            original=copy.deepcopy(self.value);v=self.value['provenance']
            if mutate=='queued':v['baselineTransfers'][0].update(state='queued',signaledFence=None)
            elif mutate=='fence':v['baselineTransfers'][0]['signaledFence']=True
            else:
                t=v['baseline'][2]['segments'][0]['write']['textureSource']
                if mutate=='width':t['width']=3
                elif mutate=='pitch':t['stagingLayout']['rowPitch']=16
                else:v['baseline'][2]['segments'][0]['write']['sourceByteCount']=80
            self.save()
            with self.subTest(mutate=mutate),self.assertRaises(ValueError):self.read()
            self.value=original

    def series(self):
        initial_value=copy.deepcopy(self.value);initial_journal=copy.deepcopy(self.journal);refs=[];rows=[]
        for i in range(8):
            self.value=copy.deepcopy(initial_value);self.journal=copy.deepcopy(initial_journal)
            m=self.value['metadata'];v=self.value['provenance'];j=self.journal['journal']
            m.update(repetition=i,frameIndex=i%3,presentedBefore=10+i,presentedAfter=11+i,completedFence=700+i)
            m['submission'].update(submission=578+i,submittedFence=700+i)
            for target in (v,j):
                for key in ('frameIndex','presentedBefore','presentedAfter','completedFence','submission'):target[key]=copy.deepcopy(m[key])
            for e in j['events']:
                if e['implicit'] is not None:e['implicit']['frameIndex']=i%3
            v['baseline'][-1]['implicit']['frameIndex']=i%3
            v['baseline'][-1]['id']=4+i%3
            v['events'][0]['write']['resourceId']=4+i%3
            v['events'][1]['draw']['bindings'][-1]['resourceId']=4+i%3
            v['baselineWriteWatermark']+=i;v['finalWriteWatermark']+=i
            v['events'][0]['write']['order']+=i;d=v['events'][1]['draw'];d['writeWatermark']+=i
            d['bindings'][-1]['sourceWriteOrders']=[4+i]
            if i>=3:
                previous=copy.deepcopy(v['events'][0]['write']);previous['order']-=3
                v['baseline'][-1]['segments']=[dict(start=256,length=4,sourceOffset=0,write=previous)]
            self.save(str(i));refs.append(copy.deepcopy(self.ref));rows.append(copy.deepcopy(self.row))
        return refs,rows

    def test_eight_source_originals_and_slot_rotation_are_joined_without_fake_identity_equality(self):
        refs,rows=self.series();result=p.validate_series(self.root,refs,rows)
        self.assertEqual(len(result['captures']),8)
        self.assertTrue(all(c['semanticSourceIntentEqualToFirst'] for c in result['comparisons']))
        self.assertTrue(result['comparisons'][3]['sameSlotLocalAssociationsEqualToPrevious'])
        self.assertFalse(result['qualified']);self.assertFalse(result['wave2Ready'])
        rows[3]['actualSubmission']=rows[0]['actualSubmission']
        with self.assertRaises(ValueError):p.validate_series(self.root,refs,rows)

    def reload_capture(self,refs,index):
        self.value=json.loads((self.root/refs[index]['file']).read_bytes())
        self.journal=json.loads((self.root/self.value['journalReference']['file']).read_bytes())

    def test_cross_capture_global_write_and_resource_definitions_are_immutable(self):
        refs,rows=self.series();self.reload_capture(refs,1)
        self.value['provenance']['baseline'][0]['segments'][0]['write']['sourceSha256']='a'*64
        self.save('1');refs[1]=copy.deepcopy(self.ref);rows[1]=copy.deepcopy(self.row)
        p.read_capture(self.root,refs[1],rows[1])
        with self.assertRaisesRegex(ValueError,'global source write'):p.validate_series(self.root,refs,rows)

    def test_cross_capture_changed_buffer_definition_cannot_hide_behind_same_identity(self):
        refs,rows=self.series();self.reload_capture(refs,1)
        self.value['provenance']['baseline'][0]['buffer']['sizeInBytes']=256
        self.journal['journal']['definitions'][1]['buffer']['sizeInBytes']=256
        self.journal['journal']['events'][3]['rangeBytes']=256
        a=self.value['provenance']['events'][1]['draw']['bindings'][0];a['length']=256;a['unknownRanges']=[dict(start=128,length=128)]
        self.save('1');refs[1]=copy.deepcopy(self.ref);rows[1]=copy.deepcopy(self.row)
        p.read_capture(self.root,refs[1],rows[1])
        with self.assertRaisesRegex(ValueError,'immutable definition'):p.validate_series(self.root,refs,rows)

    def test_referenced_transfer_flushes_before_draw_and_final_fence_must_precede_frame(self):
        v=self.value['provenance'];v['baselineTransfers'][0].update(state='queued',signaledFence=None)
        def event(sequence,state,fence=None):
            transfer=dict(v['baselineTransfers'][0],state=state,signaledFence=fence)
            return dict(sequence=sequence,kind='upload-state',write=None,transfer=transfer,draw=None,resourceId=None,resource=None)
        draw=v['events'].pop();draw['sequence']=3
        v['events'] += [event(1,'execute-returned'),event(2,'signaled',650),draw]
        self.save();self.assertEqual(len(self.read()['draws']),1)
        for fence in (700,701):
            v['events'][2]['transfer']['signaledFence']=fence;self.save()
            with self.subTest(fence=fence),self.assertRaises(ValueError):self.read()
        v['events']=[v['events'][0],draw,event(2,'execute-returned'),event(3,'signaled',650)];draw['sequence']=1;self.save()
        with self.assertRaisesRegex(ValueError,'UseResource'):self.read()

    def test_more_than_initial_list_capacity_is_valid_for_actual_slots_and_mips(self):
        v=self.value['provenance'];j=self.journal['journal'];r=v['baseline'][2]
        r['texture'].update(width=8,height=8,mipCount=4)
        j['definitions'][3]['texture']=copy.deepcopy(r['texture']);j['definitions'][0]['pipeline']['pixelTextureCount']=13
        original_write=copy.deepcopy(r['segments'][0]['write']);r['segments']=[]
        for mip,width in enumerate((8,4,2,1)):
            w=copy.deepcopy(original_write);size=width*width*4
            w.update(order=3+mip,byteCount=size,sourceByteCount=size)
            w['textureSource'].update(mipIndex=mip,width=width,height=width,sourceRowPitch=width*4,logicalRowBytes=width*4,rowCount=width)
            w['textureSource']['stagingLayout'].update(width=width,height=width,rowPitch=256,offset=1024+mip*2048)
            r['segments'].append(dict(start=0,length=size,sourceOffset=0,write=w))
        v['baselineTransfers'][1]['byteCount']=8192
        v.update(baselineWriteWatermark=6,finalWriteWatermark=7);v['events'][0]['write']['order']=7
        d=v['events'][1]['draw'];d['writeWatermark']=7
        d['bindings'][-1]['sourceWriteOrders']=[7]
        base=copy.deepcopy(d['bindings'][2]);textures=[]
        for slot in range(13):
            for mip,width in enumerate((8,4,2,1)):
                a=copy.deepcopy(base);a.update(slot=slot,mipIndex=mip,length=width*width*4,sourceWriteOrders=[3+mip]);textures.append(a)
        d['bindings']=d['bindings'][:2]+textures+[d['bindings'][-1]]
        original_texture=copy.deepcopy(j['events'][5])
        for slot in range(1,13):
            e=copy.deepcopy(original_texture);e['slot']=slot;j['events'].insert(6,e)
        for seq,e in enumerate(j['events']):e['sequence']=seq
        self.save();self.assertEqual(len(self.read()['draws'][0]['bindings']),55)
        for delta in ('missing','extra'):
            original=copy.deepcopy(d['bindings'])
            if delta=='missing':d['bindings'].pop()
            else:d['bindings'].append(copy.deepcopy(d['bindings'][-1]))
            self.save()
            with self.subTest(delta=delta),self.assertRaises(ValueError):self.read()
            d['bindings']=original

    def test_later_overlap_is_separate_from_draw_intent_and_preserves_whole_hash(self):
        v=self.value['provenance'];w=copy.deepcopy(v['baseline'][0]['segments'][0]['write'])
        w.update(order=5,destinationOffset=64,byteCount=4,sourceByteCount=4)
        v['events'].append(dict(sequence=2,kind='source-write',write=w,transfer=None,draw=None,resourceId=None,resource=None));v['finalWriteWatermark']=5
        self.save();result=self.read()
        self.assertEqual(len(result['laterWriteOverlaps']),1)
        overlap=result['laterWriteOverlaps'][0]
        self.assertEqual((overlap['overlapStart'],overlap['overlapLength']),(64,4));self.assertEqual(overlap['sourceSha256'],w['sourceSha256'].lower())
        original=copy.deepcopy(self.value)
        self.value['provenance']['events'][-1]['write']['resourceId']=4
        self.value['provenance']['events'][-1]['write']['kind']='implicit-buffered-constants'
        self.value['provenance']['events'][-1]['write']['elementSize']=4
        self.value['provenance']['events'][-1]['write']['destinationOffset']=512
        self.save();self.assertFalse(self.read()['laterWriteOverlaps'])

    def test_write_kinds_cannot_borrow_memory_or_erase_copy_associations(self):
        for change in ('missing-transfer','gpu-local-upload','upload-copy','static-short','implicit-kind'):
            original=copy.deepcopy(self.value);v=self.value['provenance']
            if change=='missing-transfer':v['baseline'][1]['segments'][0]['write']['transferId']=None
            elif change=='gpu-local-upload':v['baseline'][0]['buffer']['memory']='GpuLocal'
            elif change=='upload-copy':v['baseline'][0]['segments'][0]['write']['transferId']=2
            elif change=='static-short':v['baselineTransfers'][0]['byteCount']=32
            else:v['events'][0]['write']['kind']='upload-set-data'
            self.save()
            with self.subTest(change=change),self.assertRaises(ValueError):self.read()
            self.value=original

    def test_identity_event_and_segment_structural_caps_are_not_policy_budgets(self):
        original=copy.deepcopy(self.value['provenance']);j=self.journal['journal']
        for key,values in (('baseline',[original['baseline'][0]]*4097),('events',[original['events'][0]]*131073)):
            v=copy.deepcopy(original);v[key]=values
            with self.subTest(key=key),self.assertRaises(ValueError):p.validate_provenance(v,j)
        v=copy.deepcopy(original);v['baseline'][0]['segments']*=65
        with self.assertRaises(ValueError):p.validate_provenance(v,j)

    def test_missing_nested_leaf_and_foreign_implicit_slot_fail_closed(self):
        for change in ('missing','slot','format'):
            original=copy.deepcopy(self.value)
            if change=='missing':del self.value['provenance']['baseline'][0]['segments'][0]['write']['sourceByteCount']
            elif change=='slot':self.value['provenance']['baseline'][-1]['implicit']['frameIndex']=1
            else:self.value['provenance']['baseline'][2]['segments'][0]['write']['textureSource']['stagingLayout']['format']='R8_UNorm'
            self.save()
            with self.subTest(change=change),self.assertRaises(ValueError):self.read()
            self.value=original

    def test_wire_limits_use_original_utf8_not_reserialized_counts(self):
        p.wire_bounds(b'{"provenance":{},"context":"x"}','provenance')
        with self.assertRaises(ValueError):p.wire_bounds(b'{"provenance":{},"context":"'+b'x'*p.ENVELOPE_BYTES+b'"}','provenance')
        with self.assertRaises(ValueError):p.wire_bounds(b'{"provenance":"'+b'x'*p.BODY_BYTES+b'"}','provenance')

if __name__=='__main__':unittest.main()
