---
date: 2026-11-21
status: accepted
area: breakwater tool
---

# A breakwater is real at once, and changes only by an edit

## Context

The prototype stamped its stones through a preview that was lost when another tool took over, and nothing remembered a breakwater once applied. The game's own tools preview through a `Temp` pipeline and apply on a click. Three choices were live:

- A preview state with an Apply step, as vanilla tools have: a breakwater would exist only once applied, and its preview would be lost to another tool as the prototype's was.
- A live preview of the stones during a drag: a re-lay of 20 000 protected stones costs about 0.6 s of frames beyond its layout, measured in ticket 01, so a preview on each pointer move stalls the game. The user dropped it.
- A breakwater laid again on its own, at a load, a pick or a terrain change, to follow the latest layout code or the ground: a mod update would then reshuffle every city's breakwaters, and a terrain edit would move stones the player had accepted.

## Decision

Closing the outline creates the breakwater, and each finished gesture, parameter change or stone change lays it again for real, with no preview state and no `Temp` pipeline. Nothing else lays a breakwater: not a load, not a pick, not a terrain change; Fit to terrain is the player's act. The record holds the layout's parameters, never a style's name, since a style only fills them once.

## Consequences

- A city keeps its breakwaters exactly as laid across mod updates; a change to the layout code or to a style's values reaches a breakwater only when its player edits it.
- Every edit is real, so Revert is the way back: it restores the stones a breakwater had when picked.
- A drag shows the outline only; the stones follow on release.
- Stones stay where they were laid when the ground moves under them, until the player fits the breakwater again.
