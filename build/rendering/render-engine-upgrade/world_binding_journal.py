"""Strict opt-in opaque-World binding journals; no GPU content or cause attestation."""
from __future__ import annotations
import base64
import json
import math
import struct
from pathlib import Path
from baseline_contracts import require, parse_json_bytes, sha256_bytes
from stage_diagnostics import read_capture, bounded_bytes
from world_input_diagnostics import safe

MAX_BYTES = 32 * 1024 * 1024
ENVELOPE_BYTES = 1024 * 1024
JOURNAL_BYTES = MAX_BYTES - ENVELOPE_BYTES
MAX_DRAWS, MAX_EVENTS, MAX_DEFINITIONS = 4096, 131072, 4096
SINGLE_MAX = float.fromhex('0x1.fffffep+127')
EVENT_FIELDS = set(('sequence kind identityId slot stride offset rangeBytes indexFormat drawArguments '
    'constantBytesBase64 constantSha256 constantMode emitted implicit viewport scissor drawOrdinal').split())


def same(a, b):
    return json.dumps(a, sort_keys=True, separators=(',', ':'), allow_nan=False) == json.dumps(b, sort_keys=True, separators=(',', ':'), allow_nan=False)


def number(value, name, low=0, high=2**64-1):
    require(type(value) is int and low <= value <= high, 'Invalid strict integer: ' + name)
    return value


def single_value(value):
    if type(value) not in (int, float): return None
    try:
        rounded = struct.unpack('<f', struct.pack('<f', value))[0]
        return rounded if math.isfinite(rounded) else None
    except (OverflowError, struct.error):
        return None


