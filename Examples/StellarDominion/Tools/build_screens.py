"""Builds the Intro and Settings scenes, and writes each blueprint to a file.

Run this, then apply each file with mcp_client.py. Kept separate from build_menu.py
because these two screens do not reuse the fleet backdrop.

usage: python build_screens.py <output-directory>
"""
import json
import os
import sys

ROOT = "F:/Dev/Rekall_AGE/Examples/StellarDominion"


def e(name, tags, components, **kw):
    d = {"name": name, "tags": tags,
         "components": [{"type": t, "properties": p} for t, p in components]}
    d.update(kw)
    return d


def curtain():
    """Full-screen fade curtain. Authored last so it draws over everything."""
    return e("Fade Curtain", ["ui"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.UiElement", {
            "x": 0, "y": 0, "width": 1920, "height": 1080,
            "text": "", "backgroundColor": "#000000ff",
            "foregroundColor": "#00000000", "fontSize": 1,
        }),
    ])


def backdrop():
    """A quiet starfield, so neither screen is a flat black rectangle."""
    return [
        e("Environment", ["environment"], [
            ("Rekall.Environment3D", {
                "backgroundPolicy": "color", "backgroundColor": "#000000",
                "toneMapper": "agx", "exposure": 0.1, "whitePoint": 8.0,
                "ambientEnergy": 0.2,
            }),
        ]),
        e("Starfield", ["backdrop"], [
            ("Rekall.Transform3D", {}),
            ("Rekall.StarfieldRenderer", {
                "count": 6000, "radius": 16000, "size": 1.9, "seed": 4242,
                "color": "#dfe9ffff", "brightness": 2.0, "milkyWayStrength": 0.45,
                "active": True,
            }),
        ]),
        e("Camera", ["camera"], [
            ("Rekall.Transform3D", {"x": 0, "y": 0, "z": 0, "pitch": 6, "yaw": 24, "roll": 0}),
            ("Rekall.Camera3D", {"active": True, "fieldOfView": 60,
                                 "nearClip": 0.2, "farClip": 40000}),
        ]),
    ]


# --------------------------------------------------------------------- intro

# Original prose for this campaign. Deliberately plain: the premise does the work.
PROLOGUE = [
    "MERIDIAN REACH",
    "Forty-one years since the relays failed.",
    "",
    "When the jump network collapsed, the Reach was left holding three things:",
    "a gas giant full of fuel, one habitable moon, and more warships than",
    "anyone left alive could crew.",
    "",
    "The Ardent Compact still flies patrols. Still keeps the skimmers safe.",
    "Still maintains hulls that cannot be replaced, for a war that ended",
    "before most of the crews were born.",
    "",
    "Nobody told the Hollow Choir.",
    "",
    "Their platforms never received a stand-down order. They do not raid.",
    "They do not negotiate. There is nobody inside them to negotiate with.",
    "They simply continue, exactly and patiently, the task they were given.",
    "",
    "You command what is left of the Compact's capital squadron.",
    "",
    "Today you are escorting fuel.",
]

intro = backdrop() + [
    e("Shell", ["flow"], [
        ("Rekall.Transform3D", {}),
        ("Game.Modules.FleetRules.ShellTransition", {
            "enabled": True, "phase": "fadingIn", "elapsed": 0,
            "fadeInSeconds": 2.0, "fadeOutSeconds": 2.0,
            "targetScene": "", "overlayEntityName": "Fade Curtain",
            "musicEntityName": "", "musicGain": 0.0,
        }),
        ("Game.Modules.FleetRules.IntroSequence", {
            "enabled": True,
            "lines": PROLOGUE,
            "elapsed": 0,
            "charactersPerSecond": 46,
            "holdSeconds": 5,
            "targetScene": "Mission1",
            "textEntityName": "Prologue Text",
            "promptEntityName": "Prompt",
            "finished": False,
        }),
        ("Rekall.SceneTransition", {"requestedScene": "", "reason": "prologue"}),
    ]),
    e("Intro Canvas", ["ui"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.UiCanvas", {"referenceWidth": 1920, "referenceHeight": 1080}),
    ]),
    e("Prologue Text", ["ui"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.UiElement", {
            "x": 200, "y": 160, "width": 1520, "height": 720,
            "text": "",
            "backgroundColor": "#00000000",
            "foregroundColor": "#c8e2f5",
            "fontSize": 27,
            "fontFamily": "Consolas",
        }),
    ]),
    e("Prompt", ["ui"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.UiElement", {
            "x": 200, "y": 950, "width": 700, "height": 40,
            "text": "PRESS ANY KEY TO SKIP",
            "backgroundColor": "#00000000",
            "foregroundColor": "#5f88a8",
            "fontSize": 20,
            "fontFamily": "Consolas",
        }),
    ]),
    curtain(),
]

