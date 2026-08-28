"""StellarDominion scene blueprint.

Notes learned from the renderer while building this:
  * Directional-light direction comes from the light's Euler rotation, not its
    position, and intensity is clamped to 4.0. Angles below were solved
    numerically against the engine's DirectionFromEuler convention.
  * spaceAmbientFloor() is 0 for bodies with atmosphere data, so an unlit face is
    a true black silhouette. Composition has to put light where you want detail.
  * Environment.backgroundColor must be #RRGGBB - the background resolver rejects
    an 8-digit #RRGGBBAA and silently falls back to a light default.
"""
import json
import math
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ships

ROOT = "F:/Dev/Rekall_AGE/Examples/StellarDominion"
TEX_GAS = "asset_tex-gasgiant_127c10c8"
TEX_MOON = "asset_tex-moon_d6416558"
TEX_RINGS = "asset_tex-rings_f9640733"

CAM = (96, 104, 196)
# Phase angle (sun-planet-camera) is what sets how much of the disc is lit, not
# the sun's angle from the view axis. 50 deg phase leaves ~82% of Meridian lit and
# puts the sun high above the ring plane so the rings cast a shadow band.
SUN = (-709, 1204, 1134)
SUN_PITCH, SUN_YAW = 42, 148


def e(name, tags, components, **kw):
    d = {"name": name, "tags": tags,
         "components": [{"type": t, "properties": p} for t, p in components]}
    d.update(kw)
    return d


def hull_material(base, emissive=None, strength=0.0, metallic=0.25, rough=0.42):
    props = {
        "baseColor": base,
        "metallicFactor": metallic,
        "roughnessFactor": rough,
    }
    if emissive:
        props["emissiveColor"] = emissive
        props["emissiveStrength"] = strength
    return ("Rekall.Material", props)


entities = [
    e("Environment", ["environment"], [
        ("Rekall.Environment3D", {
            "backgroundPolicy": "color",
            "backgroundColor": "#000000",
            "toneMapper": "agx",
            "exposure": 0.15,
            "whitePoint": 8.0,
            "ambientEnergy": 0.25,
            "ambientSkyColor": "#33507f",
            "ambientGroundColor": "#0a0810",
        }),
    ]),

    e("Post", ["post"], [
        ("Rekall.PostProcessStack", {
            "enabled": True,
            "passes": [
                {"name": "bright", "type": "brightExtract", "threshold": 1.05, "scale": 4.0},
                {"name": "blurA", "type": "blur", "iterations": 5, "radius": 3.0},
                {"name": "composite", "type": "composite", "intensity": 1.0, "blendMode": "add"},
                # Camera lens grime: scatters the bloom rather than overlaying the
                # finished image, so it only shows where something is actually bright.
                {"name": "dirt", "type": "lensDirt", "intensity": 0.55, "scale": 1.0},
            ],
        }),
    ]),

    e("Starfield", ["backdrop"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.StarfieldRenderer", {
            "count": 8000, "radius": 16000, "size": 2.1, "seed": 20260828,
            "color": "#dfe9ffff", "brightness": 2.6, "milkyWayStrength": 0.5,
            "active": True,
        }),
    ]),

    e("Sun Light", ["light"], [
        ("Rekall.Transform3D", {"x": SUN[0], "y": SUN[1], "z": SUN[2],
                                "pitch": SUN_PITCH, "yaw": SUN_YAW, "roll": 0}),
        ("Rekall.DirectionalLight", {"intensity": 4.0, "color": "#fff4e2"}),
    ]),
    e("Sun Disc", ["light"], [
        ("Rekall.Transform3D", {"x": SUN[0], "y": SUN[1], "z": SUN[2],
                                "scaleX": 74, "scaleY": 74, "scaleZ": 74}),
        ("Rekall.GeometryPrimitive", {"primitive": "sphere", "color": "#fffaf0"}),
        ("Rekall.MeshRenderer", {"active": True, "castShadows": False, "receiveShadows": False}),
        ("Rekall.Material", {"baseColor": "#fffaf0", "emissiveColor": "#fff3da",
                             "emissiveStrength": 40.0, "roughnessFactor": 1.0}),
    ]),

    e("Meridian", ["planet"], [
        ("Rekall.Transform3D", {"x": 0, "y": 0, "z": 0, "roll": -13.5}),
        ("Rekall.PlanetRenderer", {
            "radius": 42, "color": "#ffffff", "surfaceTexture": TEX_GAS,
            "meshSlices": 192, "meshStacks": 96,
            "waterCoverage": 0, "waterSpecularStrength": 0,
        }),
        ("Rekall.AtmosphereRenderer", {
            "height": 0.055, "renderShell": True, "rayleighColor": "#9dc0ff",
            "density": 2.6, "densityFalloff": 0.2, "rayleighScattering": 0.008,
            "mieScattering": 0.006, "mieAnisotropy": 0.78,
            "mieColor": "#ffe9cc", "ozoneAbsorptionColor": "#ffcf9a",
        }),
        ("Rekall.RingRenderer", {
            "innerRadius": 58, "outerRadius": 104,
            "texture": TEX_RINGS, "color": "#ffffff", "segments": 384,
        }),
        ("Rekall.CelestialRotation", {
            "active": True, "siderealPeriodSeconds": 300, "tiltDegrees": -13.5,
        }),
    ]),

    e("Kell", ["planet"], [
        ("Rekall.Transform3D", {"x": 150, "y": 30, "z": -120}),
        ("Rekall.PlanetRenderer", {
            "radius": 9, "color": "#ffffff", "surfaceTexture": TEX_MOON,
            "meshSlices": 96, "meshStacks": 48, "waterCoverage": 0,
        }),
    ]),

    e("Camera", ["camera"], [
        ("Rekall.Transform3D", {"x": CAM[0], "y": CAM[1], "z": CAM[2],
                                "pitch": 0, "yaw": 0, "roll": 0}),
        ("Rekall.Camera3D", {"active": True, "fieldOfView": 62,
                             "nearClip": 0.2, "farClip": 40000}),
    ]),
]

