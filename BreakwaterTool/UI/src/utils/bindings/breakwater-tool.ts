import { trigger, useValue } from 'cs2/api';
import { tool } from 'cs2/bindings';
import { lazyBindValue } from './lazy-value-binding';

const GROUP = 'breakwaterTool.tool';

/**
 * C# `BreakwaterToolSystem.Id`, the id the game's active tool binding carries for the tool.
 */
export const BREAKWATER_TOOL_ID = 'BreakwaterTool';

const isPicked$ = lazyBindValue<boolean>(GROUP, 'isPicked', false);

const canRevert$ = lazyBindValue<boolean>(GROUP, 'canRevert', false);

const editorOptionOff$ = lazyBindValue<string>(GROUP, 'editorOptionOff', '');

/**
 * Whether Breakwater Tool is the game's active tool.
 */
export function useIsToolActive(): boolean {
  return useValue(tool.activeTool$).id == BREAKWATER_TOOL_ID;
}

/**
 * Whether a breakwater is picked, which Fit to terrain and Delete act on.
 */
export function useIsPicked(): boolean {
  return useValue(isPicked$());
}

/**
 * Whether the picked breakwater changed since it was picked, which Revert undoes.
 */
export function useCanRevert(): boolean {
  return useValue(canRevert$());
}

/**
 * The locale id of Anarchy's editor option while it is off in the editor, where it leaves the
 * stones unprotected; empty otherwise.
 */
export function useEditorOptionOff(): string {
  return useValue(editorOptionOff$());
}

/**
 * Activates the tool, or leaves it for the default tool when it is active.
 */
export function toggleTool(): void {
  trigger(GROUP, 'toggleTool');
}

/**
 * Lays the picked breakwater again on the terrain as it now is.
 */
export function fitPicked(): void {
  trigger(GROUP, 'fit');
}

/**
 * Puts the picked breakwater back as it stood when picked.
 */
export function revertPicked(): void {
  trigger(GROUP, 'revert');
}

/**
 * Deletes the picked breakwater and its stones.
 */
export function deletePicked(): void {
  trigger(GROUP, 'delete');
}
