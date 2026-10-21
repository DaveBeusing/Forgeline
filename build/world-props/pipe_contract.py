"""One reviewed source correction: align straight pipes with existing X-long instances."""
import copy

def rotate_y_positive_90(vector):
    return (vector[2], vector[1], -vector[0], *vector[3:])

def align_straight_pipe(geometry):
    result = copy.deepcopy(geometry)
    for primitive in result['primitives']:
        for channel in ('POSITION', 'NORMAL', 'TANGENT'):
            if channel in primitive['attributes']:
                primitive['attributes'][channel] = [rotate_y_positive_90(v)
                    for v in primitive['attributes'][channel]]
    return result
