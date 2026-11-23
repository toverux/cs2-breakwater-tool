import { afterEach, describe, expect, it } from 'bun:test';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import acrossSrc from '../icons/breakwater-axis-across.svg';
import twoLayersSrc from '../icons/breakwater-layers-two.svg';
import { getTriggers, type RecordedTrigger, resetBindings } from '../testing/game-setup';
import type { ParameterName } from '../utils/bindings';
import { Advanced } from './advanced';
import { lookOf, shownValue } from './look-fixture';

const GROUP = 'breakwaterTool.tool';

const OPEN_SRC = 'Media/Glyphs/ThickStrokeArrowDown.svg';

afterEach(() => {
  cleanup();
  resetBindings();
});

function toolTriggers(): readonly RecordedTrigger[] {
  return getTriggers().filter(({ event }) => event.startsWith(`${GROUP}.`));
}

function button(src: string): HTMLButtonElement {
  const found = document.querySelector(`button img[src="${src}"]`)?.closest('button');

  if (!found) {
    throw new Error(`No button shows ${src}.`);
  }

  return found;
}

function isShown(name: ParameterName): boolean {
  return screen.queryAllByText(`BreakwaterTool.Tool.PARAMETER[${name}]`).length > 0;
}

/**
 * Renders the section and opens it.
 */
function renderOpen(parameters: Parameters<typeof lookOf>[1] = {}): void {
  render(<Advanced parameters={lookOf({}, parameters).parameters} />);

  fireEvent.click(button(OPEN_SRC));
}

describe(`the Advanced section`, () => {
  it(`is closed until asked for`, () => {
    render(<Advanced parameters={lookOf().parameters} />);

    expect(screen.queryByText('BreakwaterTool.Tool.ADVANCED')).not.toBeNull();
    expect(isShown('RowRatio')).toBe(false);
  });

  it(`shows every parameter the essentials leave out, but those of a choice not taken`, () => {
    renderOpen();

    const names: ParameterName[] = [
      'RowRatio',
      'TurnJitter',
      'Mixing',
      'SizeTolerance',
      'LevelTops',
      'FollowSlope',
      'IsLongAxisAcross',
      'Layers',
      'ToeShare',
      'LevelHeight',
      'Tumble',
      'UnderLayerDepth'
    ];

    // The tumble shows with stones dumped, and the under layer's depth with two layers.
    expect(names.filter(name => !isShown(name))).toEqual(['Tumble', 'UnderLayerDepth']);
  });

  it(`shows the tumble of stones dumped, and the depth of a second layer`, () => {
    renderOpen({ Tumble: 0.35, Layers: 2 });

    expect([isShown('Tumble'), isShown('UnderLayerDepth')]).toEqual([true, true]);
  });

  it(`shows a value in its parameter's unit`, () => {
    renderOpen({ TurnJitter: 6, LevelHeight: 2, ToeShare: 0.4 });

    expect([shownValue('TurnJitter'), shownValue('LevelHeight'), shownValue('ToeShare')]).toEqual([
      '6°',
      '2.0 m',
      '40 %'
    ]);
  });

  it(`sets a parameter of a few values from its buttons, the current one lit`, () => {
    renderOpen({ Layers: 2 });

    expect(button(twoLayersSrc).classList.contains('selected')).toBe(true);
    expect(button(acrossSrc).classList.contains('selected')).toBe(false);

    fireEvent.click(button(acrossSrc));

    expect(toolTriggers()).toEqual([
      { event: `${GROUP}.setParameter`, args: ['IsLongAxisAcross', 1] }
    ]);
  });
});
