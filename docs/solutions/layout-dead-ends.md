---
date: 2026-11-23
area: BreakwaterTool/Layout
symptoms:
  - 'stones out of the brick bond at the seams where rows meet past a crease of the toe'
  - "a few stones turned against each other past a toe run's free end"
  - 'a few stones stacked out of the bond where three or more toe runs meet'
  - 'notches where rows end at an edge off the toe'
tags: [layout, brick-bond, toe, rows, fans]
---

# Layout defects whose obvious fix fails

## Problem

Rows are contours of the toe's distance field: they bend at the toe's creases and wrap round the free end of a toe run. Both places invite a local fix; the fixes below were measured or weighed, and left.

## What didn't work

- **Cutting rows where they turn past 35°**, so each arm of a crease starts in bond. Its theory reads right from `RowSampler.Along`, that the phase carried round the corner breaks the bond, and measures wrong: seam breaks over the sweep went only from 1914 to 1862, and the cuts left holes of 1.45 to 1.52 rooms, past the bare-ground bound.
- **A minimum turning radius for the toe field at free ends.** It reshapes the field at every free end, moves every picture that has one, and opens a new hole case. It waits for a design shown better first.
- **Retuning the sampler's constants**: Stretch 0.2 cut picked-toe failures from 36 to 27 and raised automatic toes to 11.4 % of their stones off the bond.
- **Stretching joints in fans**, which buys the bond with wider joints that show the ground, for little: a stone steps at most 1 + `RowSampler.Stretch` spacings ahead, so the stones a fan forces fall only to 1/(1 + stretch) of their count.
- **`Gap(RowSpacing)` for closing stones**, which reopens the spine `Lay_Strip_LaysNoTwoRowsFarApart` pins.
- **A closing margin past the default irregularity**, which opens ridges to 1.46 rooms.

## Root cause

The free-end jam is an accepted limit: past a toe run's free end the rows wrap the end point, and the first arcs are tighter than a stone is long, so their few stones turn against each other.

Where three or more toe runs meet, their rows collide from every side and a few stones stack out of the bond, with no hole: an accepted limit. Closing the rows at the junction, as `Closing` does on a spine, was weighed and dropped untried.
Where rows end at an edge off the toe they leave notches, which a fill would straddle: left by eye.
A stone turned 45° at the foot of `jetty`'s spine is left by eye too; its cause is unknown.
