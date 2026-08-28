"""Polygon ship models for StellarDominion.

Built from flat-shaded primitives rather than smooth lofts. Two things make these
read as hardware rather than as blobs:

  * Every face carries its own vertices and its own normal, so panels meet at
    crisp edges instead of being averaged into a smooth shell.
  * Every face carries a per-vertex colour with a small shade offset, which gives
    panel definition without needing UVs or a hull texture. The engine's
    GeometryMesh vertex format accepts nx/ny/nz and r/g/b/a directly.

Everything is authored along +Z (prow at +Z), matching the Drift system's heading
convention.
"""
import math
import random

# ---------------------------------------------------------------- primitives


class Mesh:
    """Flat-shaded triangle soup with per-vertex colour.

    The tint matters: the engine's ReadVertexColor treats the material's baseColor
    only as a *fallback* for vertices that omit r/g/b, so writing a greyscale shade
    replaces the hull colour outright and the ship renders near-white whatever the
    material says. Shades are therefore multiplied into the tint here.
    """

    def __init__(self, tint=(0.30, 0.34, 0.40)):
        self.v = []
        self.i = []
        self.tint = tint

    def quad(self, p0, p1, p2, p3, shade):
        """One flat quad. Normal from the winding, colour flat across the face."""
        ux, uy, uz = (p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2])
        vx, vy, vz = (p3[0] - p0[0], p3[1] - p0[1], p3[2] - p0[2])
        nx, ny, nz = (uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx)
        n = math.sqrt(nx * nx + ny * ny + nz * nz) or 1.0
        nx, ny, nz = nx / n, ny / n, nz / n
        base = len(self.v)
        for (x, y, z) in (p0, p1, p2, p3):
            self.v.append({
                "x": round(x, 4), "y": round(y, 4), "z": round(z, 4),
                "nx": round(nx, 4), "ny": round(ny, 4), "nz": round(nz, 4),
                "r": round(min(self.tint[0] * shade, 1.0), 4),
                "g": round(min(self.tint[1] * shade, 1.0), 4),
                "b": round(min(self.tint[2] * shade, 1.0), 4),
                "a": 1,
            })
        self.i += [base, base + 1, base + 2, base, base + 2, base + 3]

    def tri(self, p0, p1, p2, shade):
        ux, uy, uz = (p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2])
        vx, vy, vz = (p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2])
        nx, ny, nz = (uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx)
        n = math.sqrt(nx * nx + ny * ny + nz * nz) or 1.0
        nx, ny, nz = nx / n, ny / n, nz / n
        base = len(self.v)
        for (x, y, z) in (p0, p1, p2):
            self.v.append({
                "x": round(x, 4), "y": round(y, 4), "z": round(z, 4),
                "nx": round(nx, 4), "ny": round(ny, 4), "nz": round(nz, 4),
                "r": round(min(self.tint[0] * shade, 1.0), 4),
                "g": round(min(self.tint[1] * shade, 1.0), 4),
                "b": round(min(self.tint[2] * shade, 1.0), 4),
                "a": 1,
            })
        self.i += [base, base + 1, base + 2]

    def section(self, z0, z1, r0, r1, shade, sides=8, cx=0.0, cy=0.0, squash=1.0):
        """A prism band between two cross-sections - the hull building block.

        r0/r1 are (halfwidth, halfheight) at each end, so a hull is a stack of
        these with differing profiles: chunky amidships, tapered fore and aft.
        """
        def ring(r):
            pts = []
            for k in range(sides):
                a = 2 * math.pi * (k + 0.5) / sides
                pts.append((cx + math.cos(a) * r[0],
                            cy + math.sin(a) * r[1] * (squash if math.sin(a) < 0 else 1.0)))
            return pts

        a0, a1 = ring(r0), ring(r1)
        for k in range(sides):
            j = (k + 1) % sides
            # Alternate a slight shade step per facet so adjacent panels read apart.
            s = shade * (0.94 + 0.06 * ((k % 3) / 2.0))
            self.quad((a0[k][0], a0[k][1], z0), (a0[j][0], a0[j][1], z0),
                      (a1[j][0], a1[j][1], z1), (a1[k][0], a1[k][1], z1), s)

    def cap(self, z, r, shade, sides=8, cx=0.0, cy=0.0, flip=False):
        pts = [(cx + math.cos(2 * math.pi * (k + 0.5) / sides) * r[0],
                cy + math.sin(2 * math.pi * (k + 0.5) / sides) * r[1]) for k in range(sides)]
        for k in range(1, sides - 1):
            a, b, c = pts[0], pts[k], pts[k + 1]
            if flip:
                self.tri((a[0], a[1], z), (c[0], c[1], z), (b[0], b[1], z), shade)
            else:
                self.tri((a[0], a[1], z), (b[0], b[1], z), (c[0], c[1], z), shade)

    def box(self, cx, cy, cz, sx, sy, sz, shade):
        hx, hy, hz = sx / 2, sy / 2, sz / 2
        x0, x1 = cx - hx, cx + hx
        y0, y1 = cy - hy, cy + hy
        z0, z1 = cz - hz, cz + hz
        # Faces shaded slightly apart so edges stay legible under flat light.
        self.quad((x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1), min(shade * 1.06, 1.0))
        self.quad((x0, y0, z0), (x0, y0, z1), (x1, y0, z1), (x1, y0, z0), shade * 0.80)
        self.quad((x0, y0, z1), (x0, y1, z1), (x1, y1, z1), (x1, y0, z1), shade * 1.00)
        self.quad((x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z0), shade * 0.86)
        self.quad((x1, y0, z1), (x1, y1, z1), (x1, y1, z0), (x1, y0, z0), shade * 0.93)
        self.quad((x0, y0, z0), (x0, y1, z0), (x0, y1, z1), (x0, y0, z1), shade * 0.93)

    def merge(self, other):
        base = len(self.v)
        self.v += other.v
        self.i += [n + base for n in other.i]

    def result(self):
        return self.v, self.i


