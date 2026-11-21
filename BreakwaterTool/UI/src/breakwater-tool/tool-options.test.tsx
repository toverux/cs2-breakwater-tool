import { afterEach, describe, expect, it } from 'bun:test';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import deleteSrc from '../icons/breakwater-delete.svg';
import fitSrc from '../icons/breakwater-fit.svg';
import revertSrc from '../icons/breakwater-revert.svg';
import {
  getTriggers,
  type RecordedTrigger,
  resetBindings,
  setBinding,
  setTranslations
} from '../testing/game-setup';
import { ToolOptions } from './tool-options';

const GROUP = 'breakwaterTool.tool';

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
}

/**
 * Sets the bindings the panel reads, before it renders.
 */
function setState({ isPicked, canRevert, editorOptionOff = '' }: ToolState): void {
  setBinding(GROUP, 'isPicked', isPicked);
  setBinding(GROUP, 'canRevert', canRevert);
  setBinding(GROUP, 'editorOptionOff', editorOptionOff);
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
});
