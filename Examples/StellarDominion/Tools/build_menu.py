"""Main menu scene for Stellar Dominion.

The fleet drifts past the gas giant behind the title while the menu theme plays.
Both the picture and the music fade in on arrival and back out before the next
scene loads - the ShellSystem owns that so the outward fade actually finishes
before Rekall.SceneTransition is written.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_scene as base

ROOT = "F:/Dev/Rekall_AGE/Examples/StellarDominion"
MUSIC = "asset_menu-theme_e91ed2cf"


def e(name, tags, components, **kw):
    d = {"name": name, "tags": tags,
         "components": [{"type": t, "properties": p} for t, p in components]}
    d.update(kw)
    return d


# Reuse the fleet scene's environment, lighting, planet and ships as the backdrop,
# then drop the mission-only pieces (the tactical HUD) on top of it.
entities = [x for x in base.entities if x["name"] not in ("Tactical HUD", "Unit Panel")]

# Bake the framing into the scene. build_scene leaves the camera unrotated and
# relies on rekall.level.camera.aim_at being run afterwards; a menu has to stand
# on its own, so the solved pitch/yaw for "look at the planet" live here instead.
# Pulled left of centre so the fleet sits opposite the title block.
for entity in entities:
    if entity["name"] != "Camera":
        continue
    for component in entity["components"]:
        if component["type"] == "Rekall.Transform3D":
            component["properties"] = {
                "x": 128, "y": 96, "z": 214,
                "pitch": 22.0, "yaw": -150.0, "roll": 0,
            }
        elif component["type"] == "Rekall.Camera3D":
            component["properties"]["fieldOfView"] = 52

# --- Shell -----------------------------------------------------------------
entities.append(e("Shell", ["flow"], [
    ("Rekall.Transform3D", {}),
    ("Game.Modules.FleetRules.ShellTransition", {
        "enabled": True,
        "phase": "fadingIn",
        "elapsed": 0,
        "fadeInSeconds": 3.0,
        "fadeOutSeconds": 2.0,
        "targetScene": "",
        "overlayEntityName": "Fade Curtain",
        "musicEntityName": "Menu Music",
        "musicGain": 0.85,
    }),
    ("Rekall.SceneTransition", {"requestedScene": "", "reason": "main menu"}),
]))

# --- Music -----------------------------------------------------------------
# Gain starts at 0 and is ramped by ShellSystem, so the track fades up rather
# than starting at full volume the instant the scene loads.
entities.append(e("Menu Music", ["audio"], [
    ("Rekall.Transform3D", {}),
    ("Rekall.AudioEmitter", {
        "clip": MUSIC,
        "playing": True,
        "loop": True,
        "gain": 0.0,
    }),
]))
entities.append(e("Listener", ["audio"], [
    ("Rekall.Transform3D", {}),
    ("Rekall.AudioListener", {"active": True}),
]))

# --- UI --------------------------------------------------------------------
entities.append(e("Menu Canvas", ["ui"], [
    ("Rekall.Transform3D", {}),
    ("Rekall.UiCanvas", {"referenceWidth": 1920, "referenceHeight": 1080}),
]))

entities.append(e("Title", ["ui"], [
    ("Rekall.Transform3D", {}),
    ("Rekall.UiElement", {
        "x": 140, "y": 150, "width": 1000, "height": 120,
        "text": "STELLAR DOMINION",
        "backgroundColor": "#00000000",
        "foregroundColor": "#dff0ff",
        "fontSize": 76,
        "fontFamily": "Consolas",
        "fontWeight": "bold",
    }),
]))

entities.append(e("Subtitle", ["ui"], [
    ("Rekall.Transform3D", {}),
    ("Rekall.UiElement", {
        "x": 144, "y": 272, "width": 1000, "height": 48,
        "text": "The Meridian Reach  ---  a war nobody called off",
        "backgroundColor": "#00000000",
        "foregroundColor": "#7fa8c8",
        "fontSize": 26,
        "fontFamily": "Consolas",
    }),
]))

BUTTONS = [
    ("New Campaign", "Main", 420),
    ("Settings", "Settings", 500),
]
for label, scene, y in BUTTONS:
    entities.append(e(f"Button {label}", ["ui", "menu"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.UiElement", {
            "x": 144, "y": y, "width": 380, "height": 58,
            "text": "  " + label.upper(),
            "backgroundColor": "#0a1520c0",
            "foregroundColor": "#b8e4ff",
            "borderColor": "#3f7fa8",
            "borderWidth": 1.5,
            "fontSize": 28,
            "fontFamily": "Consolas",
            "interactive": True,
        }),
        ("Game.Modules.FleetRules.MenuAction", {
            "enabled": True, "action": "loadScene", "targetScene": scene,
        }),
    ]))

# Fade curtain last so it renders over everything else.
entities.append(e("Fade Curtain", ["ui"], [
    ("Rekall.Transform3D", {}),
    ("Rekall.UiElement", {
        "x": 0, "y": 0, "width": 1920, "height": 1080,
        "text": "",
        "backgroundColor": "#000000ff",
        "foregroundColor": "#00000000",
        "fontSize": 1,
    }),
]))

print(json.dumps({
    "projectRoot": ROOT,
    "sceneName": "MainMenu",
    "clearExisting": True,
    "entities": entities,
}))
