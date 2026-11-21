import type { ModRegistrar } from 'cs2/modding';
import type { ComponentType, ReactElement } from 'react';
import * as bindings from '../utils/bindings';
import { MOUSE_TOOL_OPTIONS_MODULE } from '../vanilla-modules/game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options';
import { ToolButton } from './tool-button';
import { ToolOptions } from './tool-options';

const TOOL_OPTIONS_PANEL_MODULE = 'game-ui/game/components/tool-options/tool-options-panel.tsx';

export const register: ModRegistrar = moduleRegistry => {
  // Appending here is what makes the game show its mods menu button, see the cs2-modding-ui skill.
  moduleRegistry.append('UniversalModMenu', ToolButton);

  // The game and the editor render the same tool options component, so one extension reaches both.
  moduleRegistry.extend(MOUSE_TOOL_OPTIONS_MODULE, 'MouseToolOptions', withToolOptions);

  // The registry's typing takes a component, and this export is a hook: the registry swaps any
  // export the same way.
  moduleRegistry.extend(
    TOOL_OPTIONS_PANEL_MODULE,
    'useToolOptionsVisible',
    withToolOptionsVisible as unknown as Parameters<typeof moduleRegistry.extend>[2]
  );
};

/**
 * The vanilla tool options with the tool's sections above them while the tool is active.
 */
function withToolOptions(MouseToolOptions: ComponentType): () => ReactElement {
  // oxlint-disable-next-line react/only-export-components - the game loads no fast refresh
  return function MouseToolOptionsWithBreakwaterTool(): ReactElement {
    const isActive = bindings.useIsToolActive();

    return (
      <>
        {isActive && <ToolOptions />}
        <MouseToolOptions />
      </>
    );
  };
}

/**
 * The vanilla visibility of the tool options panel, which also shows while the tool is active.
 */
function withToolOptionsVisible(useToolOptionsVisible: () => boolean): () => boolean {
  return function useToolOptionsVisibleWithBreakwaterTool(): boolean {
    // Both hooks run on every render, whatever the first returns, as React's rules of hooks ask.
    const isVanillaVisible = useToolOptionsVisible();
    const isActive = bindings.useIsToolActive();

    return isVanillaVisible || isActive;
  };
}
