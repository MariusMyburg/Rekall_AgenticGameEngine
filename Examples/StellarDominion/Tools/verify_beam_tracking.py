"""Focused regression proof for live beam endpoint tracking.

The authored owner advances under the ordinary FleetRules drift system while a
short-lived beam already connects it to a stationary target.  After one fixed
runtime step, both the owner and the beam emitter must have moved.  A beam left
at its spawn position fails this test deterministically; no weapon cadence or
capture timing is involved.

usage: python Examples/StellarDominion/Tools/verify_beam_tracking.py
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mcp_client import Mcp


ROOT = "F:/Dev/Rekall_AGE/Examples/StellarDominion"
PROBE = "BeamTrackingProbe"
FLEET = "Game.Modules.FleetRules."


def component(component_type, **properties):
    return {"type": component_type, "properties": properties}


def entity(entity_id, name, components):
    return {
        "id": entity_id,
        "name": name,
        "tags": [],
        "components": components,
        "parentId": None,
        "prefabSourceId": None,
        "visible": True,
        "locked": False,
    }


def transform(x, y, z):
    return component(
        "Rekall.Transform3D",
        x=x,
        y=y,
        z=z,
        pitch=0,
        yaw=0,
        roll=0,
        scaleX=1,
        scaleY=1,
        scaleZ=1,
    )


def call(mcp, tool, payload):
    response = mcp.call("tools/call", {"name": tool, "arguments": payload})
    if "error" in response:
        raise RuntimeError(response["error"])
    return json.loads(response["result"]["content"][0]["text"])


def main():
    entities = [
        entity(
            "beam-owner",
            "Beam Owner",
            [
                component(FLEET + "Drift", enabled=True, speed=60, headingYaw=0),
                transform(0, 0, 0),
            ],
        ),
        entity("beam-target", "Beam Target", [transform(10, 0, 100)]),
        entity(
            "beam-round",
            "Beam",
            [
                component(
                    FLEET + "Ordnance",
                    kind="beam",
                    ownerId="beam-owner",
                    targetId="beam-target",
                    life=0,
                    maxLife=0.3,
                ),
                component(
                    "Rekall.LineSegments",
                    segments=[
                        {
                            "fromX": 0,
                            "fromY": 0,
                            "fromZ": 0,
                            "toX": 10,
                            "toY": 0,
                            "toZ": 100,
                        }
                    ],
                    thickness=1.1,
                    color="#bfe9ffff",
                ),
                transform(0, 0, 0),
            ],
        ),
    ]

    mcp = Mcp()
    try:
        mcp.call(
            "initialize",
            {
                "protocolVersion": "2024-11-05",
                "capabilities": {},
                "clientInfo": {"name": "beam-tracking-proof", "version": "1"},
            },
        )
        call(
            mcp,
            "rekall.scene.create",
            {
                "projectRoot": ROOT,
                "name": PROBE,
                "capabilities": ["modules", "rendering3d", "world"],
            },
        )
        call(
            mcp,
            "rekall.scene.apply_blueprint",
            {
                "projectRoot": ROOT,
                "sceneName": PROBE,
                "clearExisting": True,
                "entities": entities,
            },
        )
        result = call(
            mcp,
            "rekall.runtime.inspect_scene",
            {
                "projectRoot": ROOT,
                "sceneName": PROBE,
                "frames": 1,
                "assertions": [
                    {
                        "entityName": "Beam Owner",
                        "subject": "delta.position3d.z",
                        "operator": "greater-than",
                        "expected": 0,
                    },
                    {
                        "entityName": "Beam",
                        "subject": "delta.position3d.z",
                        "operator": "greater-than",
                        "expected": 0,
                    },
                ],
            },
        )
        if not result.get("ok"):
            print("BEAM TRACKING  FAIL")
            for error in result.get("errors") or []:
                print("  - " + error.get("message", ""))
            return 1

        states = {state["entityName"]: state for state in result["value"]["entityStates"]}
        owner_delta = states["Beam Owner"]["positionDelta3D"]["z"]
        beam_delta = states["Beam"]["positionDelta3D"]["z"]
        if abs(owner_delta - beam_delta) > 0.000001:
            print(
                "BEAM TRACKING  FAIL  "
                f"owner moved {owner_delta:.6f}, beam moved {beam_delta:.6f}"
            )
            return 1

        print(
            "BEAM TRACKING  PASS  "
            f"owner and emitter both advanced {beam_delta:.6f} units in one fixed step"
        )
        return 0
    finally:
        mcp.close()
        stale = os.path.join(ROOT, "Scenes", PROBE + ".age.scene.json")
        if os.path.exists(stale):
            os.remove(stale)


if __name__ == "__main__":
    sys.exit(main())
