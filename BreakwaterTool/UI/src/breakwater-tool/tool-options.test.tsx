import { afterEach, describe, expect, it } from 'bun:test';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import clearVegetationSrc from '../icons/breakwater-clear-vegetation.svg';
import deleteSrc from '../icons/breakwater-delete.svg';
import dumpedSrc from '../icons/breakwater-dumped.svg';
import fitSrc from '../icons/breakwater-fit.svg';
import laidSrc from '../icons/breakwater-laid.svg';
import reshuffleSrc from '../icons/breakwater-reshuffle.svg';
import revertSrc from '../icons/breakwater-revert.svg';
import {
  getTriggers,
  type RecordedTrigger,
  resetBindings,
  setBinding,
  setTranslations
} from '../testing/game-setup';
import type { Look } from '../utils/bindings';
import { lookOf, shownValue, stoneOf } from './look-fixture';
import { ToolOptions } from './tool-options';

const GROUP = 'breakwaterTool.tool';

// The tumble a breakwater takes as its placement turns to Dumped, and one set under Advanced.
const DUMPED_TUMBLE = 0.35;
const OWN_TUMBLE = 0.6;

// What Anarchy's settings name its editor option, as the C# side binds it.
const OPTION_ID = 'Options.OPTION[Anarchy.AnarchyModSettings.AnarchyComponentsInEditor]';

afterEach(() => {
  cleanup();
  resetBindings();
});

interface ToolState {
  readonly isPicked: boolean;

  readonly canRevert: boolean;

  readonly editorOptionOff?: string;

  readonly isOutdated?: boolean;

  /**
   * The look the tool wrote; null as before a city gave the tool its prefabs.
   */
  readonly look?: Look | null;
}

/**
 * Sets the bindings the panel reads, before it renders.
 */
function setState({
  isPicked,
  canRevert,
  editorOptionOff = '',
  isOutdated = false,
  look = lookOf()
}: ToolState): void {
  setBinding(GROUP, 'isPicked', isPicked);
  setBinding(GROUP, 'canRevert', canRevert);
  setBinding(GROUP, 'editorOptionOff', editorOptionOff);
  setBinding(GROUP, 'isOutdated', isOutdated);
  setBinding(GROUP, 'look', look);
  setBinding(GROUP, 'styles', ['Coursed', 'Rubble', 'Riprap']);
  setBinding(GROUP, 'stoneCategory', { index: 365, version: 1 });
  setBinding(GROUP, 'stonesHint', '');
  setBinding('toolbar', 'selectedAssetCategory', { index: 0, version: 0 });
}

/**
 * A button of the section, found by its icon: the three share their markup, and their labels show
 * in tooltips only.
 */
function button(icon: string): HTMLButtonElement {
  const found = document.querySelector(`img[src="${icon}"]`)?.closest('button');

  if (!found) {
    throw new Error(`No button shows ${icon}.`);
  }

  return found;
}

function isEnabled(icon: string): boolean {
  return !button(icon).hasAttribute('disabled');
}

function isLit(icon: string): boolean {
  return button(icon).classList.contains('selected');
}

// The commands the panel sent the tool, apart from the sounds vanilla buttons play.
function toolTriggers(): readonly RecordedTrigger[] {
  return getTriggers().filter(({ event }) => event.startsWith(`${GROUP}.`));
}

function clickAll(): void {
  for (const icon of [fitSrc, revertSrc, deleteSrc]) {
    fireEvent.click(button(icon));
  }
}

