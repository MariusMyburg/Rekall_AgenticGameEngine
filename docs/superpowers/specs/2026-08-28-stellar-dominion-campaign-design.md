# Stellar Dominion — campaign and systems design

Date: 2026-08-28
Status: **Draft for review.** No implementation has started against this.

Shape agreed with the user: real-time tactics, pausable; campaign brief written
before content; first delivery is a thin vertical slice (menu → settings →
one playable mission → win/lose → menu).

---

# Part 1 — Campaign brief

## Setting

The **Meridian Reach** is a gas-giant system on the far edge of a collapsed trade
network. When the jump relays failed forty years ago, the Reach was left with
three things: a ringed gas giant rich in fuel, a single habitable moon, and more
warships than anyone could crew.

The war everyone is still fighting ended decades ago. Nobody has told the fleets.

## Factions

**The Ardent Compact** — the player. Descendants of the system defence fleet,
holding the moon of Kell and the fuel skimmers in Meridian's upper atmosphere.
Disciplined, under-supplied, and increasingly aware that the war they inherited
has no enemy left worth the name. Ships are old, well maintained, and irreplaceable.

**The Hollow Choir** — automated defence platforms and drone wings that never
received a stand-down order. They do not negotiate because there is nobody left
inside them to negotiate. They are not evil; they are a stuck process. Their
tactics are exact, patient, and completely without improvisation — which is the
only reason they can be beaten.

**The Skimmer Combine** — civilian fuel haulers turned reluctant militia. Neutral,
opportunistic, and the campaign's moral pressure: they are why the player cannot
simply burn the Reach down to win.

## The arc

The player commands the Compact's last capital squadron. The campaign is about
the cost of continuing to fight something that cannot surrender.

1. **Standing Watch** — *tutorial.* Escort a Combine fuel convoy through the ring
   plane. Teaches select, move, engage. A single Choir picket attacks; it is
   trivially winnable. The convoy captain thanks you and asks when this ends.
2. **The Long Silence** — Investigate a dead Choir platform. Nothing attacks.
   Teaches formation movement and the pause-and-plan loop in a scene with no
   pressure. Ends with the discovery that the Choir is *reproducing*.
3. **Ardent Dominion** — Defend the moon of Kell against the first real wing.
   Introduces shields, hull damage and losing ships permanently.
4. **Fuel and Ash** — Choose: hold the skimmers, or hold the shipyard. The
   mission is winnable either way and the campaign remembers which.
5. **The Choir Answers** — The Choir adapts for the first time, mirroring a
   tactic the player has used. Introduces enemy reinforcement waves.
6. **Deep Relay** — Reach the derelict relay at the system's edge. Teaches
   attrition: no reinforcements, damage carries between engagements.
7. **Standing Down** — The relay can broadcast the stand-down order the Choir
   never received. Broadcasting it ends the war and destroys the Compact's
   reason to exist as a fleet. The player chooses. Both endings are written.

Tone: quiet, procedural, more *Battlestar* than space opera. The enemy is a
process, not a villain. Ships are scarce and named; losing one should hurt.

---

# Part 2 — Systems design

## Game state

A single `Game.Shell.State` singleton component drives everything:

```
Screen      = mainMenu | settings | briefing | mission | debrief
MissionId   = "m01-standing-watch"
Paused      = bool
Outcome     = none | victory | defeat
```

Screens are scenes. The shell system reacts to state changes and requests the
matching scene.

## Two engine capabilities this needs

Both are genuine gaps. Neither exists today, and both are being **added to the
engine** rather than worked around in the game — a level-based game cannot
change its own scene, and a game with settings cannot persist them, so these are
engine deficiencies a real authored game has exposed rather than quirks of this
one. That is the direction the user chose explicitly.

### 1. Scene transitions requested from inside the game

Today a scene can only be changed from *outside* the running player, over the
live-edit pipe. A game cannot move from its own menu to its own mission.

**Proposed contract:** a `Rekall.SceneTransition` component that an authored
module writes to request a scene, and which the runtime clears once honoured:

```
Rekall.SceneTransition { requestedScene: string, reason: string }
```

The player observes it after each simulation step and performs the same
`ApplySceneDocument` path `reload_scene` already uses. Generic, inspectable, and
not specific to menus — any game needing level flow uses the same primitive.

### 2. Persistent settings and save data

No persistence primitive exists. Settings must survive a restart, and a campaign
must remember mission outcomes and which ships survived.

**Proposed contract:** a bounded, project-scoped key/value document store,
reusing the existing `RekallAgeBoundedFileSnapshot` machinery:

```
rekall.state.write { projectRoot, slot, document }
rekall.state.read  { projectRoot, slot }
```

with a `Rekall.PersistentState` component exposing the loaded slot to modules.
Bounded size, no arbitrary paths, same trust posture as the rest of the engine.

## Tactical layer

Built on the selection that already works.

- **Orders.** `Game.Fleet.Order { kind: move|attack|hold, targetId, x, y, z }`.
  Right-click issues; the order system steers the ship. Existing `Drift` becomes
  the "no order" behaviour.
- **Weapons.** `Game.Fleet.Weapon { range, damage, cycleSeconds, arc }` on
  turret sub-entities, so the turrets already modelled on the hulls are the
  things that fire.
- **Damage.** Extends `Selectable`'s hull/shields. Shields regenerate; hull does
  not. A destroyed ship leaves a wreck entity.
- **AI.** The Choir is deliberately simple and readable: acquire nearest target
  in range, hold formation, never retreat. Its predictability is the story.
- **Pause.** `Paused` gates the fleet systems only, so the camera and UI stay
  live while the world is frozen.

## UI screens

Authored as `Rekall.UiCanvas` + `Rekall.UiElement`, using the engine's existing
`Interactive` flag and `pointer.click` events — buttons work natively today, so
no engine change is needed for the shell itself.

- **Main menu** — title over a slow camera orbit of the fleet, New Campaign /
  Continue / Settings / Quit.
- **Settings** — resolution scale, vsync, bloom and lens-dirt intensity, master
  volume, invert-drag. Written through the persistence contract above.
- **Briefing** — mission text over the tactical map, Deploy button.
- **Mission HUD** — the existing unit panel plus objectives, ship roster, and a
  pause indicator.
- **Debrief** — outcome, ships lost, next mission.

## Verification

Every slice must be shown working in the interactive player, not just the
capture path. The `--screenshot` flag added earlier is the mechanism; scripted
`inspect_scene` input frames remain the way to assert behaviour deterministically.

---

# Part 3 — First delivery

The thin vertical slice, in order:

1. `Rekall.SceneTransition` contract + player support, with tests.
2. Persistence contract + settings screen reading and writing it.
3. Main menu scene, wired to New Campaign and Settings.
4. Mission 1 "Standing Watch": convoy escort, move and attack orders, one Choir
   picket, victory and defeat conditions.
5. Debrief returning to the menu.

That proves scene flow, persistence, orders, combat, and win/lose end to end.
Missions 2–7 are then content against a proven frame.

## Open questions for review

- Is the tone right? The "enemy is a stuck process" premise drives every mission
  and is the hardest thing to change later.
- Ship permanence: should losses be permanent across the campaign? It is the
  strongest source of tension and the strongest source of frustration.
