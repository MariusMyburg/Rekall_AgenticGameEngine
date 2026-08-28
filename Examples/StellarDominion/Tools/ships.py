"""Procedural capital-ship and fighter hulls for StellarDominion.

Returns Rekall.GeometryMesh component payloads (vertices + indices), which is the
same generic primitive the engine's own tree generator uses. Hulls are built from
stacked cross-sections lofted along the ship's length so the silhouette reads as a
purposeful vessel rather than a box.
"""
import math


def _loft(sections, close_ends=True):
    """sections: list of (z, [(x,y), ...]) with equal ring lengths."""
    verts, idx = [], []
    ring = len(sections[0][1])
    for z, pts in sections:
        for (x, y) in pts:
            verts.append({"x": x, "y": y, "z": z})
    for s in range(len(sections) - 1):
        for i in range(ring):
            a = s * ring + i
            b = s * ring + (i + 1) % ring
            c = (s + 1) * ring + i
            d = (s + 1) * ring + (i + 1) % ring
            idx += [a, c, b, b, c, d]
    if close_ends:
        for s, flip in ((0, False), (len(sections) - 1, True)):
            base = len(verts)
            z = sections[s][0]
            cx = sum(p[0] for p in sections[s][1]) / ring
            cy = sum(p[1] for p in sections[s][1]) / ring
            verts.append({"x": cx, "y": cy, "z": z})
            for i in range(ring):
                a = s * ring + i
                b = s * ring + (i + 1) % ring
                idx += [base, b, a] if flip else [base, a, b]
    return verts, idx


def _ring(w, h, n=8, ytop=None):
    """Flattened octagonal cross-section; ytop lets the dorsal line differ from
    the ventral one so hulls are not trivially symmetric."""
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n
        x = math.cos(a) * w
        y = math.sin(a) * (h if (ytop is None or math.sin(a) < 0) else ytop)
        pts.append((x, y))
    return pts


def dreadnought(length=26.0, beam=3.4, seed=0):
    """Long spinal hull, flared engine block aft, tapered prow."""
    L = length
    sections = [
        (-L * 0.50, _ring(beam * 0.16, beam * 0.14)),
        (-L * 0.44, _ring(beam * 0.46, beam * 0.34)),
        (-L * 0.30, _ring(beam * 0.72, beam * 0.50)),
        (-L * 0.10, _ring(beam * 0.92, beam * 0.62, ytop=beam * 0.78)),
        (L * 0.10, _ring(beam * 1.00, beam * 0.60, ytop=beam * 0.84)),
        (L * 0.26, _ring(beam * 0.88, beam * 0.52, ytop=beam * 0.66)),
        (L * 0.40, _ring(beam * 0.60, beam * 0.36)),
        (L * 0.48, _ring(beam * 0.26, beam * 0.18)),
        (L * 0.50, _ring(beam * 0.10, beam * 0.08)),
    ]
    return _loft(sections)


def cruiser(length=14.0, beam=2.2):
    L = length
    sections = [
        (-L * 0.50, _ring(beam * 0.22, beam * 0.20)),
        (-L * 0.38, _ring(beam * 0.60, beam * 0.44)),
        (-L * 0.16, _ring(beam * 0.86, beam * 0.56, ytop=beam * 0.70)),
        (L * 0.14, _ring(beam * 0.80, beam * 0.48, ytop=beam * 0.62)),
        (L * 0.34, _ring(beam * 0.48, beam * 0.30)),
        (L * 0.50, _ring(beam * 0.12, beam * 0.10)),
    ]
    return _loft(sections)


def fighter(length=1.5, beam=0.42):
    """Small delta: wide midships, sharp nose, stub tail."""
    L = length
    sections = [
        (-L * 0.50, _ring(beam * 0.5, beam * 0.42, n=6)),
        (-L * 0.20, _ring(beam * 1.35, beam * 0.40, n=6)),
        (L * 0.05, _ring(beam * 1.05, beam * 0.34, n=6)),
        (L * 0.34, _ring(beam * 0.42, beam * 0.20, n=6)),
        (L * 0.50, _ring(beam * 0.10, beam * 0.07, n=6)),
    ]
    return _loft(sections)


def mesh_component(verts, idx):
    return ("Rekall.GeometryMesh", {"vertices": verts, "indices": idx})


def drive(length, beam, nozzles=3):
    """Emissive drive block sitting just aft of a hull's stern.

    Kept as its own mesh because a Rekall.Material applies to the whole mesh: an
    emissive hull glows end to end, so the drives have to be separate geometry to
    bloom on their own.
    """
    verts, idx = [], []
    z0 = -length * 0.50
    spread = beam * 0.42
    centres = [(0.0, 0.0)] if nozzles == 1 else [
        (math.cos(2 * math.pi * i / nozzles) * spread,
         math.sin(2 * math.pi * i / nozzles) * spread * 0.5)
        for i in range(nozzles)
    ]
    for (cx, cy) in centres:
        r = beam * 0.26
        sections = [
            (z0 + length * 0.045, [(cx + x, cy + y) for (x, y) in _ring(r * 0.72, r * 0.72, n=8)]),
            (z0 - length * 0.005, [(cx + x, cy + y) for (x, y) in _ring(r, r, n=8)]),
            (z0 - length * 0.030, [(cx + x, cy + y) for (x, y) in _ring(r * 0.55, r * 0.55, n=8)]),
        ]
        v, i = _loft(sections)
        base = len(verts)
        verts += v
        idx += [n + base for n in i]
    return verts, idx
