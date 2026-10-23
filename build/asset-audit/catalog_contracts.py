"""Animation contracts shared by the source census and regression fixtures."""
import numpy as np


def animation_stats(document, buffers, read_accessor):
    animations = document.get('animations', [])
    if not animations:
        raise ValueError('Source contains neither mesh primitives nor animation clips')
    channels = 0
    for clip in animations:
        samplers = clip.get('samplers', [])
        targets = set()
        if not clip.get('channels'):
            raise ValueError('Animation clip contains no channels')
        for channel in clip['channels']:
            si = channel['sampler']
            if not isinstance(si, int) or not 0 <= si < len(samplers):
                raise ValueError('Animation channel references a missing sampler')
            sampler = samplers[si]
            target = channel['target']
            node, path = target['node'], target['path']
            if not isinstance(node, int) or not 0 <= node < len(document.get('nodes', [])):
                raise ValueError('Animation target node is missing')
            if path not in ('translation', 'rotation', 'scale') or (node, path) in targets:
                raise ValueError('Animation target is unsupported or duplicated')
            targets.add((node, path))
            interpolation = sampler.get('interpolation', 'LINEAR')
            if interpolation not in ('LINEAR', 'STEP', 'CUBICSPLINE'):
                raise ValueError('Unsupported animation interpolation')
            input_id, output_id = sampler['input'], sampler['output']
            times = read_accessor(document, buffers, input_id)
            values = read_accessor(document, buffers, output_id)
            if document['accessors'][input_id]['componentType'] != 5126 or times.shape[1] != 1 or times[0, 0] < 0 or np.any(np.diff(times[:, 0]) <= 0):
                raise ValueError('Animation times must be increasing nonnegative FLOAT scalars')
            factor = 3 if interpolation == 'CUBICSPLINE' else 1
            if document['accessors'][output_id]['componentType'] != 5126 or values.shape != (len(times) * factor, 4 if path == 'rotation' else 3):
                raise ValueError('Animation output count or shape does not match its sampler')
            if path == 'rotation':
                keys = values[1::3] if factor == 3 else values
                if np.any(np.abs(np.linalg.norm(keys, axis=1) - 1) > 0.002):
                    raise ValueError('Animation rotation keys must be unit quaternions')
            channels += 1
    for index in range(len(document.get('accessors', []))):
        read_accessor(document, buffers, index)
    return {'animations': len(animations), 'channels': channels}, []
