import { afterEach, describe, expect, it } from 'bun:test';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import addSrc from '../icons/breakwater-add-stone.svg';
import {
  getTriggers,
  type RecordedTrigger,
  resetBindings,
  setBinding,
  setTranslations
} from '../testing/game-setup';
import type { Stone } from '../utils/bindings';
import { stoneOf } from './look-fixture';
import { StonesRow } from './stones-row';

const GROUP = 'breakwaterTool.tool';

const CATEGORY = { index: 365, version: 1 };

const OTHER_CATEGORY = { index: 366, version: 1 };

// Four stones: one laid in part, one left out, one Anarchy cannot protect, one laid whole.
const PARTLY = 1;
const LEFT_OUT = 2;
const UNPROTECTABLE = 3;
const WHOLE = 4;

afterEach(() => {
  cleanup();
  resetBindings();

  // A notch or a hover holds a tooltip until the mouse moves.
  fireEvent.mouseMove(document.body);
});

/**
 * Renders the row as in a game, where the asset menu has a category of stones, or as in the
 * editor, which has none.
 */
function renderRow(stones: readonly Stone[], { shown = OTHER_CATEGORY, hint = '' } = {}): void {
  setBinding(GROUP, 'stoneCategory', hint ? { index: 0, version: 0 } : CATEGORY);
  setBinding(GROUP, 'stonesHint', hint);
  setBinding('toolbar', 'selectedAssetCategory', shown);

  render(<StonesRow stones={stones} />);
}

function button(src: string): HTMLButtonElement {
  const found = document.querySelector(`button img[src="${src}"]`)?.closest('button');

  if (!found) {
    throw new Error(`No button shows ${src}.`);
  }

  return found;
}

/**
 * The image beside the row that loads a thumbnail, which tells of the failure the button's own does
 * not report.
 */
function probe(src: string): Element {
  const found = [...document.querySelectorAll(`img[src="${src}"]`)].find(
    image => !image.closest('button')
  );

  if (!found) {
    throw new Error(`No image beside the row loads ${src}.`);
  }

  return found;
}

// What the row sent the tool and the asset menu, apart from the sounds vanilla buttons play.
function sent(): readonly RecordedTrigger[] {
  return getTriggers().filter(({ event }) => !event.startsWith('audio.'));
}

describe(`the Stones row`, () => {
  it(`shows each chosen prefab's thumbnail, as neighbours in one group of buttons`, () => {
    renderRow([stoneOf(1), stoneOf(2)]);

    const [first, second, add] = [
      button('thumbnail://stone/1'),
      button('thumbnail://stone/2'),
      button(addSrc)
    ];

    // The game spaces the buttons of a group as siblings, each next to the one before.
    expect(first.nextElementSibling).toBe(second);
    expect(second.nextElementSibling).toBe(add);
  });

  it(`removes a stone at a click on its tile`, () => {
    renderRow([stoneOf(1), stoneOf(2)]);

    fireEvent.click(button('thumbnail://stone/2'));

    expect(sent()).toEqual([{ event: `${GROUP}.removeStone`, args: [{ index: 2, version: 1 }] }]);
  });

  it(`shows a stone's fallback image once its thumbnail failed to load, the others kept`, () => {
    renderRow([stoneOf(1), stoneOf(2)]);

    act(() => {
      fireEvent.error(probe('thumbnail://stone/1'));
    });

    expect(button('Media/fallback/1.svg')).not.toBeNull();
    expect(button('thumbnail://stone/2')).not.toBeNull();
  });

  it(`shows the fallback image of a stone that has no thumbnail`, () => {
    renderRow([stoneOf(1, { thumbnail: '' })]);

    expect(button('Media/fallback/1.svg')).not.toBeNull();
  });

  it(`says in a stone's tooltip why it is not laid whole`, async () => {
    const stones = [
      stoneOf(PARTLY, { stones: 8, laid: 7 }),
      stoneOf(LEFT_OUT, { laid: 0 }),
      stoneOf(UNPROTECTABLE, { laid: 0, isUnprotectable: true }),
      stoneOf(WHOLE)
    ];

    const statuses = ['7 of 8 laid', 'Left out', 'Unprotectable'];

    const tips: string[][] = [];

    for (const stone of stones) {
      setTranslations({
        'BreakwaterTool.Tool.STONE_PARTLY': '{LAID} of {STONES} laid',
        'BreakwaterTool.Tool.STONE_LEFT_OUT': 'Left out',
        'BreakwaterTool.Tool.STONE_UNPROTECTABLE': 'Unprotectable',
        'BreakwaterTool.Tool.STONE_REMOVE': 'Click to remove'
      });

      renderRow([stone]);

      fireEvent.mouseOver(button(stone.thumbnail));

      // The game's tooltip opens after a delay, each paragraph of its text an element.
      // oxlint-disable-next-line no-await-in-loop -- One tooltip shows at a time.
      await screen.findByText('Click to remove');

      tips.push(statuses.filter(status => screen.queryByText(status) != null));

      cleanup();
      resetBindings();
      fireEvent.mouseMove(document.body);
    }

    expect(tips).toEqual([['7 of 8 laid'], ['Left out'], ['Unprotectable'], []]);
  });

  it(`opens the asset menu's stones from the add tile, telling the tool first`, () => {
    renderRow([stoneOf(1)]);

    fireEvent.click(button(addSrc));

    expect(sent()).toEqual([
      { event: `${GROUP}.expectOffer`, args: [] },
      { event: 'toolbar.selectAssetCategory', args: [CATEGORY] }
    ]);
  });

  it(`lights the add tile and makes it inert while the asset menu shows the stones`, () => {
    renderRow([stoneOf(1)], { shown: CATEGORY });

    fireEvent.click(button(addSrc));

    expect(button(addSrc).classList.contains('selected')).toBe(true);
    expect(sent()).toEqual([]);
  });

  it(`says how stones are added in the editor, which has no add tile`, () => {
    renderRow([stoneOf(1)], { hint: 'NoFindIt' });

    expect(screen.queryByText('BreakwaterTool.Tool.STONES_HINT[NoFindIt]')).not.toBeNull();
    expect(document.querySelector(`img[src="${addSrc}"]`)).toBeNull();
  });
});
