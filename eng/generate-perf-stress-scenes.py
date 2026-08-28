"""Throwaway generator for renderer scaling scenes.

Emits .age.scene.json files at several entity counts so the *slope* of each
cost can be read, not just one data point. Deliberately emits NO colliders:
the debug HUD (required to enable the frame profiler) builds collider
wireframes every frame, which would distort the measurement at high counts.

A configurable fraction of entities is placed BEHIND the camera so that
frustum culling has something to win.
"""
import json, os, sys, uuid

ROOT = r"F:\Dev\Rekall_AGE\.age-perf-stress"


def eid():
    return "ent_" + uuid.uuid4().hex


def cube(size, ox, oy, oz, tint):
    """8-vertex cube. Small vertex count keeps per-renderable overhead dominant."""
    h = size / 2.0
    corners = [(-h, -h, -h), (h, -h, -h), (h, h, -h), (-h, h, -h),
               (-h, -h, h), (h, -h, h), (h, h, h), (-h, h, h)]
    verts = [{"x": ox + x, "y": oy + y, "z": oz + z} for (x, y, z) in corners]
    idx = [0, 1, 2, 0, 2, 3, 5, 4, 7, 5, 7, 6, 4, 0, 3, 4, 3, 7,
           1, 5, 6, 1, 6, 2, 3, 2, 6, 3, 6, 7, 4, 5, 1, 4, 1, 0]
    return verts, idx


def grid(size, ox, oy, oz, tint, cells):
    """A denser mesh with the same footprint: isolates per-vertex cost from
    per-renderable cost when total vertex counts are matched."""
    verts, idx = [], []
    for r in range(cells + 1):
        for c in range(cells + 1):
            verts.append({"x": ox + (c / cells - 0.5) * size,
                          "y": oy,
                          "z": oz + (r / cells - 0.5) * size})
    for r in range(cells):
        for c in range(cells):
            a = r * (cells + 1) + c
            b = a + 1
            d = a + cells + 1
            e = d + 1
            idx += [a, d, b, b, d, e]
    return verts, idx


def entity(name, verts, idx, x, y, z, color):
    return {
        "id": eid(),
        "name": name,
        "tags": ["prop"],
        "parentId": None, "prefabSourceId": None, "visible": True, "locked": False,
        "components": [
            {"type": "Rekall.Transform3D", "properties": {"x": x, "y": y, "z": z}},
            {"type": "Rekall.GeometryMesh",
             "properties": {"vertices": verts, "indices": idx}},
            {"type": "Rekall.Material", "properties": {"baseColor": color}},
            {"type": "Rekall.MeshRenderer",
             "properties": {"active": True, "castShadows": False, "receiveShadows": False}},
        ],
    }


def scene(name, count, behind_fraction=0.5, mesh="cube", cells=8):
    ents = [{
        "id": eid(),
        "name": "Camera",
        "tags": ["camera"],
        "parentId": None, "prefabSourceId": None, "visible": True, "locked": False,
        "components": [
            {"type": "Rekall.Camera3D",
             "properties": {"active": True, "fieldOfViewDegrees": 60,
                            "nearClip": 0.1, "farClip": 500}},
            {"type": "Rekall.Transform3D",
             "properties": {"x": 0, "y": 6, "z": 0,
                            "rotationX": -10, "rotationY": 0, "rotationZ": 0}},
        ],
    }]

    per_row = max(1, int(count ** 0.5))
    behind = int(count * behind_fraction)
    for i in range(count):
        row, col = divmod(i, per_row)
        x = (col - per_row / 2) * 3.0
        # first `behind` entities go behind the camera (+Z); the rest in front (-Z)
        z = (row + 1) * 3.0 if i < behind else -(row + 1) * 3.0
        color = "#%02x%02x60" % (60 + (i * 7) % 190, 60 + (i * 13) % 190)
        if mesh == "cube":
            v, ix = cube(1.4, 0, 0, 0, i)
        else:
            v, ix = grid(4.0, 0, 0, 0, i, cells)
        ents.append(entity(f"Prop {i}", v, ix, x, 0.7, z, color))

    return {
        "schemaVersion": 1,
        "id": "scene_" + uuid.uuid4().hex,
        "name": name,
        "capabilities": ["rendering3d", "world"],
        "entities": ents,
    }


def write(doc):
    path = os.path.join(ROOT, "Scenes", doc["name"] + ".age.scene.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(doc, f)
    verts = sum(len(c["properties"]["vertices"])
                for e in doc["entities"] for c in e["components"]
                if c["type"] == "Rekall.GeometryMesh")
    print(f"{doc['name']:<12} entities={len(doc['entities'])-1:>5} vertices={verts:>7} "
          f"{os.path.getsize(path)/1e6:.1f} MB")


os.makedirs(os.path.join(ROOT, "Scenes"), exist_ok=True)
with open(os.path.join(ROOT, "rekall.project.json"), "w", encoding="utf-8") as f:
    json.dump({"name": "PerfStress", "schemaVersion": 1,
               "capabilities": ["rendering3d", "world"]}, f, indent=2)

for n in (50, 500, 2000, 5000):
    write(scene(f"Cubes{n}", n))

# Same total vertex budget as Cubes5000 (~40k verts) but concentrated in few
# renderables: separates per-vertex cost from per-renderable cost.
write(scene("Dense50", 50, mesh="grid", cells=28))
