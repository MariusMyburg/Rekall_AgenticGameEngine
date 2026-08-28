"""Deterministic gameplay proof for Stellar Dominion's semantic tactical abilities."""

import copy
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mcp_client import Mcp


ROOT = "F:/Dev/Rekall_AGE/Examples/StellarDominion"
SCENE = os.path.join(ROOT, "Scenes", "Mission1.age.scene.json")
FLEET = "Game.Modules.FleetRules."


def call(mcp, tool, payload):
    response = mcp.call("tools/call", {"name": tool, "arguments": payload})
    if "error" in response:
        raise RuntimeError(response["error"])
    return json.loads(response["result"]["content"][0]["text"])


def component(entity, component_type):
    return next(item for item in entity["components"] if item["type"] == component_type)


def build_probe(action, probe_name):
    with open(SCENE, encoding="utf-8") as stream:
        scene = json.load(stream)
    entities = copy.deepcopy(scene["entities"])
    flagship = next(entity for entity in entities if entity["name"] == "Ardent Dominion")
    command = next(entity for entity in entities if entity["name"] == "Tactical HUD")
    component(command, FLEET + "FleetCommand")["properties"].update(
        selectedEntityId=flagship["id"], selectedName=flagship["name"]
    )
    component(flagship, FLEET + "Selectable")["properties"]["selected"] = True
    if action == "fleet.shield-pulse":
        component(flagship, FLEET + "Selectable")["properties"]["shields"] = 1200
        assertions = [
            {
                "entityName": "Tactical HUD",
                "subject": "changed.component.property",
                "operator": "equals",
                "componentType": FLEET + "TacticalAbilities",
                "propertyName": "shieldPulseCooldown",
                "expected": True,
            },
            {
                "entityName": "Ardent Dominion",
                "subject": "changed.component.property",
                "operator": "equals",
                "componentType": FLEET + "TacticalStatus",
                "propertyName": "shieldPulseVisualSeconds",
                "expected": True,
            },
        ]
    else:
        assertions = [
            {
                "entityName": "Tactical HUD",
                "subject": "changed.component.property",
                "operator": "equals",
                "componentType": FLEET + "TacticalAbilities",
                "propertyName": "overchargeCooldown",
                "expected": True,
            },
            {
                "entityName": "Ardent Dominion",
                "subject": "changed.component.property",
                "operator": "equals",
                "componentType": FLEET + "TacticalStatus",
                "propertyName": "overchargeRemaining",
                "expected": True,
            },
        ]
    assertions.insert(
        0,
        {
            "entityName": "Ardent Dominion",
            "subject": "component",
            "operator": "exists",
            "componentType": FLEET + "TacticalStatus",
        },
    )
    return scene["capabilities"], entities, assertions


def main():
    mcp = Mcp()
    failures = 0
    probes = []
    try:
        mcp.call(
            "initialize",
            {
                "protocolVersion": "2024-11-05",
                "capabilities": {},
                "clientInfo": {"name": "tactical-ability-proof", "version": "1"},
            },
        )
        for action, label in (
            ("fleet.shield-pulse", "ShieldPulse"),
            ("fleet.overcharge", "Overcharge"),
        ):
            probe = "TacticalAbilityProbe" + label
            probes.append(probe)
            capabilities, entities, assertions = build_probe(action, probe)
            call(mcp, "rekall.scene.create", {
                "projectRoot": ROOT, "name": probe, "capabilities": capabilities,
            })
            call(mcp, "rekall.scene.apply_blueprint", {
                "projectRoot": ROOT, "sceneName": probe,
                "clearExisting": True, "entities": entities,
            })
            result = call(mcp, "rekall.runtime.inspect_scene", {
                "projectRoot": ROOT,
                "sceneName": probe,
                "frames": 1,
                "inputs": [{
                    "semanticActions": [{
                        "name": action, "value": 1, "isDown": True, "wasPressed": True
                    }]
                }],
                "assertions": assertions,
            })
            if result.get("ok"):
                print(f"{label.upper():<14} PASS  semantic action changed cooldown and vessel state")
            else:
                failures += 1
                print(f"{label.upper():<14} FAIL")
                for error in (result.get("errors") or [])[:12]:
                    print("  - " + error.get("message", ""))
                for check in (result.get("value") or {}).get("assertionResults", []):
                    if not check.get("passed"):
                        print("  - " + json.dumps(check, sort_keys=True))
                value = result.get("value") or {}
                print("  runtime keys: " + ", ".join(sorted(value.keys())))
                print("  systems: " + json.dumps(value.get("systemsRun", [])))
                print("  actions: " + json.dumps(value.get("inputActions", []), sort_keys=True))
                for issue in value.get("issues", []):
                    print("  - issue " + json.dumps(issue, sort_keys=True))
    finally:
        mcp.close()
        for probe in probes:
            stale = os.path.join(ROOT, "Scenes", probe + ".age.scene.json")
            if os.path.exists(stale):
                os.remove(stale)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