# ------------------------------------------------------------------ greebles


def _profile_at(profile, L, B, z):
    """Half-width and half-height of the hull at a given z, by interpolating the
    section profile. Greebles have to be placed against this rather than against
    the hull's widest point, or they float free of the tapering bow and stern."""
    t = z / L
    for k in range(len(profile) - 1):
        z0, w0, h0 = profile[k]
        z1, w1, h1 = profile[k + 1]
        if z0 <= t <= z1:
            f = 0.0 if z1 == z0 else (t - z0) / (z1 - z0)
            return ((w0 + (w1 - w0) * f) * B, (h0 + (h1 - h0) * f) * B)
    return (profile[-1][1] * B, profile[-1][2] * B)


def _greeble_field(m, rng, profile, L, B, count, scale, shade):
    """Scatter small blocks over the hull's flanks and spine.

    This is what stops a hull reading as one smooth extrusion: the silhouette
    stays clean while the surface picks up shadow detail at close range. Each
    block is seated against the hull's local cross-section and sunk slightly into
    it, so nothing hangs off the ship as loose debris.
    """
    z0, z1 = profile[0][0] * L * 0.86, profile[-1][0] * L * 0.86
    for _ in range(count):
        z = rng.uniform(z0, z1)
        hw, hh = _profile_at(profile, L, B, z)
        if hw <= 0.05 * B:
            continue
        face = rng.choice(("top", "side", "side", "bottom"))
        a = rng.uniform(0.5, 1.6) * scale
        b = rng.uniform(0.25, 0.8) * scale
        c = rng.uniform(0.8, 3.0) * scale
        if face == "top":
            m.box(rng.uniform(-hw * 0.5, hw * 0.5), hh - b * 0.35, z,
                  a, b, c, shade * rng.uniform(0.82, 0.98))
        elif face == "bottom":
            m.box(rng.uniform(-hw * 0.45, hw * 0.45), -hh + b * 0.35, z,
                  a, b, c, shade * rng.uniform(0.62, 0.78))
        else:
            side = rng.choice((-1, 1))
            m.box(side * (hw - b * 0.35), rng.uniform(-hh * 0.35, hh * 0.45), z,
                  b, a, c, shade * rng.uniform(0.70, 0.88))


def _turret(m, x, y, z, scale, shade, barrel_dir=1):
    """Barbette, rotating housing and twin barrels."""
    m.section(z - 0.5 * scale, z + 0.35 * scale,
              (0.85 * scale, 0.85 * scale), (0.7 * scale, 0.7 * scale),
              shade * 0.9, sides=8, cx=x, cy=y)
    m.cap(z + 0.35 * scale, (0.7 * scale, 0.7 * scale), shade * 1.05, sides=8, cx=x, cy=y)
    m.box(x, y + 0.55 * scale, z, 1.15 * scale, 0.6 * scale, 1.5 * scale, shade * 1.02)
    for off in (-0.28, 0.28):
        m.box(x + off * scale, y + 0.6 * scale, z + barrel_dir * 1.5 * scale,
              0.16 * scale, 0.16 * scale, 2.2 * scale, shade * 0.78)


