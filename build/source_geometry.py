"""Bounded component repairs; retain valid legacy profiles and triangle ranges."""
import numpy as np


def _unit(value):
    length = np.linalg.norm(value)
    if not np.isfinite(length) or length <= 1e-12:
        raise ValueError('Component direction must be finite and nonzero')
    return value / length


def pipe_frames(points, reference):
    """Preserve regular authored frames; transport through reference singularities."""
    points = np.asarray(points, dtype=float)
    reference = np.asarray(reference, dtype=float)
    if points.ndim != 2 or points.shape[1] != 3 or len(points) < 2 or not np.isfinite(points).all():
        raise ValueError('Pipe requires at least two finite 3D points')
    if reference.shape != (3,) or not np.isfinite(reference).all():
        raise ValueError('Pipe reference must be a finite 3D vector')
    if np.any(np.linalg.norm(np.diff(points, axis=0), axis=1) <= 1e-12):
        raise ValueError('Pipe contains a repeated adjacent point')
    tangents = [_unit(points[min(i + 1, len(points) - 1)] - points[max(0, i - 1)]) for i in range(len(points))]
    legacy = [np.cross(tangent, reference) for tangent in tangents]
    if all(np.linalg.norm(u) >= 1e-6 for u in legacy):
        # No reference singularity: retain valid production surface roll exactly.
        return [(u / np.linalg.norm(u), np.cross(t, u / np.linalg.norm(u))) for t, u in zip(tangents, legacy)]
    first = tangents[0]
    u = np.cross(first, reference)
    if np.linalg.norm(u) <= 1e-8:
        u = np.cross(first, np.eye(3)[int(np.argmin(np.abs(first)))])
    u = _unit(u)
    frames = [(u.copy(), _unit(np.cross(first, u)))]
    for previous, tangent in zip(tangents, tangents[1:]):
        cosine = float(np.clip(np.dot(previous, tangent), -1, 1))
        axis = np.cross(previous, tangent)
        if cosine <= -1 + 1e-8:
            raise ValueError('Pipe reverses direction without a defined bend')
        if np.linalg.norm(axis) > 1e-12:
            u = u + np.cross(axis, u) + np.cross(axis, np.cross(axis, u)) / (1 + cosine)
        u = _unit(u - tangent * np.dot(u, tangent))
        frames.append((u.copy(), _unit(np.cross(tangent, u))))
    return frames


def _cross(a, b):
    return a[..., 0] * b[..., 1] - a[..., 1] * b[..., 0]


def inset_profile(profile, inset):
    """Retain a valid authored inset; repair crossing strips by convex offsets."""
    outer = np.asarray(profile, dtype=float)
    inset = np.asarray(inset, dtype=float)
    if outer.ndim != 2 or outer.shape[1] != 2 or len(outer) < 3 or not np.isfinite(outer).all():
        raise ValueError('Track profile must contain finite 2D vertices')
    if inset.shape != (2,) or not np.isfinite(inset).all() or np.any(inset <= 0):
        raise ValueError('Track inset must be finite and positive')
    edges = np.roll(outer, -1, axis=0) - outer
    lengths = np.linalg.norm(edges, axis=1)
    scale = float(np.max(np.ptp(outer, axis=0)))
    eps = max(scale * scale * 1e-12, 1e-15)
    area = float(_cross(outer, np.roll(outer, -1, axis=0)).sum())
    if abs(area) <= eps or np.any(lengths <= 1e-12):
        raise ValueError('Track profile is degenerate')
    winding = 1 if area > 0 else -1
    if np.any(winding * _cross(edges, np.roll(edges, -1, axis=0)) <= eps):
        raise ValueError('Track inset requires a strictly convex profile')
    normals = winding * np.column_stack((-edges[:, 1], edges[:, 0])) / lengths[:, None]
    offsets = np.einsum('ij,ij->i', normals, outer)

    def valid(inner):
        if not np.isfinite(inner).all() or np.any(inner @ normals.T < offsets[None, :] - eps):
            return False
        for i in range(len(outer)):
            j = (i + 1) % len(outer)
            quad = np.array([outer[i], outer[j], inner[j], inner[i]])
            sides = np.roll(quad, -1, axis=0) - quad
            if np.any(winding * _cross(sides, np.roll(sides, -1, axis=0)) <= eps):
                return False
        return True

    legacy = outer - np.sign(outer - outer.mean(axis=0)) * inset
    if valid(legacy):
        return legacy
    shifted = offsets + np.abs(normals) @ inset
    result = np.array([np.linalg.solve(normals[[i - 1, i]], shifted[[i - 1, i]]) for i in range(len(outer))])
    if not valid(result):
        raise ValueError('Track inset collapses or crosses the outer profile')
    return result
