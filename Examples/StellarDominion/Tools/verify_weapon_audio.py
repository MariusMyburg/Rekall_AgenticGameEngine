"""Focused proof that an actual FleetRules beam uses the imported heavy WAV.

The probe places one armed ship inside beam range, lets CombatSystem fire, and
then inspects the runtime audio projection.  It fails if the shot still uses a
procedural cache key or if the imported clip never becomes an active voice.

usage: python Examples/StellarDominion/Tools/verify_weapon_audio.py
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mcp_client import Mcp


ROOT = "F:/Dev/Rekall_AGE/Examples/StellarDominion"
PROBE = "WeaponAudioProbe"
FLEET = "Game.Modules.FleetRules."
HEAVY_BEAM_CLIP = "asset_stellar-dominion-heavy-beam_434b3a06"


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
            "audio-owner",
            "Audio Owner",
            [
                component(FLEET + "Faction", side="compact", destroyed=False),
                component(FLEET + "Order", kind="attack", targetId="audio-target", speed=0),
                component(
                    FLEET + "Weapon",
                    enabled=True,
                    range=200,
                    damage=1,
                    cycleSeconds=10,
                    cooldown=0,
                    kind="beam",
                ),
                transform(0, 0, 0),
            ],
        ),
        entity(
            "audio-target",
            "Audio Target",
            [
                component(FLEET + "Faction", side="choir", destroyed=False),
                component(
                    FLEET + "Selectable",
                    enabled=True,
                    hull=100,
                    hullMax=100,
                    shields=100,
                    shieldsMax=100,
                ),
                transform(0, 0, 50),
            ],
        ),
        entity(
            "audio-listener",
            "Audio Listener",
            [component("Rekall.AudioListener", active=True), transform(0, 20, -40)],
        ),
    ]

    mcp = Mcp()
    try:
        mcp.call(
            "initialize",
            {
                "protocolVersion": "2024-11-05",
                "capabilities": {},
                "clientInfo": {"name": "weapon-audio-proof", "version": "1"},
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
            {"projectRoot": ROOT, "sceneName": PROBE, "frames": 2},
        )
        if not result.get("ok"):
            print("WEAPON AUDIO  FAIL")
            for error in result.get("errors") or []:
                print("  - " + error.get("message", ""))
            return 1

        value = result["value"]
        emitters = value.get("audioVoices") or []
        matching = [
            emitter
            for emitter in emitters
            if emitter.get("clipAssetId") == HEAVY_BEAM_CLIP
            and emitter.get("state") == "playing"
        ]
        if not matching:
            clips = sorted({emitter.get("clipAssetId", "") for emitter in emitters})
            names = sorted(state["entityName"] for state in value.get("entityStates") or [])
            observations = [
                f"{item.get('code')}: {item.get('message')}"
                for item in value.get("observations") or []
            ]
            print(
                "WEAPON AUDIO  FAIL  actual FleetRules shot did not play the heavy WAV; "
                f"observed clips: {clips}"
            )
            print(f"  entities: {names}")
            for observation in observations:
                print("  observation: " + observation)
            return 1

        emitter = matching[0]
        if max(emitter.get("leftGain", 0), emitter.get("rightGain", 0)) <= 0:
            print("WEAPON AUDIO  FAIL  imported clip is playing but spatial gain is zero")
            return 1

        print(
            "WEAPON AUDIO  PASS  "
            f"{HEAVY_BEAM_CLIP} playing at "
            f"L={emitter.get('leftGain', 0):.3f} R={emitter.get('rightGain', 0):.3f}"
        )
        return 0
    finally:
        mcp.close()
        stale = os.path.join(ROOT, "Scenes", PROBE + ".age.scene.json")
        if os.path.exists(stale):
            os.remove(stale)


if __name__ == "__main__":
    sys.exit(main())