def _radiator(m, x, y, z, length, height, shade, tilt=0.0):
    """Thin angled heat-radiator panel."""
    t = math.tan(math.radians(tilt))
    m.quad((x, y, z - length / 2), (x, y + height, z - length / 2 + t * height),
           (x, y + height, z + length / 2 + t * height), (x, y, z + length / 2), shade * 1.1)
    m.quad((x, y, z + length / 2), (x, y + height, z + length / 2 + t * height),
           (x, y + height, z - length / 2 + t * height), (x, y, z - length / 2), shade * 0.7)


# -------------------------------------------------------------------- hulls


def dreadnought(length=64.0, beam=8.4, seed=11, tint=(0.30, 0.34, 0.41)):
    """Spinal capital ship: stepped hull, dorsal citadel, turret batteries,
    radiator wings and a four-nozzle drive block."""
    rng = random.Random(seed)
    m = Mesh(tint)
    L, B = length, beam / 2
    s = 0.80

    # Stepped main hull. Widths chosen so the silhouette has shoulders rather
    # than tapering smoothly from end to end.
    profile = [
        (-0.50, 0.16, 0.14), (-0.44, 0.44, 0.30), (-0.34, 0.70, 0.46),
        (-0.16, 0.86, 0.56), (0.02, 1.00, 0.60), (0.18, 0.94, 0.55),
        (0.30, 0.72, 0.44), (0.40, 0.48, 0.32), (0.47, 0.24, 0.18),
        (0.50, 0.08, 0.07),
    ]
    for k in range(len(profile) - 1):
        z0, w0, h0 = profile[k]
        z1, w1, h1 = profile[k + 1]
        m.section(z0 * L, z1 * L, (w0 * B, h0 * B), (w1 * B, h1 * B),
                  s * (0.97 + 0.03 * (k % 2)), sides=8)
    m.cap(profile[0][0] * L, (profile[0][1] * B, profile[0][2] * B), s * 0.8, sides=8, flip=True)
    m.cap(profile[-1][0] * L, (profile[-1][1] * B, profile[-1][2] * B), s * 1.05, sides=8)

    # Dorsal citadel: stepped superstructure with a bridge block on top.
    m.box(0, B * 0.72, -L * 0.06, B * 1.05, B * 0.42, L * 0.30, s * 1.04)
    m.box(0, B * 0.98, -L * 0.02, B * 0.72, B * 0.34, L * 0.20, s * 1.10)
    m.box(0, B * 1.20, L * 0.01, B * 0.44, B * 0.26, L * 0.11, s * 1.16)
    m.box(0, B * 1.38, L * 0.01, B * 0.16, B * 0.14, L * 0.05, s * 0.9)   # mast base
    m.box(0, B * 1.62, L * 0.01, B * 0.05, B * 0.34, B * 0.05, s * 0.8)   # sensor mast

    # Ventral keel and hangar lip.
    m.box(0, -B * 0.66, L * 0.04, B * 0.62, B * 0.30, L * 0.34, s * 0.84)
    m.box(0, -B * 0.80, -L * 0.20, B * 0.40, B * 0.16, L * 0.12, s * 0.7)

    # Main battery: turrets fore and aft along the spine.
    for z, d in ((0.30, 1), (0.20, 1), (-0.26, -1), (-0.36, -1)):
        _turret(m, 0, B * 0.55, z * L, B * 0.30, s, barrel_dir=d)
    # Secondary sponsons on the flanks.
    for z in (0.10, -0.04, -0.16):
        for side in (-1, 1):
            _turret(m, side * B * 0.86, B * 0.10, z * L, B * 0.17, s * 0.95, barrel_dir=1)

    # Radiator wings angled off the dorsal shoulders.
    for side in (-1, 1):
        _radiator(m, side * B * 0.95, B * 0.20, -L * 0.12, L * 0.26, B * 1.5, s * 0.75, tilt=18 * side)

    _greeble_field(m, rng, profile, L, B, 80, B * 0.10, s)
    return m.result()


