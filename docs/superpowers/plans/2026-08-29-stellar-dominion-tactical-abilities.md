# Stellar Dominion Tactical Abilities and Combat Effects Plan

**Goal:** Make fleet combat more interactive and spectacular through
agent-authored tactical abilities while exercising only generic AGE input,
runtime mutation, audio, particles, lights, UI, and deterministic inspection.

## Architecture

- Add a semantic `Rekall.InputActionMap` for `fleet.shield-pulse` and
  `fleet.overcharge`; gameplay never guesses raw keys.
- Add agent-owned `TacticalAbilities` singleton state and per-vessel
  `TacticalStatus` state. Cooldowns and active durations use the engine delta
  time.
- A module system applies abilities to the currently selected friendly vessel,
  emits inspectable state, updates an ability HUD, and creates ordinary
  short-lived render/light/particle/audio effect entities.
- Combat reads `TacticalStatus` to alter damage and cycle timing; no
  genre-specific engine behavior is added.

## Work

1. Add the two components/system and register them in Fleet Rules.
2. Author semantic actions, singleton state, per-unit status, and compact HUD in
   `build_mission.py`, then reapply Mission1.
3. Shield Pulse restores a bounded amount of shield and produces a cool radial
   pulse. Overcharge temporarily increases weapon damage/cadence and produces a
   hot emissive/particle signature with a clear cooldown.
4. Add deterministic `inspect_scene` probes that select a known vessel, inject
   each semantic action, and strictly assert changed agent-owned properties.
5. Rebuild, run the mission gate, capture a combat frame, package/audit, update
   progress, commit, and push.

## Acceptance

- Both abilities are available through semantic actions and visible HUD text.
- Invalid/no selection is safe and observable; a valid selected Compact vessel
  changes state on the same deterministic inspection run.
- Cooldowns prevent spam and tick with delta time.
- Existing victory/defeat, beam tracking, weapon audio, and package audit remain
  green.

## Completion evidence

Completed 2026-08-29. Both semantic actions pass strict deterministic
`changed.component.property` assertions on the command singleton and selected
vessel. The full mission matrix, moving beam, spatial weapon audio, Windows
package, and consolidated package audit pass after the final mutation. The
retained combat capture is
`Examples/StellarDominion/Captures/combat-showcase-1920x1080.png`.

The visual review also drove two follow-on corrections before delivery: the
orange three-ring Overcharge mark was replaced by subtler blue-white broken
filaments, and repeated lamp meshes were replaced by generic emissive line
segments to avoid an 11 MB scene-payload regression.