# --- Fleet -----------------------------------------------------------------
# Capitals sit between the camera and the planet so they read as silhouettes with
# lit upper surfaces; fighter wings ring each capital.
# The lead ship is a foreground element - large and close enough to read as a
# vessel with structure, not a speck against the planet. The escorts sit further
# back to give the shot depth.
dread_v, dread_i = ships.dreadnought(length=64, beam=8.4)
cru_v, cru_i = ships.cruiser(length=34, beam=5.0)
fig_v, fig_i = ships.fighter(length=3.4, beam=0.95)

dread_d = ships.drive(64, 8.4, nozzles=4)
cru_d = ships.drive(34, 5.0, nozzles=3)

CAPITALS = [
    ("Ardent Dominion", "dreadnought", (-16, -22, 128), (2, -28, -6), (dread_v, dread_i), dread_d, 9, 17.0),
    ("Vigil of Kell", "cruiser", (74, 34, 84), (0, 22, 4), (cru_v, cru_i), cru_d, 6, 10.5),
    ("Long Watch", "cruiser", (-86, 30, 46), (-3, -58, 0), (cru_v, cru_i), cru_d, 5, 9.0),
]

for name, cls, pos, rot, (mv, mi), (dv, di), wing, radius in CAPITALS:
    entities.append(e(name, ["ship", "capital"], [
        ("Rekall.Transform3D", {"x": pos[0], "y": pos[1], "z": pos[2],
                                "pitch": rot[0], "yaw": rot[1], "roll": rot[2]}),
        ("Rekall.GeometryMesh", {"vertices": mv, "indices": mi}),
        ("Rekall.MeshRenderer", {"active": True, "castShadows": True, "receiveShadows": True}),
        hull_material("#7d8794"),
        ("Game.Modules.FleetRules.Drift", {
            "enabled": True,
            "speed": 0.9 if cls == "dreadnought" else 1.4,
            "headingYaw": rot[1],
        }),
    ]))

    # Engine block: a short emissive stub trailing the hull. This is the only
    # emissive surface on a ship, so bloom picks out drives rather than the whole
    # silhouette.
    entities.append(e(f"{name} Drive", ["ship", "drive"], [
        ("Rekall.Transform3D", {"x": pos[0], "y": pos[1], "z": pos[2],
                                "pitch": rot[0], "yaw": rot[1], "roll": rot[2],
                                "scaleX": 1.0, "scaleY": 1.0, "scaleZ": 1.0}),
        ("Rekall.GeometryMesh", {"vertices": dv, "indices": di}),
        ("Rekall.MeshRenderer", {"active": True, "castShadows": False, "receiveShadows": False}),
        ("Rekall.Material", {"baseColor": "#bfe9ff", "emissiveColor": "#8fd4ff",
                             "emissiveStrength": 4.0, "roughnessFactor": 1.0}),
    ]))

    for k in range(wing):
        a = 2 * math.pi * k / wing
        entities.append(e(f"{name} Fighter {k + 1}", ["ship", "fighter"], [
            ("Rekall.Transform3D", {
                "x": pos[0] + math.cos(a) * radius,
                "y": pos[1] + math.sin(a) * radius * 0.35,
                "z": pos[2] + math.sin(a) * radius,
                "pitch": 0, "yaw": math.degrees(a), "roll": 0,
            }),
            ("Rekall.GeometryMesh", {"vertices": fig_v, "indices": fig_i}),
            ("Rekall.MeshRenderer", {"active": True, "castShadows": True, "receiveShadows": True}),
            hull_material("#98a1ab", rough=0.28),
            ("Game.Modules.FleetRules.Escort", {
                "enabled": True,
                "leader": name,
                "radius": radius,
                "phase": math.degrees(a),
                "angularSpeed": 42.0 if cls == "dreadnought" else 55.0,
                "inclination": 20.0 + 8.0 * k,
            }),
        ]))

payload = {"projectRoot": ROOT, "sceneName": "Main",
           "clearExisting": True, "entities": entities}
print(json.dumps(payload))
