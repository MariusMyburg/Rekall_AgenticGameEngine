"""Mission 1 "Standing Watch", and the debrief that follows it.

Deliberately a separate builder from build_scene.py rather than an extension of it.
build_menu.py imports build_scene.py to reuse the fleet as its backdrop, so anything
added there also lands in the main menu - and a MissionState in the menu would see
zero hostiles, declare victory on its first step, and drive the menu's ShellTransition
straight to the debrief.

Layout notes:
  * Everything that fights sits on the y=0 plane. A move order lands on the plane the
    vessel already occupies, so a single tactical layer is what keeps the cursor able
    to aim at all - a free 3D destination is unaimable with a 2D pointer.
  * The camera watches the lane broadside from -X, high enough to see the plane.
    Angles are solved from the engine's convention: forward = (cos(p)sin(y), -sin(p),
    cos(p)cos(y)), so positive pitch looks down and yaw 0 faces +Z.
  * The picket sits ~200 units up-lane from the squadron - beyond every weapon's
    reach. Nothing happens until the player orders someone to close, which is the
    point of the mission.

usage: python build_mission.py <output-directory>
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ships

ROOT = "F:/Dev/Rekall_AGE/Examples/StellarDominion"
TEX_GAS = "asset_tex-gasgiant_821048d2"
TEX_MOON = "asset_tex-moon_79224562"
TEX_RINGS = "asset_tex-rings_f9640733"

# Broadside, not down-lane. Looking along the transit lane put every hull nose-on, so
# a 90-unit dreadnought presented its 12-unit beam and read as a speck. From the side
# the lane runs across the screen and the ships show their length.
CAM = (-360, 150, 170)
CAM_PITCH, CAM_YAW = 22, 85
SUN = (-709, 1204, 1134)
SUN_PITCH, SUN_YAW = 42, 148


def e(name, tags, components, **kw):
    d = {"name": name, "tags": tags,
         "components": [{"type": t, "properties": p} for t, p in components]}
    d.update(kw)
    return d


def hull_material(base, metallic=0.25, rough=0.42):
    return ("Rekall.Material", {"baseColor": base, "metallicFactor": metallic,
                                "roughnessFactor": rough})


def drive_material(base="#bfe9ff", emissive="#8fd4ff", strength=4.0):
    return ("Rekall.Material", {"baseColor": base, "emissiveColor": emissive,
                                "emissiveStrength": strength, "roughnessFactor": 1.0})


def curtain():
    return e("Fade Curtain", ["ui"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.UiElement", {
            "x": 0, "y": 0, "width": 1920, "height": 1080,
            "text": "", "backgroundColor": "#000000ff",
            "foregroundColor": "#00000000", "fontSize": 1,
        }),
    ])


def space():
    """Sun, starfield and the two bodies. Shared by the mission and the debrief."""
    return [
        e("Environment", ["environment"], [
            ("Rekall.Environment3D", {
                "backgroundPolicy": "color", "backgroundColor": "#000000",
                "toneMapper": "agx", "exposure": 0.15, "whitePoint": 8.0,
                "ambientEnergy": 0.25, "ambientSkyColor": "#33507f",
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
            ("Rekall.MeshRenderer", {"active": True, "castShadows": False,
                                     "receiveShadows": False}),
            ("Rekall.Material", {"baseColor": "#fffaf0", "emissiveColor": "#fff3da",
                                 "emissiveStrength": 40.0, "roughnessFactor": 1.0}),
        ]),
        # Meridian sits far beyond the lane, straight down the view axis, so the fleet
        # is read against a lit disc instead of against empty black.
        e("Meridian", ["planet"], [
            ("Rekall.Transform3D", {"x": 1700, "y": 150, "z": 340, "roll": -13.5}),
            ("Rekall.PlanetRenderer", {
                "radius": 300, "color": "#ffffff", "surfaceTexture": TEX_GAS,
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
                "innerRadius": 415, "outerRadius": 745,
                "texture": TEX_RINGS, "color": "#ffffff", "segments": 384,
            }),
            ("Rekall.CelestialRotation", {
                "active": True, "siderealPeriodSeconds": 300, "tiltDegrees": -13.5,
            }),
        ]),
        e("Kell", ["planet"], [
            ("Rekall.Transform3D", {"x": 980, "y": 430, "z": -420}),
            ("Rekall.PlanetRenderer", {
                "radius": 60, "color": "#ffffff", "surfaceTexture": TEX_MOON,
                "meshSlices": 96, "meshStacks": 48, "waterCoverage": 0,
            }),
        ]),
        e("Camera", ["camera"], [
            ("Rekall.Transform3D", {"x": CAM[0], "y": CAM[1], "z": CAM[2],
                                    "pitch": CAM_PITCH, "yaw": CAM_YAW, "roll": 0}),
            ("Rekall.Camera3D", {"active": True, "fieldOfView": 62,
                                 "nearClip": 0.2, "farClip": 40000}),
        ]),
    ]


# --------------------------------------------------------------------- meshes

dread_v, dread_i = ships.dreadnought(length=90, beam=12.0)
cru_v, cru_i = ships.cruiser(length=52, beam=7.6)
fig_v, fig_i = ships.fighter(length=5.5, beam=1.5)
# Hull tint is baked into the vertex colours, not taken from the material: the
# renderer's ReadVertexColor uses the material baseColor only as a fallback, so two
# ships sharing a mesh share a colour however their materials differ. The Choir and
# the convoy therefore get their own tinted meshes - a hostile that looks like one of
# yours is not a difficulty setting, it is a bug.
choir_v, choir_i = ships.cruiser(length=46, beam=9.6, seed=41,
                                 tint=(0.34, 0.11, 0.15))
tank_v, tank_i = ships.cruiser(length=62, beam=12.0, seed=57,
                               tint=(0.42, 0.36, 0.26))

dread_d = ships.drive(90, 12.0, nozzles=4)
cru_d = ships.drive(52, 7.6, nozzles=3)
choir_d = ships.drive(46, 9.6, nozzles=3, tint=(1.0, 0.32, 0.42))
tank_d = ships.drive(62, 12.0, nozzles=2, tint=(1.0, 0.72, 0.42))


def warship(name, side, pos, yaw, mesh, drive, stats, weapon, order_speed,
            hull_color, drive_colors, story_critical=False, tags=("ship",)):
    """A hull that can be selected, ordered, shot at and lost.

    Weapon and Order live on the hull, not on a turret child: CombatSystem reads the
    target off the Order on the weapon's own entity, so splitting them across two
    entities would leave the ship permanently unable to fire.
    """
    unit_class, role, hull, hull_max, shields, shields_max, crew, radius = stats
    mv, mi = mesh
    dv, di = drive
    out = [e(name, list(tags), [
        ("Rekall.Transform3D", {"x": pos[0], "y": pos[1], "z": pos[2],
                                "pitch": 0, "yaw": yaw, "roll": 0}),
        ("Rekall.GeometryMesh", {"vertices": mv, "indices": mi}),
        ("Rekall.MeshRenderer", {"active": True, "castShadows": True,
                                 "receiveShadows": True}),
        hull_material(hull_color),
        ("Game.Modules.FleetRules.Faction", {
            "side": side, "storyCritical": story_critical, "destroyed": False,
        }),
        ("Game.Modules.FleetRules.Order", {
            "kind": "hold", "targetId": "", "x": pos[0], "y": pos[1], "z": pos[2],
            "speed": order_speed,
        }),
        ("Game.Modules.FleetRules.Selectable", {
            "enabled": True,
            "unitClass": unit_class, "role": role,
            "hull": hull, "hullMax": hull_max,
            "shields": shields, "shieldsMax": shields_max,
            "crew": crew, "selectRadius": radius,
        }),
    ] + ([("Game.Modules.FleetRules.Weapon", {
        "enabled": True, "range": weapon[0], "damage": weapon[1],
        "cycleSeconds": weapon[2], "cooldown": 0,
    })] if weapon else []))]

    out.append(e(f"{name} Drive", ["ship", "drive"], [
        ("Rekall.Transform3D", {"x": pos[0], "y": pos[1], "z": pos[2],
                                "pitch": 0, "yaw": yaw, "roll": 0}),
        ("Rekall.GeometryMesh", {"vertices": dv, "indices": di}),
        ("Rekall.MeshRenderer", {"active": True, "castShadows": False,
                                 "receiveShadows": False}),
        drive_material(*drive_colors),
    ]))
    return out


entities = space()

# --- The Compact squadron --------------------------------------------------
# (unitClass, role, hull, hullMax, shields, shieldsMax, crew, selectRadius)
entities += warship(
    "Ardent Dominion", "compact", (0, 0, 120), 0, (dread_v, dread_i), (dread_d),
    ("Dominion-class Dreadnought", "Fleet flagship",
     8400, 8400, 6000, 6000, 2140, 48),
    weapon=(115, 260, 2.4), order_speed=11,
    hull_color="#4d5560", drive_colors=("#bfe9ff", "#8fd4ff", 4.0),
    story_critical=True, tags=("ship", "capital"))

entities += warship(
    "Vigil of Kell", "compact", (-78, 0, 186), 0, (cru_v, cru_i), (cru_d),
    ("Kell-pattern Cruiser", "Screening element",
     3600, 3600, 2400, 2400, 620, 30),
    weapon=(98, 120, 1.6), order_speed=16,
    hull_color="#4d5560", drive_colors=("#bfe9ff", "#8fd4ff", 4.0),
    tags=("ship", "capital"))

entities += warship(
    "Long Watch", "compact", (84, 0, 62), 0, (cru_v, cru_i), (cru_d),
    ("Kell-pattern Cruiser", "Picket / early warning",
     3600, 3600, 2400, 2400, 604, 30),
    weapon=(98, 120, 1.6), order_speed=16,
    hull_color="#4d5560", drive_colors=("#bfe9ff", "#8fd4ff", 4.0),
    tags=("ship", "capital"))

# --- The convoy ------------------------------------------------------------
# Civilian: never acquired by the Choir, never counted as a loss. They are here to
# be the reason the lane matters, not to be a second health bar to babysit.
for index, (name, x) in enumerate((("Skimmer Ferrous", -46), ("Skimmer Anneal", 38))):
    entities += warship(
        name, "civilian", (x, 0, -70 - index * 60), 0, (tank_v, tank_i), (tank_d),
        ("Combine Bulk Tanker", "Fuel convoy", 1200, 1200, 0, 1, 44, 36),
        weapon=None, order_speed=6,
        hull_color="#6d6152", drive_colors=("#ffd9a8", "#ff9d4a", 2.2),
        tags=("ship", "civilian"))

# --- Fighter wings ---------------------------------------------------------
# Escort keeps a fighter on its patrol circle only while it has no standing order;
# ordering one detaches it, and it does not go back.
WINGS = [("Ardent Dominion", 6, 27.0), ("Vigil of Kell", 4, 17.0), ("Long Watch", 4, 17.0)]
for leader, count, radius in WINGS:
    leader_pos = next(
        c["properties"] for x in entities if x["name"] == leader
        for c in x["components"] if c["type"] == "Rekall.Transform3D")
    for k in range(count):
        a = 2 * math.pi * k / count
        entities.append(e(f"{leader} Fighter {k + 1}", ["ship", "fighter"], [
            ("Rekall.Transform3D", {
                "x": leader_pos["x"] + math.cos(a) * radius,
                "y": leader_pos["y"] + math.sin(a) * radius * 0.35,
                "z": leader_pos["z"] + math.sin(a) * radius,
                "pitch": 0, "yaw": math.degrees(a), "roll": 0,
            }),
            ("Rekall.GeometryMesh", {"vertices": fig_v, "indices": fig_i}),
            ("Rekall.MeshRenderer", {"active": True, "castShadows": True,
                                     "receiveShadows": True}),
            hull_material("#5b6470", rough=0.30),
            ("Game.Modules.FleetRules.Escort", {
                "enabled": True, "leader": leader, "radius": radius,
                "phase": math.degrees(a), "angularSpeed": 48.0,
                "inclination": 20.0 + 8.0 * k,
            }),
            ("Game.Modules.FleetRules.Faction", {
                "side": "compact", "storyCritical": False, "destroyed": False,
            }),
            ("Game.Modules.FleetRules.Order", {
                "kind": "hold", "targetId": "", "x": 0, "y": 0, "z": 0, "speed": 38,
            }),
            ("Game.Modules.FleetRules.Weapon", {
                "enabled": True, "range": 58, "damage": 22, "cycleSeconds": 1.1,
                "cooldown": 0,
            }),
            ("Game.Modules.FleetRules.Selectable", {
                "enabled": True,
                "unitClass": "Talon-series Interceptor",
                "role": f"{leader} escort wing",
                "hull": 140, "hullMax": 140,
                "shields": 90, "shieldsMax": 90,
                "crew": 1, "selectRadius": 4.2,
            }),
        ]))

# --- The Hollow Choir picket -----------------------------------------------
# Up-lane and out of everyone's reach. Nothing happens until the player closes.
CHOIR = [("Choir Node Ashen", -74, 360), ("Choir Node Salt", 14, 402),
         ("Choir Node Hymn", 96, 352)]
for name, x, z in CHOIR:
    entities += warship(
        name, "choir", (x, 0, z), 180, (choir_v, choir_i), (choir_d),
        ("Choir Picket Node", "Standing interdiction", 900, 900, 400, 400, 0, 28),
        weapon=(90, 70, 2.0), order_speed=10,
        hull_color="#3a2028", drive_colors=("#ffb9c8", "#ff3f5e", 5.0),
        tags=("ship", "hostile"))

# --- Command layer ---------------------------------------------------------
# MissionState sits on the same entity as ShellTransition: MissionSystem only hands
# over to the debrief when it finds both on the host.
entities.append(e("Shell", ["flow"], [
    ("Rekall.Transform3D", {}),
    ("Game.Modules.FleetRules.ShellTransition", {
        "enabled": True, "phase": "fadingIn", "elapsed": 0,
        "fadeInSeconds": 1.6, "fadeOutSeconds": 1.6,
        "targetScene": "", "overlayEntityName": "Fade Curtain",
        "musicEntityName": "", "musicGain": 0.0,
    }),
    ("Game.Modules.FleetRules.MissionState", {
        "title": "MISSION 1 - STANDING WATCH",
        "objective": "Clear the Choir picket from the transit lane.",
        "outcome": "active", "engaged": False,
        "panelEntityName": "Objective Panel",
        "debriefScene": "Debrief",
        "endDelaySeconds": 4.0, "elapsed": 0,
    }),
    ("Rekall.PersistentState", {
        "slot": "campaign",
        "document": {"lastMission": "", "lastOutcome": "",
                     "lastLosses": 0, "lastCriticalLoss": False},
    }),
    ("Rekall.SceneTransition", {"requestedScene": "", "reason": "mission"}),
]))

entities.append(e("Tactical HUD", ["ui"], [
    ("Rekall.Transform3D", {}),
    ("Rekall.UiCanvas", {"referenceWidth": 1920, "referenceHeight": 1080}),
    ("Game.Modules.FleetRules.FleetCommand", {
        "enabled": True, "selectedEntityId": "", "selectedName": "",
        "panelEntityName": "Unit Panel",
    }),
]))

entities.append(e("Objective Panel", ["ui"], [
    ("Rekall.Transform3D", {}),
    ("Rekall.UiElement", {
        "x": 36, "y": 36, "width": 560, "height": 128,
        "text": "MISSION 1 - STANDING WATCH",
        "backgroundColor": "#0a1520e0", "foregroundColor": "#cfe9ff",
        "borderColor": "#3f7fa8", "borderWidth": 1.5,
        "fontSize": 19, "fontFamily": "Consolas",
    }),
]))

entities.append(e("Unit Panel", ["ui"], [
    ("Rekall.Transform3D", {}),
    ("Rekall.UiElement", {
        "x": 36, "y": 700, "width": 470, "height": 230,
        "text": "NO UNIT SELECTED\nClick a vessel to inspect it.",
        "backgroundColor": "#0a1520e0", "foregroundColor": "#b8e4ff",
        "borderColor": "#3f7fa8", "borderWidth": 1.5,
        "fontSize": 17, "fontFamily": "Consolas",
    }),
]))

entities.append(e("Controls Hint", ["ui"], [
    ("Rekall.Transform3D", {}),
    ("Rekall.UiElement", {
        "x": 36, "y": 946, "width": 1020, "height": 56,
        "text": "LEFT CLICK select    RIGHT CLICK move, or engage what is under the cursor",
        "backgroundColor": "#00000000", "foregroundColor": "#6f96b4",
        "fontSize": 17, "fontFamily": "Consolas",
    }),
]))

entities.append(curtain())

# ------------------------------------------------------------------- debrief

debrief = space() + [
    e("Shell", ["flow"], [
        ("Rekall.Transform3D", {}),
        ("Game.Modules.FleetRules.ShellTransition", {
            "enabled": True, "phase": "fadingIn", "elapsed": 0,
            "fadeInSeconds": 1.6, "fadeOutSeconds": 1.2,
            "targetScene": "", "overlayEntityName": "Fade Curtain",
            "musicEntityName": "", "musicGain": 0.0,
        }),
        ("Rekall.SceneTransition", {"requestedScene": "", "reason": "debrief"}),
    ]),
    # Same slot the mission wrote to. The runtime has it loaded before the first step,
    # which is the only way the result survives the scene change.
    e("Campaign Store", ["flow"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.PersistentState", {
            "slot": "campaign",
            "document": {"lastMission": "", "lastOutcome": "",
                         "lastLosses": 0, "lastCriticalLoss": False},
        }),
        ("Game.Modules.FleetRules.DebriefPanel", {
            "enabled": True,
            "textEntityName": "Debrief Text",
            "headlineEntityName": "Debrief Headline",
        }),
    ]),
    e("Debrief Canvas", ["ui"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.UiCanvas", {"referenceWidth": 1920, "referenceHeight": 1080}),
    ]),
    e("Debrief Headline", ["ui"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.UiElement", {
            "x": 200, "y": 170, "width": 1100, "height": 90,
            "text": "OPERATION COMPLETE", "backgroundColor": "#00000000",
            "foregroundColor": "#dff0ff", "fontSize": 54,
            "fontFamily": "Consolas", "fontWeight": "bold",
        }),
    ]),
    e("Debrief Text", ["ui"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.UiElement", {
            "x": 200, "y": 300, "width": 1400, "height": 420,
            "text": "", "backgroundColor": "#00000000",
            "foregroundColor": "#c8e2f5", "fontSize": 25,
            "fontFamily": "Consolas",
        }),
    ]),
    e("Button Return", ["ui", "menu"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.UiElement", {
            "x": 200, "y": 780, "width": 420, "height": 58,
            "text": "  RETURN TO COMMAND",
            "backgroundColor": "#0a1520c0", "foregroundColor": "#b8e4ff",
            "borderColor": "#3f7fa8", "borderWidth": 1.5,
            "fontSize": 24, "fontFamily": "Consolas", "interactive": True,
        }),
        ("Game.Modules.FleetRules.MenuAction", {
            "enabled": True, "action": "loadScene", "targetScene": "MainMenu",
        }),
    ]),
    curtain(),
]


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    for scene_name, scene in (("Mission1", entities), ("Debrief", debrief)):
        path = os.path.join(out, f"scene_{scene_name}.json")
        with open(path, "w", encoding="utf-8") as handle:
            json.dump({"projectRoot": ROOT, "sceneName": scene_name,
                       "clearExisting": True, "entities": scene}, handle)
        print(f"{scene_name}: {len(scene)} entities -> {path}")