# ------------------------------------------------------------------ settings

SETTINGS_ROWS = [
    ("vsync", "VSYNC", "toggle", 0, 1, 1),
    ("bloomIntensity", "BLOOM", "range", 0.0, 1.5, 0.25),
    ("lensDirt", "LENS DIRT", "range", 0.0, 1.5, 0.25),
    ("masterVolume", "MASTER VOLUME", "range", 0.0, 1.0, 0.1),
    ("showHints", "TACTICAL HINTS", "toggle", 0, 1, 1),
]

settings = backdrop() + [
    e("Shell", ["flow"], [
        ("Rekall.Transform3D", {}),
        ("Game.Modules.FleetRules.ShellTransition", {
            "enabled": True, "phase": "fadingIn", "elapsed": 0,
            "fadeInSeconds": 1.0, "fadeOutSeconds": 1.0,
            "targetScene": "", "overlayEntityName": "Fade Curtain",
            "musicEntityName": "", "musicGain": 0.0,
        }),
        ("Rekall.SceneTransition", {"requestedScene": "", "reason": "settings"}),
    ]),
    # The runtime loads this slot's document before the first step and writes it
    # back whenever SettingsSystem changes it.
    e("Settings Store", ["flow"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.PersistentState", {
            "slot": "settings",
            "document": {
                "vsync": True,
                "bloomIntensity": 1.0,
                "lensDirt": 0.55,
                "masterVolume": 0.8,
                "showHints": True,
            },
        }),
    ]),
    e("Settings Canvas", ["ui"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.UiCanvas", {"referenceWidth": 1920, "referenceHeight": 1080}),
    ]),
    e("Settings Title", ["ui"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.UiElement", {
            "x": 200, "y": 150, "width": 900, "height": 90,
            "text": "SETTINGS", "backgroundColor": "#00000000",
            "foregroundColor": "#dff0ff", "fontSize": 58,
            "fontFamily": "Consolas", "fontWeight": "bold",
        }),
    ]),
    e("Settings Hint", ["ui"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.UiElement", {
            "x": 204, "y": 246, "width": 1000, "height": 40,
            "text": "Click a row to change it. Changes are saved immediately.",
            "backgroundColor": "#00000000", "foregroundColor": "#6f96b4",
            "fontSize": 22, "fontFamily": "Consolas",
        }),
    ]),
]

for index, (key, label, kind, minimum, maximum, step) in enumerate(SETTINGS_ROWS):
    settings.append(e(f"Setting {key}", ["ui", "setting"], [
        ("Rekall.Transform3D", {}),
        ("Rekall.UiElement", {
            "x": 200, "y": 330 + index * 74, "width": 720, "height": 56,
            "text": "  " + label,
            "backgroundColor": "#0a1520c0",
            "foregroundColor": "#b8e4ff",
            "borderColor": "#3f7fa8",
            "borderWidth": 1.5,
            "fontSize": 24,
            "fontFamily": "Consolas",
            "interactive": True,
        }),
        ("Game.Modules.FleetRules.SettingBinding", {
            "enabled": True, "key": key, "label": label, "kind": kind,
            "minimum": minimum, "maximum": maximum, "step": step,
        }),
    ]))

settings.append(e("Button Back", ["ui", "menu"], [
    ("Rekall.Transform3D", {}),
    ("Rekall.UiElement", {
        "x": 200, "y": 330 + len(SETTINGS_ROWS) * 74 + 40, "width": 320, "height": 56,
        "text": "  BACK",
        "backgroundColor": "#0a1520c0", "foregroundColor": "#b8e4ff",
        "borderColor": "#3f7fa8", "borderWidth": 1.5,
        "fontSize": 24, "fontFamily": "Consolas", "interactive": True,
    }),
    ("Game.Modules.FleetRules.MenuAction", {
        "enabled": True, "action": "loadScene", "targetScene": "MainMenu",
    }),
]))
settings.append(curtain())


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    for scene_name, entities in (("Intro", intro), ("Settings", settings)):
        path = os.path.join(out, f"scene_{scene_name}.json")
        with open(path, "w", encoding="utf-8") as handle:
            json.dump({"projectRoot": ROOT, "sceneName": scene_name,
                       "clearExisting": True, "entities": entities}, handle)
        print(f"{scene_name}: {len(entities)} entities -> {path}")