describe(`the tool's options`, () => {
  it(`fit, revert and delete the picked breakwater from their buttons`, () => {
    setState({ isPicked: true, canRevert: true });

    render(<ToolOptions />);

    clickAll();

    expect(toolTriggers()).toEqual([
      { event: `${GROUP}.fit`, args: [] },
      { event: `${GROUP}.revert`, args: [] },
      { event: `${GROUP}.delete`, args: [] }
    ]);
  });

  it(`disable every button while nothing is picked`, () => {
    setState({ isPicked: false, canRevert: false });

    render(<ToolOptions />);

    clickAll();

    expect([isEnabled(fitSrc), isEnabled(revertSrc), isEnabled(deleteSrc)]).toEqual([
      false,
      false,
      false
    ]);
    expect(toolTriggers()).toEqual([]);
  });

  it(`enable Revert once the picked breakwater changed`, () => {
    setState({ isPicked: true, canRevert: false });

    render(<ToolOptions />);

    expect([isEnabled(fitSrc), isEnabled(revertSrc), isEnabled(deleteSrc)]).toEqual([
      true,
      false,
      true
    ]);

    act(() => setBinding(GROUP, 'canRevert', true));

    expect(isEnabled(revertSrc)).toBe(true);
  });

  it(`warn that Anarchy's editor option is off, naming it`, () => {
    setTranslations({
      'BreakwaterTool.Tool.EDITOR_OPTION_OFF': `Turn on "{OPTION}".`,
      [OPTION_ID]: 'Anarchy Components In Editor'
    });
    setState({ isPicked: false, canRevert: false, editorOptionOff: OPTION_ID });

    render(<ToolOptions />);

    expect(screen.queryByText(`Turn on "Anarchy Components In Editor".`)).not.toBeNull();
  });

  it(`show the section and the toe's legend, with no warning while Anarchy's option is on`, () => {
    setState({ isPicked: false, canRevert: false });

    render(<ToolOptions />);

    expect(screen.queryByText('BreakwaterTool.Tool.BREAKWATER')).not.toBeNull();
    expect(screen.queryByText('BreakwaterTool.Tool.TOE_LEGEND')).not.toBeNull();
    expect(screen.queryByText('BreakwaterTool.Tool.EDITOR_OPTION_OFF')).toBeNull();
  });

  it(`show the Breakwater section alone until the tool wrote its look`, () => {
    setState({ isPicked: false, canRevert: false, look: null });

    render(<ToolOptions />);

    expect(screen.queryByText('BreakwaterTool.Tool.BREAKWATER')).not.toBeNull();
    expect(screen.queryByText('BreakwaterTool.Tool.STYLE')).toBeNull();
    expect(screen.queryByText('BreakwaterTool.Tool.TOE_LEGEND')).toBeNull();
  });

  it(`show the look's sections in order, the rest folded under Advanced`, () => {
    setState({ isPicked: true, canRevert: false, look: lookOf({ stones: [stoneOf(1)] }) });

    render(<ToolOptions />);

    const titles = [
      'BreakwaterTool.Tool.BREAKWATER',
      'BreakwaterTool.Tool.STYLE',
      'BreakwaterTool.Tool.STONES',
      'BreakwaterTool.Tool.PLACEMENT',
      'BreakwaterTool.Tool.PARAMETER[Density]',
      'BreakwaterTool.Tool.PARAMETER[Embed]',
      'BreakwaterTool.Tool.PARAMETER[Irregularity]',
      'BreakwaterTool.Tool.TOE_LEGEND',
      'BreakwaterTool.Tool.ADVANCED'
    ];

    const found: Element[] = titles.map(title => screen.getByText(title));

    // Each title stands where the document's order has it.
    const inOrder = [...document.querySelectorAll('*')].filter(element => found.includes(element));

    expect(inOrder).toEqual(found);

    expect(screen.queryByText('BreakwaterTool.Tool.PARAMETER[RowRatio]')).toBeNull();
  });

  it(`reshuffle and clear the vegetation of the picked breakwater only`, () => {
    setState({ isPicked: false, canRevert: false });

    render(<ToolOptions />);

    expect([isEnabled(reshuffleSrc), isEnabled(clearVegetationSrc)]).toEqual([false, false]);

    act(() => setBinding(GROUP, 'isPicked', true));

    fireEvent.click(button(reshuffleSrc));
    fireEvent.click(button(clearVegetationSrc));

    expect(toolTriggers()).toEqual([
      { event: `${GROUP}.reshuffle`, args: [] },
      { event: `${GROUP}.clearVegetation`, args: [] }
    ]);
  });

  it(`say that the terrain changed under the picked breakwater, Fit left as any button`, () => {
    setState({ isPicked: true, canRevert: false });

    render(<ToolOptions />);

    expect(screen.queryByText('BreakwaterTool.Tool.OUTDATED')).toBeNull();

    act(() => setBinding(GROUP, 'isOutdated', true));

    expect(screen.queryByText('BreakwaterTool.Tool.OUTDATED')).not.toBeNull();

    // Lit, the button would read as a mode that is on.
    expect(isLit(fitSrc)).toBe(false);
  });

  it(`name the built-in style the look has, or say it is none`, () => {
    setState({ isPicked: false, canRevert: false, look: lookOf({ style: 'Rubble' }) });

    render(<ToolOptions />);

    // A style's name shows as its locale key gives it, its own name without one.
    expect(screen.queryByText('Rubble')).not.toBeNull();

    act(() => setBinding(GROUP, 'look', lookOf({ style: '' })));

    expect(screen.queryByText('Rubble')).toBeNull();
    expect(screen.queryByText('BreakwaterTool.Tool.STYLE_CUSTOM')).not.toBeNull();
  });

  it(`apply the style chosen in the dropdown`, async () => {
    setState({ isPicked: false, canRevert: false, look: lookOf({ style: '' }) });

    render(<ToolOptions />);

    fireEvent.click(screen.getByText('BreakwaterTool.Tool.STYLE_CUSTOM'));
    fireEvent.click(await screen.findByText('Riprap'));

    expect(toolTriggers()).toEqual([{ event: `${GROUP}.applyStyle`, args: ['Riprap'] }]);
  });

  it(`choose between stones laid and dumped, which is the tumble`, () => {
    setState({ isPicked: true, canRevert: false });

    render(<ToolOptions />);

    expect([isLit(laidSrc), isLit(dumpedSrc)]).toEqual([true, false]);

    // Laid already: a click on it changes nothing a re-lay would show.
    fireEvent.click(button(dumpedSrc));

    expect(toolTriggers()).toEqual([
      { event: `${GROUP}.setParameter`, args: ['Tumble', DUMPED_TUMBLE] }
    ]);

    act(() => setBinding(GROUP, 'look', lookOf({ style: '' }, { Tumble: OWN_TUMBLE })));

    expect([isLit(laidSrc), isLit(dumpedSrc)]).toEqual([false, true]);

    // Dumped already: its tumble, set under Advanced, is kept.
    fireEvent.click(button(dumpedSrc));
    fireEvent.click(button(laidSrc));

    expect(toolTriggers().slice(1)).toEqual([
      { event: `${GROUP}.setParameter`, args: ['Tumble', 0] }
    ]);
  });

  it(`show the essentials in the player's units`, () => {
    setState({ isPicked: false, canRevert: false });

    render(<ToolOptions />);

    expect([shownValue('Density'), shownValue('Embed'), shownValue('Irregularity')]).toEqual([
      '190 %',
      '50 %',
      '3 %'
    ]);
  });
});
