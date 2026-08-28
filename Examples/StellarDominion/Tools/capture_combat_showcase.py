"""Build and capture a deterministic close-combat showcase from the real mission rules."""

import copy
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mcp_client import Mcp


ROOT = "F:/Dev/Rekall_AGE/Examples/StellarDominion"
SCENE = os.path.join(ROOT, "Scenes", "Mission1.age.scene.json")
OUTPUT = os.path.join(ROOT, "Captures")
PROBE = "CombatShowcaseProbe"
FLEET = "Game.Modules.FleetRules."


def call(mcp, tool, payload):
    response = mcp.call("tools/call", {"name": tool, "arguments": payload})
    if "error" in response:
        raise RuntimeError(response["error"])
    return json.loads(response["result"]["content"][0]["text"])


def component(entity, suffix):
    return next(
        (item["properties"] for item in entity["components"]
         if item["type"] == FLEET + suffix or item["type"] == suffix),
        None,
    )


def transform(entity):
    return component(entity, "Rekall.Transform3D")


def main():
    with open(SCENE, encoding="utf-8") as stream:
        scene = json.load(stream)
    entities = copy.deepcopy(scene["entities"])
    by_name = {entity["name"]: entity for entity in entities}

    # Compress the real fleets into one readable engagement without replacing any rules.
    hostile_layout = {
        "Choir Node Ashen": (-72, 3, 238),
        "Choir Node Salt": (8, -2, 252),
        "Choir Node Hymn": (92, 4, 224),
    }
    compact_targets = ("Ardent Dominion", "Vigil of Kell", "Long Watch")
    for hostile_index, (hostile_name, position) in enumerate(hostile_layout.items()):
        hostile = by_name[hostile_name]
        pose = transform(hostile)
        pose.update(x=position[0], y=position[1], z=position[2], yaw=180)
        drive = transform(by_name[hostile_name + " Drive"])
        drive.update(x=position[0], y=position[1], z=position[2], yaw=180)
        lights = transform(by_name[hostile_name + " Lights"])
        lights.update(x=position[0], y=position[1], z=position[2], yaw=180)
        order = component(hostile, "Order")
        order.update(kind="attack", targetId=by_name[compact_targets[hostile_index]]["id"])

    for index, compact_name in enumerate(compact_targets):
        order = component(by_name[compact_name], "Order")
        target = by_name[list(hostile_layout)[index]]
        order.update(kind="attack", targetId=target["id"])

    shell = component(by_name["Shell"], "MissionState")
    shell.update(phase="active", phaseElapsed=0, engaged=True)
    transition = component(by_name["Shell"], "ShellTransition")
    transition.update(phase="idle", elapsed=0)
    component(by_name["Fade Curtain"], "Rekall.UiElement")["backgroundColor"] = "#00000000"
    flagship = by_name["Ardent Dominion"]
    command = component(by_name["Tactical HUD"], "FleetCommand")
    command.update(selectedEntityId=flagship["id"], selectedName=flagship["name"])
    component(flagship, "Selectable")["selected"] = True
    camera = component(by_name["Camera"], "TacticalCamera")
    camera.update(frameOnStart=False, pivotX=5, pivotY=0, pivotZ=175, distance=380)

    first = {
        "semanticActions": [{
            "name": "fleet.overcharge", "value": 1, "isDown": True, "wasPressed": True,
        }]
    }
    held = {
        "semanticActions": [{
            "name": "fleet.overcharge", "value": 1, "isDown": True, "wasPressed": False,
        }]
    }

    mcp = Mcp()
    try:
        mcp.call("initialize", {
            "protocolVersion": "2024-11-05", "capabilities": {},
            "clientInfo": {"name": "combat-showcase-capture", "version": "1"},
        })
        call(mcp, "rekall.scene.create", {
            "projectRoot": ROOT, "name": PROBE, "capabilities": scene["capabilities"],
        })
        call(mcp, "rekall.scene.apply_blueprint", {
            "projectRoot": ROOT, "sceneName": PROBE,
            "clearExisting": True, "entities": entities,
        })
        result = call(mcp, "rekall.render.capture_runtime_viewport", {
            "projectRoot": ROOT,
            "sceneName": PROBE,
            "frames": 8,
            "outputDirectory": OUTPUT,
            "width": 1920,
            "height": 1080,
            "debugOverlay": False,
            "backendId": "vulkan",
            "qualityPreset": "Epic",
            "includeGpuTimings": True,
            "inputs": [first] + [held] * 7,
        })
        if not result.get("ok"):
            print(json.dumps(result, indent=2))
            return 1
        value = result["value"]
        print(value["screenshotPath"])
        print(
            f"nonblank={value['nonBlank']} renderables={value['renderableCount']} "
            f"draws={value.get('drawCount', 0)} gpu={value.get('selectedDeviceName')}"
        )
        print("camera=" + str(value.get("layoutDiagnostics", {}).get("activeCamera")))
        print("warnings=" + str(value.get("layoutDiagnostics", {}).get("warningCodes")))
        return 0
    finally:
        mcp.close()
        stale = os.path.join(ROOT, "Scenes", PROBE + ".age.scene.json")
        if os.path.exists(stale):
            os.remove(stale)


if __name__ == "__main__":
    sys.exit(main())