def text(value, name, maximum=512):
    require(type(value) is str and 0 < len(value.encode('utf-16-le')) // 2 <= maximum, 'Missing/oversized string: ' + name)
    return value


def digest(value):
    import re
    require(type(value) is str and re.fullmatch('[0-9a-fA-F]{64}', value), 'Malformed SHA256')
    return value.lower()


def fields(value, expected, name):
    require(type(value) is dict and set(value) == set(expected.split()), 'Wrong typed fields: ' + name)



def wire_bounds(raw):
    """Check original UTF8 spans; reserialization would change escaping/lengths."""
    require(len(raw) <= MAX_BYTES, 'Actual journal sidecar exceeds32MiB')
    source = raw.decode('utf-8-sig')
    decoder = json.JSONDecoder()
    def whitespace(position):
        while position < len(source) and source[position].isspace():
            position += 1
        return position
    position = whitespace(0)
    require(source[position:position+1] == '{', 'Journal envelope is not an object')
    position += 1
    section = None
    while True:
        position = whitespace(position)
        if source[position:position+1] == '}':
            break
        key, position = decoder.raw_decode(source, position)
        require(type(key) is str, 'Envelope property is not a string')
        position = whitespace(position)
        require(source[position:position+1] == ':', 'Envelope property separator missing')
        start = whitespace(position+1)
        _, end = decoder.raw_decode(source, start)
        if key == 'journal':
            require(section is None, 'Duplicate journal section')
            section = (start, end)
        position = whitespace(end)
        if source[position:position+1] == '}':
            break
        require(source[position:position+1] == ',', 'Envelope property delimiter missing')
        position += 1
    require(section is not None, 'Original journal section missing')
    journal_bytes = len(source[section[0]:section[1]].encode('utf-8'))
    envelope_bytes = len(raw) - journal_bytes + 4
    require(journal_bytes <= JOURNAL_BYTES and envelope_bytes <= ENVELOPE_BYTES,
            'Original journal/envelope exceeds31MiB/1MiB reserved wire bounds')
    return journal_bytes, envelope_bytes


def pipeline(p):
    fields(p, 'vertexShaderSha256 pixelShaderSha256 vertexEntryPoint pixelEntryPoint vertexSourceName pixelSourceName vertexElements vertexRootConstantCount pixelTextureCount primitiveTopology cullMode depthEnabled depthAlwaysPass alphaBlendEnabled sceneDepthReceiver vertexConstantsInBuffer directionalShadowReceiver targetFormat', 'pipeline')
    digest(p['vertexShaderSha256']); digest(p['pixelShaderSha256'])
    for key in ('vertexEntryPoint', 'pixelEntryPoint'):
        text(p[key], key, 128)
    for key in ('vertexSourceName', 'pixelSourceName'):
        text(p[key], key)
    for key in ('depthEnabled', 'depthAlwaysPass', 'alphaBlendEnabled', 'sceneDepthReceiver', 'vertexConstantsInBuffer', 'directionalShadowReceiver'):
        require(type(p[key]) is bool, 'Pipeline boolean alias: ' + key)
    require(p['primitiveTopology'] in ('TriangleList', 'LineList') and p['cullMode'] in ('CounterClockwise', 'Clockwise', 'None') and p['targetFormat'] in ('Rgba16Float', 'Rgba8Unorm', 'Depth32Float'), 'Unknown pipeline enum')
    count = number(p['vertexRootConstantCount'], 'root constants', 0, 64)
    textures = number(p['pixelTextureCount'], 'pixel textures', 0, 16)
    require(not p['vertexConstantsInBuffer'] or count > 0, 'Buffered constants without declared values')
    require(not p['depthAlwaysPass'] or p['depthEnabled'], 'Always-pass without depth')
    require(not p['directionalShadowReceiver'] or p['vertexConstantsInBuffer'] and count > 0 and textures <= 15, 'Malformed shadow receiver pipeline')
    require(type(p['vertexElements']) is list and len(p['vertexElements']) <= 64, 'Vertex layout exceeds bound')
    seen = set()
    for element in p['vertexElements']:
        fields(element, 'semanticName semanticIndex format offsetInBytes inputSlot inputRate instanceStepRate', 'vertex element')
        name = text(element['semanticName'], 'semantic', 128)
        index = number(element['semanticIndex'], 'semantic index', 0, 2**31-1)
        require((name.upper(), index) not in seen, 'Repeated vertex semantic'); seen.add((name.upper(), index))
        number(element['offsetInBytes'], 'vertex offset', 0, 2**31-1)
        number(element['inputSlot'], 'input slot', 0, 31)
        step = number(element['instanceStepRate'], 'instance step', 0, 2**31-1)
        require(element['format'] in ('Float2', 'Float3', 'Float4') and element['inputRate'] in ('PerVertex', 'PerInstance') and (step == 0 if element['inputRate'] == 'PerVertex' else step > 0), 'Vertex format/rate differs')


def definitions(rows):
    require(type(rows) is list and len(rows) <= MAX_DEFINITIONS, 'Identity definition bound exceeded')
    result = {}
    for row in rows:
        fields(row, 'id kind buffer texture descriptor pipeline', 'identity definition')
        identity = number(row['id'], 'identity', 1)
        require(identity not in result, 'Duplicate identity definition')
        kind = row['kind']
        require(kind in ('buffer', 'texture', 'pipeline'), 'Unknown explicit resource kind')
        require((row['buffer'] is not None) is (kind == 'buffer') and (row['texture'] is not None) is (kind == 'texture') and
                (row['pipeline'] is not None) is (kind == 'pipeline') and (row['descriptor'] is not None) is (kind == 'texture'), 'Definition kind/payload differs')
        if kind == 'buffer':
            fields(row['buffer'], 'sizeInBytes memory', 'buffer')
            number(row['buffer']['sizeInBytes'], 'buffer size', 1)
            require(row['buffer']['memory'] in ('GpuLocal', 'Upload'), 'Unknown buffer memory')
        elif kind == 'texture':
            t = row['texture']; fields(t, 'width height format colorSpace mipCount deferUploadWait', 'texture')
            number(t['width'], 'texture width', 1, 2**31-1); number(t['height'], 'texture height', 1, 2**31-1)
            number(t['mipCount'], 'mip count', 1, 32); number(row['descriptor'], 'descriptor', 0, 2**31-1)
            require(t['format'] in ('Rgba8Unorm', 'Bc7Unorm', 'R8Unorm') and t['colorSpace'] in ('Linear', 'Srgb') and type(t['deferUploadWait']) is bool, 'Malformed texture metadata')
            require(t['format'] != 'R8Unorm' or t['colorSpace'] == 'Linear', 'Coverage texture must be linear')
            require(t['format'] != 'Bc7Unorm' or t['width'] % 4 == 0 and t['height'] % 4 == 0, 'BC7 dimensions must be multiples of four')
        else:
            pipeline(row['pipeline'])
        result[identity] = row
    return result


def implicit(value, journal, kind, root=None, register=None, descriptor=False):
    fields(value, 'kind resourceGeneration frameIndex descriptor rootParameter textureRegister constantByteOffset textureRootParameter', 'implicit binding')
    require(value['kind'] == kind and type(value['resourceGeneration']) is int and value['resourceGeneration'] == journal['resourceGeneration'] and
            type(value['frameIndex']) is int and value['frameIndex'] == journal['frameIndex'] and value['rootParameter'] == root and
            (value['rootParameter'] is None or type(value['rootParameter']) is int) and value['textureRegister'] == register and
            (value['textureRegister'] is None or type(value['textureRegister']) is int), 'Implicit resource epoch/slot/root differs')
    offset = number(value['constantByteOffset'], 'constant byte offset', 0, 4096*256-256)
    require(offset % 256 == 0, 'Buffered constant offset alignment differs')
    if descriptor:
        number(value['descriptor'], 'implicit texture descriptor', 0, 2**31-1)
        require(type(value['textureRootParameter']) is int and value['textureRootParameter'] == root + 1 and offset == 0, 'Implicit texture root/offset differs')
    else:
        require(value['descriptor'] is None and value['textureRootParameter'] is None, 'Implicit data must not invent texture identity')


def reduce_events(journal):
    fields(journal, 'schemaVersion kind complete failure deviceGeneration resourceGeneration deviceSession frameIndex submission completedFence presentedBefore presentedAfter drawCount accountedPayloadBytes definitions events', 'journal')
    require(type(journal['schemaVersion']) is int and journal['schemaVersion'] == 1 and journal['kind'] == 'opaque-world-binding-journal', 'Unknown journal version/kind')
    require(journal['complete'] is True and journal['failure'] is None, 'Incomplete prefixes cannot qualify selected journal-v1')
    for key in ('deviceGeneration', 'resourceGeneration'):
        number(journal[key], key, 1)
    import re
    require(type(journal['deviceSession']) is str and re.fullmatch('[0-9a-f]{32}', journal['deviceSession']), 'Device session missing')
    number(journal['frameIndex'], 'frame slot', 0, 15)
    number(journal['accountedPayloadBytes'], 'accounted compact DTO bytes', 0, JOURNAL_BYTES)
    defs = definitions(journal['definitions'])
    events = journal['events']
    require(type(events) is list and len(events) <= MAX_EVENTS, 'Event bound exceeded')
    draw_count = number(journal['drawCount'], 'draw count', 0, MAX_DRAWS)
    state = dict(pipeline=None, constants=None, textures={}, vertices={}, index=None, shadow=None, shadowFrame=None, viewport=None, scissor=None)
    draws = []
    for i, event in enumerate(events):
        require(type(event) is dict and set(event) == EVENT_FIELDS, 'Wrong event fields')
        require(type(event['sequence']) is int and event['sequence'] == i, 'Event sequence gap/replay')
        kind = event['kind']
        active = {'sequence', 'kind'}
        def use(*keys):
            active.update(keys)
            require(all(event[k] is not None for k in keys), 'Missing required event value: ' + kind)
        def identity(resource_kind):
            use('identityId'); key = number(event['identityId'], 'event resource identity', 1)
            require(key in defs and defs[key]['kind'] == resource_kind, 'Undefined/foreign resource kind')
            return key
        if kind == 'InvalidateRoot':
            state.update(pipeline=None, constants=None, textures={}, shadow=None)
        elif kind == 'Pipeline':
            key = identity('pipeline'); use('emitted')
            require(type(event['emitted']) is bool, 'Pipeline emission boolean missing')
            if event['emitted']:
                state.update(constants=None, textures={}, shadow=None)
            else:
                require(state['pipeline'] == key, 'False pipeline cache-hit cannot borrow state')
            state['pipeline'] = key
        elif kind in ('VertexBuffer', 'IndexBuffer'):
            key = identity('buffer'); use('offset', 'rangeBytes')
            offset = number(event['offset'], 'bound view offset', 0, 2**31-1)
            size = number(event['rangeBytes'], 'bound view size', 1, 2**32-1)
            require(offset + size == defs[key]['buffer']['sizeInBytes'], 'View range differs from actual full remaining buffer view')
            if kind == 'VertexBuffer':
                use('slot', 'stride'); slot = number(event['slot'], 'vertex slot', 0, 31)
                number(event['stride'], 'vertex stride', 1, 2**31-1)
                state['vertices'][slot] = dict(identityId=key, offset=offset, rangeBytes=size, stride=event['stride'])
            else:
                use('indexFormat'); require(event['indexFormat'] in ('SixteenBit', 'ThirtyTwoBit'), 'Unknown index format')
                state['index'] = dict(identityId=key, offset=offset, rangeBytes=size, indexFormat=event['indexFormat'])
        elif kind in ('Constants', 'ShadowFrame'):
            use('constantBytesBase64', 'constantSha256', 'constantMode')
            raw = base64.b64decode(event['constantBytesBase64'], validate=True)
            require(len(raw) <= 256 and len(raw) % 4 == 0 and sha256_bytes(raw) == digest(event['constantSha256']), 'Original IEEE constant bytes/hash differ')
            if kind == 'ShadowFrame':
                use('implicit'); require(len(raw) == 96 and event['constantMode'] == 'implicit-shadow-frame', 'Actual shadow frame constants differ')
                implicit(event['implicit'], journal, 'directional-shadow-frame')
                require(event['implicit']['constantByteOffset'] == 0, 'Shadow frame data offset differs')
                state['shadowFrame'] = event['constantSha256'].lower()
            else:
                require(state['pipeline'] is not None, 'Constants have no actual pipeline')
                p = defs[state['pipeline']]['pipeline']
                require(len(raw) == p['vertexRootConstantCount']*4 and p['vertexRootConstantCount'] > 0, 'Constants count differs from actual pipeline')
                require(event['constantMode'] == ('buffered' if p['vertexConstantsInBuffer'] else 'root'), 'Root/buffered mode differs')
                if p['vertexConstantsInBuffer']:
                    use('implicit'); implicit(event['implicit'], journal, 'buffered-vertex-constants', root=0)
                    require(event['implicit']['constantByteOffset'] >= 256, 'Buffered values overlap shadow frame data')
                state['constants'] = dict(bytes=event['constantBytesBase64'], sha256=event['constantSha256'].lower(), mode=event['constantMode'], implicit=event['implicit'])
        elif kind == 'ShadowReceiver':
            use('implicit', 'emitted'); require(type(event['emitted']) is bool and state['pipeline'] is not None, 'Shadow receiver has no pipeline')
            p = defs[state['pipeline']]['pipeline']
            require(p['directionalShadowReceiver'] and state['shadowFrame'] is not None, 'Shadow receiver lacks same-capture frame constants')
            implicit(event['implicit'], journal, 'directional-shadow-receiver', root=1+p['pixelTextureCount'], register=15, descriptor=True)
            if not event['emitted']:
                require(same(state['shadow'], event['implicit']), 'False implicit shadow binding cache hit')
            state['shadow'] = event['implicit']
        elif kind == 'Texture':
            key = identity('texture'); use('slot', 'emitted')
            require(state['pipeline'] is not None and type(event['emitted']) is bool, 'Texture has no actual pipeline/emission')
            slot = number(event['slot'], 'texture slot', 0, 15)
            require(slot < defs[state['pipeline']]['pipeline']['pixelTextureCount'], 'Texture exceeds declared root signature')
            if not event['emitted']:
                require(state['textures'].get(slot) == key, 'False texture cache hit')
            state['textures'][slot] = key
        elif kind == 'Viewport':
            use('viewport'); values = event['viewport']
            require(type(values) is list and len(values) == 4, 'Actual viewport shape differs')
            rounded = [single_value(v) for v in values]
            require(all(v is not None for v in rounded) and rounded[2] > 0 and rounded[3] > 0, 'Actual viewport malformed')
            state['viewport'] = values
        elif kind == 'Scissor':
            use('scissor'); values = event['scissor']
            require(type(values) is list and len(values) == 4, 'Scissor shape differs')
            for v in values: number(v, 'scissor', -2**31, 2**31-1)
            require(values[2] > values[0] and values[3] > values[1], 'Scissor bounds differ')
            state['scissor'] = values
        elif kind in ('DrawInstanced', 'DrawIndexedInstanced'):
            use('drawArguments', 'drawOrdinal')
            require(type(event['drawOrdinal']) is int and event['drawOrdinal'] == len(draws) and state['pipeline'] is not None, 'World draw sequence/pipeline missing')
            args = event['drawArguments']; require(type(args) is list and len(args) == 5, 'Complete native draw arguments missing')
            for n, a in enumerate(args): number(a, 'draw argument', -2**31 if n == 3 else 0, 2**31-1)
            require(args[0] > 0 and args[1] > 0, 'Native draw count is empty')
            if kind == 'DrawInstanced': require(args[1] == 1 and args[3:] == [0, 0], 'Nonindexed native argument shape differs')
            p = defs[state['pipeline']]['pipeline']
            require(p['targetFormat'] == 'Rgba16Float' and not p['sceneDepthReceiver'], 'A shadow/particle/overlay draw was labelled opaque World')
            require(state['viewport'] is not None and state['scissor'] is not None, 'World draw raster state missing')
            require(not p['vertexRootConstantCount'] or state['constants'] is not None, 'World draw constants unavailable')
            require(not p['directionalShadowReceiver'] or state['shadow'] is not None, 'World draw implicit shadow binding missing')
            require(all(e['inputSlot'] in state['vertices'] for e in p['vertexElements']), 'World draw inherited vertex view missing')
            require(all(slot in state['textures'] for slot in range(p['pixelTextureCount'])), 'World draw texture state missing')
            require(kind != 'DrawIndexedInstanced' or state['index'] is not None, 'Indexed World draw has no actual index view')
            snapshot = json.loads(json.dumps(state))
            draws.append(dict(ordinal=len(draws), kind=kind, arguments=args, state=snapshot))
        else:
            require(False, 'Unknown journal event: ' + str(kind))
        require(all(event[k] is None for k in EVENT_FIELDS-active), 'Unexpected active event field: ' + str(kind))
    require(len(draws) == draw_count, 'World draw count/prefix differs')
    return defs, draws


def read_journal(root, stage_reference, stage=None, required_version=1):
    require(required_version is None or type(required_version) is int and required_version == 1, 'Unsupported selected journal contract')
    if stage is None: stage = read_capture(root, stage_reference)[0]
    version, reference = stage.get('worldBindingJournalVersion'), stage.get('worldBindingJournal')
    if version is None and reference is None and required_version is None:
        return dict(available=False, status='historical-unavailable', qualified=False)
    require(type(version) is int and version == 1, 'Selected journal-v1 reference missing/unknown')
    fields(reference, 'file sha256 bytes schemaVersion', 'journal file reference')
    require(type(reference['schemaVersion']) is int and reference['schemaVersion'] == 1, 'Unknown journal reference version')
    name = reference['file']; require(type(name) is str and '/' not in name and '\\' not in name and ':' not in name and name not in ('', '.', '..'), 'Journal must be an original sibling sidecar')
    metadata_path = safe(root, stage_reference['path'])
    metadata_raw=bounded_bytes(metadata_path,1024*1024)
    require(sha256_bytes(metadata_raw) == digest(stage_reference['sha256']) and same(parse_json_bytes(metadata_raw),stage), 'Original stage metadata differs from supplied validated stage')
    path = safe(metadata_path.parent, name); raw = bounded_bytes(path, MAX_BYTES)
    require(type(reference['bytes']) is int and len(raw) == reference['bytes'] and sha256_bytes(raw) == digest(reference['sha256']), 'Original journal sidecar bytes/hash differ')
    wire_bounds(raw)
    value = parse_json_bytes(raw)
    fields(value, 'schemaVersion kind qualified sourceSha producer runtimeFingerprint recipeFingerprint recipeInputBytesBase64 repetition camera profileDomain profileInputBytesBase64 profileFingerprint deviceIdentity bufferCount journal scope', 'journal sidecar')
    require(type(value['schemaVersion']) is int and value['schemaVersion'] == 1 and value['kind'] == 'reference-world-binding-journal' and value['qualified'] is False, 'Wrong selected journal sidecar')
    require(all(same(value[k], stage[k]) for k in ('sourceSha','producer','runtimeFingerprint','recipeFingerprint','recipeInputBytesBase64','repetition','camera','deviceIdentity')), 'Original journal source/recipe/pose/process differs from stage')
    require(value['profileDomain'] == 'effective-quality-profile', 'Journal profile domain is not full ordinary composition')
    profile = base64.b64decode(value['profileInputBytesBase64'], validate=True)
    require(sha256_bytes(profile) == digest(value['profileFingerprint']) and same(parse_json_bytes(profile), stage['effectiveProfile']), 'Journal actual quality bytes differ')
    require(type(value['bufferCount']) is int and value['bufferCount'] == stage['outputRaster']['bufferCount'] == 3, 'Journal buffer count differs')
    text(value['scope'], 'scope', 1024)
    journal = value['journal']
    require(type(journal) is dict and same(journal.get('submission'), stage['submission']) and journal.get('completedFence') == stage['completedFence'] and
            journal.get('frameIndex') == stage['frameIndex'] and journal.get('presentedBefore') == stage['presentedBefore'] and journal.get('presentedAfter') == stage['presentedAfter'] and
            journal.get('deviceGeneration') == stage['deviceIdentity']['generation'] and journal.get('resourceGeneration') == stage['submission']['resourceGeneration'] and journal.get('deviceSession') == stage['deviceIdentity']['sessionId'],
            'Journal completion/device/slot tuple differs from original successful stage')
    for key in ('completedFence','frameIndex','presentedBefore','presentedAfter'):
        number(journal[key], key)
    defs, draws = reduce_events(journal)
    return dict(available=True, status='complete-bounded-command-observations', qualified=False, record=value, definitions=defs, draws=draws, sha256=sha256_bytes(raw))


def semantic_draw(draw, defs):
    """Descriptions can compare across lanes; IDs/descriptor/epoch/slots cannot."""
    result = json.loads(json.dumps(draw))
    state = result['state']
    state['pipeline'] = defs[state['pipeline']]['pipeline']
    for value in state['vertices'].values():
        value['description'] = defs[value.pop('identityId')]['buffer']
    if state['index'] is not None:
        state['index']['description'] = defs[state['index'].pop('identityId')]['buffer']
    state['textures'] = {slot: defs[identity]['texture'] for slot, identity in state['textures'].items()}
    if state['constants'] is not None: state['constants'].pop('implicit', None)
    if state['shadow'] is not None:
        state['shadow'] = dict(kind=state['shadow']['kind'], rootParameter=state['shadow']['rootParameter'], textureRootParameter=state['shadow']['textureRootParameter'], textureRegister=15)
    return result


def validate_loaded(root, references, captures, required_version=1):
    require(type(references) is list and len(references) == len(captures) == 8, 'All eight declared stages required')
    loaded = [read_journal(root, ref, capture[0], required_version) for ref, capture in zip(references, captures)]
    if all(not value['available'] for value in loaded):
        return dict(available=False, status='historical-unavailable', qualified=False)
    require(all(value['available'] for value in loaded), 'Selected series cannot mix missing journals')
    known = {}; previous = None; comparisons = []; previous_slot = {}
    first_semantic = None
    for i, item in enumerate(loaded):
        stage = captures[i][0]; value = item['record']
        require(type(value['repetition']) is int and value['repetition'] == i, 'Journal repetition missing/replayed')
        if previous is not None:
            require(all(same(value[k], previous[k]) for k in ('sourceSha','producer','runtimeFingerprint','recipeFingerprint','deviceIdentity','camera','profileInputBytesBase64')), 'Own-lane journal immutable identity changed')
            current_tuple=value['journal']['submission'];prior_tuple=previous['journal']['submission']
            require(current_tuple['submission']==prior_tuple['submission']+1 and
                    current_tuple['submittedFence']>prior_tuple['submittedFence'] and
                    current_tuple['resourceGeneration']==prior_tuple['resourceGeneration'] and
                    value['journal']['presentedBefore']==previous['journal']['presentedAfter'],
                    'Own-lane journal capture submission/fence/resource/presentation chronology differs')

        for identity, definition in item['definitions'].items():
            require(identity not in known or same(known[identity], definition), 'Same local Lifetime identity changed its recorded definition')
            known[identity] = definition
        semantic = [semantic_draw(d, item['definitions']) for d in item['draws']]
        first_semantic = semantic if first_semantic is None else first_semantic
        slot=stage['frameIndex']; prior=previous_slot.get(slot)
        comparisons.append(dict(repetition=i, frameIndex=slot, submission=stage['submission'], semanticDescriptionsEqualToFirst=same(semantic, first_semantic),
                                previousSameSlotRepetition=prior[0] if prior else None,
                                localBindingsEqualToPreviousSameSlot=same(item['draws'],prior[1]) if prior else None,
                                localDrawBindings=item['draws']))
        previous_slot[slot]=(i,item['draws'])
        previous = value
    return dict(available=True, status='complete-bounded-command-observations', qualified=False, comparisons=comparisons,
                interpretation='Local IDs are device-epoch continuity only; semantic description equality is not asset identity, GPU contents or cause proof')


def validate_series(root, stage_references, required_version=1):
    return validate_loaded(root, stage_references, [read_capture(root, ref) for ref in stage_references], required_version)


def compare_lanes(root, native_references, host_references):
    results = [validate_series(root, refs, 1) for refs in (native_references, host_references)]
    lanes = [[read_journal(root, ref) for ref in refs] for refs in (native_references, host_references)]
    comparisons = []
    for i, (a, b) in enumerate(zip(*lanes)):
        left, right = a['record'], b['record']
        require(left['producer'] == 'native-reference' and right['producer'] == 'actual-render-host' and
                all(same(left[k], right[k]) for k in ('sourceSha','runtimeFingerprint','recipeFingerprint','recipeInputBytesBase64','camera')) and
                same(parse_json_bytes(base64.b64decode(left['profileInputBytesBase64'])), parse_json_bytes(base64.b64decode(right['profileInputBytesBase64']))),
                'Cross-lane immutable journal inputs differ')
        x = [semantic_draw(d, a['definitions']) for d in a['draws']]
        y = [semantic_draw(d, b['definitions']) for d in b['draws']]
        comparisons.append(dict(repetition=i, semanticDescriptionsEqual=same(x, y), native=x, host=y))
    return dict(kind='cross-lane-opaque-world-binding-observations', qualified=False, lanes=results, comparisons=comparisons,
                interpretation='Process/local IDs, descriptor addresses, epochs, slots/fences are not equalized. Descriptions omit actual GPU/prearmed contents; pixels/physical mapping remain separate')
