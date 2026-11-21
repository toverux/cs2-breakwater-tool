---
date: 2026-11-21
status: accepted
area: breakwater tool
---

# The outline's edges are drawn straight, aimed by the pointer's ray, and dragged by the ground

## Context

The tool draws a breakwater's outline over terrain that is rarely level: its nodes sit on the terrain, so an edge between two of them runs above or under the ground between them. The tool is to feel like vanilla's area tool, an edge must be easy to aim at to insert a vertex, and a dragged handle must land where the player expects.

Six alternatives were live, and each lost in the game:

- Edges following the terrain, drawn in sampled pieces: the user chose vanilla's straight edges over them.
- A projected overlay (`StyleFlags.Projected`): it paints every surface inside a box spanning the terrain's height range, so the outline draped over the stones it lays and a node on a rock face showed on each face.
- A projected shadow of the edges on the ground, under the unprojected ones: tried, and removed by the user.
- Vanilla's aiming, an edge answering the ground under the pointer: on a slope the player has to guess where a straight edge lies on the ground.
- An inserted vertex keeping the gap between the edge and the ground under the pointer, as a grabbed vertex does: on a slope seen aslant the gap is metres, and the handle trails the pointer for the whole drag.
- Dragging an inserted vertex in a horizontal plane through the point clicked: it needs a floating handle with a line down to the ground, which vanilla lacks, and the plane passes under hills.

## Decision

The outline is drawn as vanilla's area tool draws a surface area: the nodes on the terrain and the edges straight from node to node, unprojected. An edge answers the pointer's ray from the camera, so it is aimed where it shows; a node answers the ground under the pointer, where it is drawn. A vertex inserted on an edge stays on that edge, drawn there, until the drag starts, then follows the ground under the pointer with no offset, as vanilla moves a node. The edge's point and that ground point lie on the pointer's ray, so the handle never leaves the pointer on screen, and it lands where the pointer meets the ground.

## Consequences

- The outline shows through the ground and the stones in front of it, as vanilla's does.
- As an insert's drag starts, the vertex moves on the ground by the gap between the edge and the ground under the pointer, and its two edges swing to it. A click with no drag inserts the vertex on the edge, and once released it is drawn on the ground below that point.
- The gesture interpreter takes the camera's position and the terrain's height (`Pointer.Eye`, `heightAt`).
- A proposal to bring back one of the alternatives above starts from the reason it lost here.