def cruiser(length=34.0, beam=5.0, seed=23, tint=(0.33, 0.37, 0.44)):
    """Lighter escort: narrower hull, single dorsal fin, three turrets."""
    rng = random.Random(seed)
    m = Mesh(tint)
    L, B = length, beam / 2
    s = 0.82
    profile = [
        (-0.50, 0.20, 0.18), (-0.40, 0.52, 0.38), (-0.22, 0.82, 0.54),
        (0.00, 0.96, 0.58), (0.20, 0.80, 0.48), (0.36, 0.50, 0.32),
        (0.50, 0.12, 0.10),
    ]
    for k in range(len(profile) - 1):
        z0, w0, h0 = profile[k]
        z1, w1, h1 = profile[k + 1]
        m.section(z0 * L, z1 * L, (w0 * B, h0 * B), (w1 * B, h1 * B),
                  s * (0.97 + 0.03 * (k % 2)), sides=8)
    m.cap(profile[0][0] * L, (profile[0][1] * B, profile[0][2] * B), s * 0.8, sides=8, flip=True)
    m.cap(profile[-1][0] * L, (profile[-1][1] * B, profile[-1][2] * B), s * 1.05, sides=8)

    m.box(0, B * 0.70, -L * 0.02, B * 0.80, B * 0.40, L * 0.26, s * 1.05)
    m.box(0, B * 0.98, L * 0.03, B * 0.46, B * 0.26, L * 0.13, s * 1.13)
    m.box(0, B * 1.24, L * 0.03, B * 0.06, B * 0.30, B * 0.06, s * 0.8)
    m.box(0, -B * 0.62, L * 0.02, B * 0.46, B * 0.26, L * 0.28, s * 0.84)

    for z, d in ((0.26, 1), (-0.20, -1)):
        _turret(m, 0, B * 0.52, z * L, B * 0.26, s, barrel_dir=d)
    for side in (-1, 1):
        _turret(m, side * B * 0.78, B * 0.06, 0.02 * L, B * 0.15, s * 0.95)
    for side in (-1, 1):
        _radiator(m, side * B * 0.92, B * 0.16, -L * 0.10, L * 0.20, B * 1.1, s * 0.75, tilt=16 * side)

    _greeble_field(m, rng, profile, L, B, 40, B * 0.11, s)
    return m.result()


def fighter(length=3.4, beam=0.95, seed=7, tint=(0.38, 0.42, 0.49)):
    """Interceptor: faceted fuselage, swept wings, canopy, twin tails."""
    m = Mesh(tint)
    L, B = length, beam / 2
    s = 0.86
    profile = [
        (-0.50, 0.30, 0.30), (-0.30, 0.62, 0.52), (0.00, 0.74, 0.56),
        (0.26, 0.46, 0.36), (0.50, 0.10, 0.10),
    ]
    for k in range(len(profile) - 1):
        z0, w0, h0 = profile[k]
        z1, w1, h1 = profile[k + 1]
        m.section(z0 * L, z1 * L, (w0 * B, h0 * B), (w1 * B, h1 * B), s * 0.98, sides=6)
    m.cap(profile[0][0] * L, (profile[0][1] * B, profile[0][2] * B), s * 0.8, sides=6, flip=True)
    m.cap(profile[-1][0] * L, (profile[-1][1] * B, profile[-1][2] * B), s * 1.05, sides=6)

    # Swept delta wings.
    for side in (-1, 1):
        root_f, root_a = (0.16 * L, -0.28 * L)
        tipx = side * B * 3.0
        m.tri((0, 0, root_f), (tipx, B * 0.10, -0.10 * L), (0, 0, root_a), s * 1.08)
        m.tri((0, 0, root_a), (tipx, B * 0.10, -0.10 * L), (0, 0, root_f), s * 0.82)
        # Wingtip pod.
        m.box(tipx, B * 0.10, -0.10 * L, B * 0.30, B * 0.30, L * 0.22, s * 0.9)

    # Canopy and twin tail fins.
    m.box(0, B * 0.62, 0.06 * L, B * 0.42, B * 0.30, L * 0.24, s * 1.2)
    for side in (-1, 1):
        m.box(side * B * 0.42, B * 0.60, -0.34 * L, B * 0.08, B * 0.70, L * 0.16, s * 0.88)
    return m.result()


def drive(length, beam, nozzles=3, seed=5, tint=(0.70, 0.86, 1.0)):
    """Emissive drive block: a cluster of flared nozzles aft of the hull.

    Separate geometry because a Rekall.Material applies to a whole mesh - an
    emissive hull would glow end to end instead of only at the drives.
    """
    m = Mesh(tint)
    B = beam / 2
    z0 = -length * 0.5
    spread = B * 0.52
    centres = [(0.0, 0.0)] if nozzles == 1 else [
        (math.cos(2 * math.pi * i / nozzles) * spread,
         math.sin(2 * math.pi * i / nozzles) * spread * 0.55)
        for i in range(nozzles)
    ]
    for (cx, cy) in centres:
        r = B * 0.30
        m.section(z0 + length * 0.05, z0 - length * 0.005,
                  (r * 0.66, r * 0.66), (r, r), 0.85, sides=8, cx=cx, cy=cy)
        m.section(z0 - length * 0.005, z0 - length * 0.035,
                  (r, r), (r * 0.5, r * 0.5), 1.0, sides=8, cx=cx, cy=cy)
        m.cap(z0 - length * 0.035, (r * 0.5, r * 0.5), 1.0, sides=8, cx=cx, cy=cy, flip=True)
    return m.result()
