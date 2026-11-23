import { afterEach, describe, expect, it } from 'bun:test';
import { cleanup, fireEvent, render } from '@testing-library/react';
import { getTriggers, type RecordedTrigger, resetBindings } from '../testing/game-setup';
import { shownValue } from './look-fixture';
import { ParameterRow } from './parameter-row';

const GROUP = 'breakwaterTool.tool';

// The track's size on screen, so that a pointer's x reads as a share of its width.
const TRACK = 200;
const TRACK_HEIGHT = 10;

// The row's range, as the panel gives the density's.
const MIN = 0.5;
const MAX = 4;
const STEP = 0.05;

// A density on the range, one past it, and the one halfway along the range.
const DENSITY = 1.9;
const PAST_THE_RANGE = 7;
const HALFWAY = 2.25;
const THREE_QUARTERS = 0.75;

// Longer than the row waits between two values a drag sets, in milliseconds.
const PAST_THE_WAIT = 250;

// The row's wait between two values a drag sets, in milliseconds, and a steady drag: a move every
// frame of 60 fps, each to a new value.
const WAIT = 100;
const FRAME = 16;
const STEADY_FROM = 20;
const STEADY_STEP = 4;
const STEADY_MOVES = 40;

// A place along the track that falls between two steps, 1.55 and 1.6, and the nearer of them.
const BETWEEN_STEPS = 0.31;
const NEARER_STEP = 1.6;
const DIGITS = 10;

afterEach(() => {
  cleanup();
  resetBindings();
});

function toolTriggers(): readonly RecordedTrigger[] {
  return getTriggers().filter(({ event }) => event.startsWith(`${GROUP}.`));
}

/**
 * The densities the row set, in order.
 */
function sets(): readonly unknown[] {
  return toolTriggers().map(({ event, args }) =>
    event == `${GROUP}.setParameter` && args[0] == 'Density' ? args[1] : event
  );
}

/**
 * The game's slider of the row, its track given a place on screen, which the test DOM lays out
 * nowhere.
 */
function slider(): Element {
  const track = document.querySelector('[class*="track-bounds"]');
  const root = track?.closest('[class*="slider_"]');

  if (!(track instanceof HTMLElement) || !root) {
    throw new Error(`The row holds no slider.`);
  }

  track.getBoundingClientRect = () => new DOMRect(0, 0, TRACK, TRACK_HEIGHT);

  return root;
}

function renderDensity(value: number): void {
  render(
    <ParameterRow name='Density' value={value} min={MIN} max={MAX} step={STEP} unit='percent' />
  );
}

describe(`a parameter's row`, () => {
  it(`shows the value in its unit, one past the slider's range too`, () => {
    renderDensity(DENSITY);

    expect(shownValue('Density')).toBe('190 %');

    cleanup();
    renderDensity(PAST_THE_RANGE);

    expect(shownValue('Density')).toBe('700 %');
  });

  it(`shows the value under the pointer during a drag and sets it as the drag goes`, async () => {
    renderDensity(1);

    // Halfway along the track: halfway from 0.5 to 4. The first value is set at once.
    fireEvent.mouseDown(slider(), { clientX: TRACK / 2 });

    expect(shownValue('Density')).toBe('225 %');
    expect(sets()).toEqual([HALFWAY]);

    // The values that follow wait, and only the one under the pointer as the wait ends is set.
    fireEvent.mouseMove(document, { clientX: TRACK * THREE_QUARTERS });
    fireEvent.mouseMove(document, { clientX: TRACK });

    expect(shownValue('Density')).toBe('400 %');
    expect(sets()).toEqual([HALFWAY]);

    await Bun.sleep(PAST_THE_WAIT);

    expect(sets()).toEqual([HALFWAY, MAX]);

    // The release sets nothing the drag already set.
    fireEvent.mouseUp(document, { clientX: TRACK });

    expect(sets()).toEqual([HALFWAY, MAX]);

    // The look's value again, until the tool writes the new one.
    expect(shownValue('Density')).toBe('100 %');
  });

  it(`sets the value under the pointer as the drag ends, without waiting`, () => {
    renderDensity(1);

    fireEvent.mouseDown(slider(), { clientX: TRACK / 2 });
    fireEvent.mouseMove(document, { clientX: TRACK });
    fireEvent.mouseUp(document, { clientX: TRACK });

    expect(sets()).toEqual([HALFWAY, MAX]);
  });

  it(`sets one value per wait through a steady drag`, async () => {
    renderDensity(1);

    const start = performance.now();

    fireEvent.mouseDown(slider(), { clientX: STEADY_FROM });

    for (let move = 1; move <= STEADY_MOVES; move++) {
      // oxlint-disable-next-line no-await-in-loop - a hand's moves come one after the other
      await Bun.sleep(FRAME);
      fireEvent.mouseMove(document, { clientX: STEADY_FROM + move * STEADY_STEP });
    }

    // The first value at once, then one per wait at most.
    const waits = Math.floor((performance.now() - start) / WAIT);

    expect(sets().length).toBeLessThanOrEqual(waits + 1);
  });

  it(`sets nothing after the row is gone`, async () => {
    renderDensity(1);

    fireEvent.mouseDown(slider(), { clientX: TRACK / 2 });
    fireEvent.mouseMove(document, { clientX: TRACK });
    cleanup();

    await Bun.sleep(PAST_THE_WAIT);

    expect(sets()).toEqual([HALFWAY]);
  });

  it(`snaps the value under the pointer to the row's step`, () => {
    renderDensity(1);

    // 0.5 + 0.31 * 3.5 = 1.585.
    fireEvent.mouseDown(slider(), { clientX: BETWEEN_STEPS * TRACK });
    fireEvent.mouseUp(document, { clientX: BETWEEN_STEPS * TRACK });

    const [trigger] = toolTriggers();

    expect(trigger?.args[0]).toBe('Density');
    expect(trigger?.args[1]).toBeCloseTo(NEARER_STEP, DIGITS);
  });

  it(`sets nothing on a press that leaves the value where it is`, () => {
    renderDensity(HALFWAY);

    fireEvent.mouseDown(slider(), { clientX: TRACK / 2 });
    fireEvent.mouseUp(document, { clientX: TRACK / 2 });

    expect(toolTriggers()).toEqual([]);
  });
});
