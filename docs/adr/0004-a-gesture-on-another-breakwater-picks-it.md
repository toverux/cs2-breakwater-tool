---
date: 2026-11-21
status: accepted
area: breakwater tool
---

# A gesture on another breakwater's handle picks it at once

## Context

The tool edits one breakwater at a time, the one picked by a click inside its outline; Fit, Revert and Delete act on it. Vanilla's area tool has no such step: a press on any area's node or edge edits that area. Two ways were live:

- An explicit pick only: the handles of the breakwaters not picked answer nothing until a click inside their outline picks them.
- An implicit pick: every breakwater's handles answer the pointer, and a gesture on one picks its breakwater as it edits it.

## Decision

The tool keeps its pick, and a gesture on another breakwater's handle picks that breakwater at once: a drag of its vertex moves that vertex, a press on its edge inserts a vertex, a right-click on its vertex removes that vertex. The user chose it after trying it in the game, for the vanilla area tool's feel without losing the pick the panel's actions need.

## Consequences

- Where the picked breakwater's handles and another's are both within reach of the pointer, the picked one's win.
- While an outline is drawn, the other breakwaters' handles answer nothing.
- An implicit pick is a pick: Revert undoes what was done to that breakwater since.
- The breakwaters not picked are outlined dim white, brighter under the pointer, so their handles can be found.
