---
date: 2026-11-21
status: accepted
area: breakwater tool
---

# The breakwater tool is a tool of the mod's own, not a vanilla area

## Context

The game already has an area tool that draws, picks and edits polygons, and areas that a save keeps, so reusing them would spare the mod its own tool and its own outline. Two ways of reusing them were live:

- A breakwater as a vanilla area under a prefab of the mod's own: with the mod removed, the save keeps areas pointing at a missing prefab, which breaks the Removal rule of `AGENTS.md`'s "Save safety".
- The mod's tool driving vanilla's `AreaToolSystem`: it works only with a mod `SpacePrefab`, whose editing reaches every Space area, buildings' own included; it refuses an outline over unbought map tiles or overlapping another breakwater; in the editor the outline collides with its own stones and the bulldozer deletes it; and each breakwater still leaves an area entity in the save, orphaned once the mod is gone.

## Decision

The tool is a `ToolBaseSystem` of the mod's own, and a breakwater is the mod's own record, with no area entity. It copies the vanilla area tool's code and feel, its gestures, hints and overlay style, wherever the two do not pull apart, since only the mod's own tool and record keep a save harmless without the mod.

## Consequences

- The tool redoes what the area tool gives for free: the raycast, the gestures, the overlay and the hints. Each copies vanilla's code, read in the decompile, rather than its own design.
- A game update that changes the area tool's look or feel reaches this tool only when the mod copies it again.
- Nothing of a breakwater is a vanilla area, so the game's own area systems never touch one.
