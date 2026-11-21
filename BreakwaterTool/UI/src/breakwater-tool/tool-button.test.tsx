import { afterEach, describe, expect, it } from 'bun:test';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { getTriggers, resetBindings, setBinding } from '../testing/game-setup';
import { BREAKWATER_TOOL_ID } from '../utils/bindings';
import { ToolButton } from './tool-button';

afterEach(() => {
  cleanup();
  resetBindings();
});

/**
 * Sets the game's active tool, which the button reads with no default.
 */
function setActiveTool(id: string): void {
  setBinding('tool', 'activeTool', { id });
}

function isSelected(): boolean {
  return screen.getByRole('button').classList.contains('selected');
}

describe(`the tool's mods menu button`, () => {
  it(`opens and leaves the tool on a click`, () => {
    setActiveTool('Default Tool');

    render(<ToolButton />);

    fireEvent.click(screen.getByRole('button'));

    // The commands the button sent the tool, apart from the sounds the vanilla button plays.
    const toolTriggers = getTriggers().filter(({ event }) => event.startsWith('breakwaterTool.'));

    expect(toolTriggers).toEqual([{ event: 'breakwaterTool.tool.toggleTool', args: [] }]);
  });

  it(`shows selected while the tool is active`, () => {
    setActiveTool('Default Tool');

    render(<ToolButton />);

    expect(isSelected()).toBe(false);

    act(() => setActiveTool(BREAKWATER_TOOL_ID));

    expect(isSelected()).toBe(true);

    act(() => setActiveTool('Default Tool'));

    expect(isSelected()).toBe(false);
  });
});
