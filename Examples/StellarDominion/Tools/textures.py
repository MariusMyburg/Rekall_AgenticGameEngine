"""Procedural equirectangular textures for StellarDominion.

Pure-stdlib PNG writing (zlib + struct) so this needs no image library. The gas
giant's banding is the single biggest realism lever available without authored
art: PlanetRenderer with only a flat Color reads as a featureless ball.
"""
import math
import os
import struct
import zlib

OUT = os.path.dirname(os.path.abspath(__file__))


def write_png(path, width, height, rgb_rows):
    raw = b"".join(b"\x00" + bytes(row) for row in rgb_rows)
    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9))
    png += chunk(b"IEND", b"")
    open(path, "wb").write(png)
    print("wrote", path, os.path.getsize(path), "bytes")


def _hash(x, y, seed):
    n = (x * 374761393 + y * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFF) / 65535.0


def _vnoise(x, y, seed):
    xi, yi = int(math.floor(x)), int(math.floor(y))
    xf, yf = x - xi, y - yi
    u = xf * xf * (3 - 2 * xf)
    v = yf * yf * (3 - 2 * yf)
    a = _hash(xi, yi, seed); b = _hash(xi + 1, yi, seed)
    c = _hash(xi, yi + 1, seed); d = _hash(xi + 1, yi + 1, seed)
    return (a * (1 - u) + b * u) * (1 - v) + (c * (1 - u) + d * u) * v


def fbm(x, y, seed, octaves=5):
    total, amp, freq, norm = 0.0, 1.0, 1.0, 0.0
    for _ in range(octaves):
        total += _vnoise(x * freq, y * freq, seed) * amp
        norm += amp
        amp *= 0.5
        freq *= 2.0
    return total / norm


def lerp3(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def _cyl(lon, turns):
    """Longitude mapped onto a circle so noise sampled from it wraps seamlessly.

    Sampling noise directly from a 0..1 longitude leaves u=0 and u=1 uncorrelated,
    which shows up on the rendered globe as a hard vertical seam down one side.
    """
    a = lon * 2.0 * math.pi
    return math.cos(a) * turns, math.sin(a) * turns


def gas_giant(width=2048, height=1024, seed=7):
    """Banded gas giant. Latitude drives the palette; turbulence warps the band
    boundaries so they meander like real Jovian belts rather than sitting in
    perfect stripes. All longitude-varying noise is sampled on a cylinder so the
    equirectangular map wraps without a seam."""
    palette = [
        (0.29, 0.20, 0.14), (0.71, 0.56, 0.38), (0.42, 0.29, 0.20),
        (0.85, 0.74, 0.56), (0.55, 0.39, 0.26), (0.78, 0.65, 0.47),
        (0.36, 0.25, 0.18), (0.88, 0.80, 0.64),
    ]
    rows = []
    for j in range(height):
        lat = j / (height - 1)
        row = bytearray()
        for i in range(width):
            lon = i / (width - 1)
            # Warp latitude by turbulence, then quantise into soft bands.
            cx, cy = _cyl(lon, 4.0)
            warp = (fbm(cx + 8.0, cy + lat * 26.0, seed, 5) - 0.5) * 0.10
            cx2, cy2 = _cyl(lon, 1.5)
            warp += (fbm(cx2 + 3.0, cy2 + lat * 7.0, seed + 11, 4) - 0.5) * 0.05
            band = (lat + warp) * len(palette) * 1.35
            k = int(band) % len(palette)
            k2 = (k + 1) % len(palette)
            base = lerp3(palette[k], palette[k2], min(1.0, (band - int(band)) * 1.6))
            # Fine turbulence for storm texture.
            dx, dy = _cyl(lon, 13.0)
            detail = fbm(dx + 20.0, dy + lat * 80.0, seed + 3, 4)
            base = tuple(c * (0.86 + 0.28 * detail) for c in base)
            # Polar darkening.
            polar = abs(lat - 0.5) * 2.0
            base = tuple(c * (1.0 - 0.35 * polar ** 3) for c in base)
            row += bytes(max(0, min(255, int(c * 255))) for c in base)
        rows.append(row)
    return width, height, rows


def rocky_moon(width=1024, height=512, seed=41):
    rows = []
    for j in range(height):
        lat = j / (height - 1)
        row = bytearray()
        for i in range(width):
            lon = i / (width - 1)
            mx, my = _cyl(lon, 6.0)
            n = fbm(mx + 5.0, my + lat * 18.0, seed, 6)
            kx, ky = _cyl(lon, 17.0)
            craters = fbm(kx + 30.0, ky + lat * 55.0, seed + 5, 3)
            v = 0.34 + 0.34 * n - 0.16 * (craters ** 3)
            tint = (v * 1.02, v * 0.98, v * 0.93)
            row += bytes(max(0, min(255, int(c * 255))) for c in tint)
        rows.append(row)
    return width, height, rows


def ring_strip(width=2048, height=8, seed=97):
    """Radial ring density as a wide, short strip: gaps and bright bands."""
    rows = []
    base_row = bytearray()
    for i in range(width):
        r = i / (width - 1)
        d = 0.55 + 0.45 * fbm(r * 60.0, 0.5, seed, 5)
        for gap, w in ((0.18, 0.012), (0.42, 0.02), (0.61, 0.008), (0.79, 0.016)):
            d *= 1.0 - 0.92 * math.exp(-((r - gap) / w) ** 2)
        d *= 0.35 + 0.65 * min(1.0, (1.0 - abs(r - 0.5) * 1.6))
        c = (d * 0.92, d * 0.84, d * 0.70)
        base_row += bytes(max(0, min(255, int(v * 255))) for v in c)
    for _ in range(height):
        rows.append(base_row)
    return width, height, rows


if __name__ == "__main__":
    for name, fn in (("gasgiant", gas_giant), ("moon", rocky_moon), ("rings", ring_strip)):
        w, h, rows = fn()
        write_png(os.path.join(OUT, f"tex_{name}.png"), w, h, rows)
