"""Drives Mission 1 headlessly and proves the combat chain end to end.

Builds throwaway scenes from the real Mission1 blueprint with the squadron already
under attack orders, runs them, and asserts on the result with the runtime's own
assertion facility - so a regression fails the command rather than needing a human
to read numbers out of a dump.

Everything checked here is input-independent. Issuing an order with the mouse is not,
and is not claimed to be covered: that path runs through the player's input bridge and
has to be exercised in the interactive player.

usage: python verify_mission.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mcp_client import Mcp

ROOT = "F:/Dev/Rekall_AGE/Examples/StellarDominion"
FLEET = "Game.Modules.FleetRules."
WARSHIPS = ["Ardent Dominion", "Vigil of Kell", "Long Watch"]


def component(entity, suffix):
    for c in entity["components"]:
        if c["type"] == FLEET + suffix:
            return c["properties"]
    return None


def call(m, tool, payload):
    r = m.call("tools/call", {"name": tool, "arguments": payload})
    if "error" in r:
        raise RuntimeError(r["error"])
    return json.loads(r["result"]["content"][0]["text"])


def assertion(name, subject, operator, expected=None, component_type=None, prop=None):
    a = {"entityName": name, "subject": subject, "operator": operator}
    if expected is not None:
        a["expected"] = expected
    if component_type:
        a["componentType"] = FLEET + component_type
    if prop:
        a["propertyName"] = prop
    return a


def load(scene_name):
    path = os.path.join(ROOT, "Scenes", f"{scene_name}.age.scene.json")
    scene = json.load(open(path, encoding="utf-8"))
    return scene, {x["name"]: x for x in scene["entities"]}


def engage_now(scene):
    """Drops a probe straight into contact.

    The real mission opens with a briefing that holds the Choir back, which is the
    point of the pacing - but a combat case should be testing combat, not waiting out
    prose. case_briefing covers the pacing itself.
    """
    shell = next(x for x in scene["entities"] if x["name"] == "Shell")
    state = component(shell, "MissionState")
    state["phase"] = "engaged"
    state["briefingLines"] = []
    return scene


def case_briefing(scene, by_name):
    """Nothing hostile moves, and nothing is decided, while the briefing runs."""
    hostiles = [x for x in scene["entities"]
                if (component(x, "Faction") or {}).get("side") == "choir"]
    checks = [
        assertion("Shell", "component.property", "equals", "briefing",
                  "MissionState", "phase"),
        assertion("Shell", "component.property", "equals", "active",
                  "MissionState", "outcome"),
    ]
    # A held platform holds station: no order, and no movement.
    for hostile in hostiles:
        checks.append(assertion(hostile["name"], "component.property", "equals", "hold",
                                "Order", "kind"))
        checks.append({"entityName": hostile["name"], "subject": "delta.position3d.z",
                       "operator": "equals", "expected": 0})
    return "briefing", scene, checks, 600


def case_victory(scene, by_name):
    """Three warships each engage a picket node. Every hostile should die."""
    engage_now(scene)
    hostiles = [x for x in scene["entities"]
                if (component(x, "Faction") or {}).get("side") == "choir"]
    for index, name in enumerate(WARSHIPS):
        order = component(by_name[name], "Order")
        order["kind"] = "attack"
        order["targetId"] = hostiles[index % len(hostiles)]["id"]

    checks = [assertion(h["name"], "component.property", "equals", True,
                        "Faction", "destroyed") for h in hostiles]
    checks += [assertion(h["name"], "visible", "equals", False) for h in hostiles]
    checks.append(assertion("Shell", "component.property", "equals", "victory",
                            "MissionState", "outcome"))
    # The flagship must have taken fire and survived it - a victory where nothing
    # shot back would pass every other check here.
    checks.append(assertion("Ardent Dominion", "component.property", "greater-than", 0,
                            "Selectable", "hull"))
    checks.append(assertion(hostiles[0]["name"], "component.property", "equals", "attack",
                            "Order", "kind"))
    # A drive block must ride its hull. This shipped broken because every earlier check
    # asserted on gameplay components and none on a transform: FleetSystem built its
    # leader table only from Drift entities, and orders replaced Drift in this mission,
    # so no plume followed anything. The Choir is the case to check - it is the only
    # side that moves without the player telling it to.
    for hostile in hostiles:
        for axis in ("x", "z"):
            checks.append({
                "entityName": hostile["name"] + " Drive",
                "subject": f"delta.position3d.{axis}",
                "operator": "not-equals", "expected": 0})
    checks.append({"entityName": hostiles[0]["name"] + " Drive",
                   "subject": "delta.position3d.z", "operator": "less-than", "expected": 0})

    # And the mission must actually hand over: outcome alone is a readout, not a flow.
    checks.append({"entityName": "Shell", "subject": "component.property",
                   "operator": "equals", "componentType": "Rekall.SceneTransition",
                   "propertyName": "requestedScene", "expected": "Debrief"})
    return "victory", scene, checks, 5400


def case_missiles(scene, by_name):
    """Fighters alone against one picket node.

    Missiles are the only weapon whose damage is not applied at the moment of firing - the
    round has to arrive - so nothing else in this file proves that path. The capitals are
    stripped of their batteries so a beam cannot get the credit.
    """
    engage_now(scene)
    hostiles = [x for x in scene["entities"]
                if (component(x, "Faction") or {}).get("side") == "choir"]
    target = hostiles[0]
    # Leave one node; the others would only pull fighters away.
    scene["entities"] = [x for x in scene["entities"]
                         if x not in hostiles[1:]
                         and not any(x["name"].startswith(h["name"] + " ") for h in hostiles[1:])]

    for entity in scene["entities"]:
        if component(entity, "Weapon") and "Fighter" not in entity["name"]:
            component(entity, "Weapon")["enabled"] = False
        if "Fighter" in entity["name"]:
            order = component(entity, "Order")
            order["kind"] = "attack"
            order["targetId"] = target["id"]

    checks = [
        assertion(target["name"], "component.property", "equals", True,
                  "Faction", "destroyed"),
        assertion("Shell", "component.property", "equals", "victory",
                  "MissionState", "outcome"),
        # Shields first, then hull - a missile that skipped the shield pool would still
        # destroy the node, so assert the pool actually drained.
        assertion(target["name"], "component.property", "equals", 0,
                  "Selectable", "shields"),
    ]
    return "missiles", scene, checks, 5400


def case_defeat(scene, by_name):
    """The flagship alone, hopelessly outgunned, with the rest of the squadron gone.

    Story-critical loss is the campaign rule: the ship later missions need cannot be
    allowed to die quietly.
    """
    engage_now(scene)
    keep = {"Ardent Dominion", "Ardent Dominion Drive"}
    scene["entities"] = [x for x in scene["entities"]
                         if (component(x, "Faction") or {}).get("side") != "compact"
                         or x["name"] in keep]
    scene["entities"] = [x for x in scene["entities"]
                         if not x["name"].endswith(" Drive")
                         or x["name"] in keep
                         or not x["name"].startswith(("Vigil", "Long"))]
    flagship = next(x for x in scene["entities"] if x["name"] == "Ardent Dominion")
    stats = component(flagship, "Selectable")
    stats["hull"] = 60
    stats["shields"] = 0
    hostiles = [x for x in scene["entities"]
                if (component(x, "Faction") or {}).get("side") == "choir"]
    for hostile in hostiles:
        transform = next(c["properties"] for c in hostile["components"]
                         if c["type"] == "Rekall.Transform3D")
        transform["z"] = 70                       # already inside weapons range

    checks = [
        assertion("Ardent Dominion", "component.property", "equals", True,
                  "Faction", "destroyed"),
        assertion("Shell", "component.property", "equals", "defeat",
                  "MissionState", "outcome"),
        # The drive block must go dark with its hull, not hang in space still burning.
        assertion("Ardent Dominion Drive", "visible", "equals", False),
    ]
    return "defeat", scene, checks, 1200


def case_camera(scene, by_name):
    """The camera derives its own pose and frames the fleet on the first step.

    Worth pinning headlessly because the authored transform in the blueprint is only a
    seed - if the system stopped running, the scene would still look plausible and
    nothing else here would notice.
    """
    checks = [
        # The framing latch must clear, or it would re-frame every step and fight the player.
        assertion("Camera", "component.property", "equals", False,
                  "TacticalCamera", "frameOnStart"),
        # Framing has to actually move the camera off the pose authored in the blueprint.
        {"entityName": "Camera", "subject": "delta.position3d.z",
         "operator": "not-equals", "expected": 0},
        # And it must sit back far enough to hold the whole engagement.
        assertion("Camera", "component.property", "greater-than", 300,
                  "TacticalCamera", "distance"),
    ]
    return "camera", scene, checks, 120


def case_quiet(scene, by_name):
    """No orders given. Nothing should happen, and nothing should be declared."""
    checks = [
        assertion("Shell", "component.property", "equals", "active",
                  "MissionState", "outcome"),
        assertion("Choir Node Ashen", "component.property", "equals", False,
                  "Faction", "destroyed"),
        assertion("Ardent Dominion", "component.property", "equals", 8400,
                  "Selectable", "hull"),
    ]
    return "quiet", scene, checks, 1800


def case_debrief(scene, by_name):
    """The after-action report reads the result out of the campaign document.

    In the game the mission writes that document and the runtime persists it. Here it
    is authored directly, which is the same thing the debrief sees: it has no access
    to the battle either, only to what was written down.
    """
    store = next(x for x in scene["entities"] if x["name"] == "Campaign Store")
    state = next(c["properties"] for c in store["components"]
                 if c["type"] == "Rekall.PersistentState")
    state["document"] = {"lastMission": "MISSION 1 - STANDING WATCH",
                         "lastOutcome": "defeat", "lastLosses": 7,
                         "lastCriticalLoss": True}

    checks = [
        {"entityName": "Debrief Headline", "subject": "component.property",
         "operator": "equals", "componentType": "Rekall.UiElement",
         "propertyName": "text", "expected": "OPERATION FAILED"},
        {"entityName": "Debrief Text", "subject": "component.property",
         "operator": "contains", "componentType": "Rekall.UiElement",
         "propertyName": "text", "expected": "The flagship is gone."},
        {"entityName": "Debrief Text", "subject": "component.property",
         "operator": "contains", "componentType": "Rekall.UiElement",
         "propertyName": "text", "expected": "MISSION 1 - STANDING WATCH"},
    ]
    return "debrief", scene, checks, 30


def main():
    failures = 0
    m = Mcp()
    try:
        m.call("initialize", {"protocolVersion": "2024-11-05", "capabilities": {},
                              "clientInfo": {"name": "verify", "version": "1"}})

        for build in (case_quiet, case_camera, case_briefing, case_victory,
                      case_missiles, case_defeat, case_debrief):
            scene, by_name = load(
                "Debrief" if build is case_debrief else "Mission1")
            label, scene, checks, frames = build(scene, by_name)
            probe = ("DebriefProbe" if build is case_debrief
                     else "Mission1Probe" + label.capitalize())

            call(m, "rekall.scene.create", {"projectRoot": ROOT, "name": probe,
                                            "capabilities": scene["capabilities"]})
            call(m, "rekall.scene.apply_blueprint",
                 {"projectRoot": ROOT, "sceneName": probe, "clearExisting": True,
                  "entities": scene["entities"]})
            result = call(m, "rekall.runtime.inspect_scene",
                          {"projectRoot": ROOT, "sceneName": probe,
                           "frames": frames, "assertions": checks})

            if result.get("ok"):
                print(f"{label:<8} PASS  {len(checks)} assertions over {frames} frames")
            else:
                failures += 1
                print(f"{label:<8} FAIL")
                for error in (result.get("errors") or [])[:12]:
                    print("   - " + error.get("message", ""))
    finally:
        m.close()
        # Probes are scratch scenes, not content. Leaving them behind would put four
        # half-finished copies of the mission in the project's scene list.
        for stale in os.listdir(os.path.join(ROOT, "Scenes")):
            if stale.startswith("Mission1Probe") or stale.startswith("DebriefProbe"):
                os.remove(os.path.join(ROOT, "Scenes", stale))

    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
