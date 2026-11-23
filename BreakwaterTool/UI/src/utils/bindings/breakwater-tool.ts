import { trigger, useValue } from 'cs2/api';
import { type Entity, tool, toolbar } from 'cs2/bindings';
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
 * A layout parameter's name, C# `BreakwaterParameters`' property names; each travels as a number, a
 * flag as 0 or 1.
 */
export type ParameterName =
  | 'Density'
  | 'Embed'
  | 'Irregularity'
  | 'Seed'
  | 'RowRatio'
  | 'LevelTops'
  | 'IsLongAxisAcross'
  | 'FollowSlope'
  | 'TurnJitter'
  | 'SizeTolerance'
  | 'Mixing'
  | 'Layers'
  | 'UnderLayerDepth'
  | 'Tumble'
  | 'ToeShare'
  | 'LevelHeight';

/**
 * A chosen prefab as the Stones row shows it, C# `StoneVerdict`.
 */
export interface Stone {
  readonly prefab: Entity;

  /**
   * The prefab's name, which its `Assets.NAME[…]` locale key carries.
   */
  readonly name: string;

  readonly thumbnail: string;

  /**
   * The image to show where the thumbnail fails to load: the game draws none of the small props.
   */
  readonly fallback: string;

  /**
   * The stones the prefab lists, itself when it is one, and how many of them are laid.
   */
  readonly stones: number;

  readonly laid: number;

  readonly isUnprotectable: boolean;
}

/**
 * The look the panel shows, C# `LookView`: the picked breakwater's, or what the next one takes.
 */
export interface Look {
  readonly parameters: Readonly<Record<ParameterName, number>>;

  readonly stones: readonly Stone[];

  /**
   * The built-in style the look is, or empty.
   */
  readonly style: string;
}

/**
 * How stones are added in the editor, which has no asset menu: through Find It, or not at all
 * without it; empty in a game.
 */
export type StonesHint = '' | 'FindIt' | 'NoFindIt';

const NO_ENTITY: Entity = { index: 0, version: 0 };

const look$ = lazyBindValue<Look | null>(GROUP, 'look', null);

const styles$ = lazyBindValue<readonly string[]>(GROUP, 'styles', []);

const isOutdated$ = lazyBindValue<boolean>(GROUP, 'isOutdated', false);

const stoneCategory$ = lazyBindValue<Entity>(GROUP, 'stoneCategory', NO_ENTITY);

const stonesHint$ = lazyBindValue<StonesHint>(GROUP, 'stonesHint', '');

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
 * The look, or null until the tool wrote it.
 */
export function useLook(): Look | null {
  return useValue(look$());
}

/**
 * The names of the built-in styles, which their locale keys carry.
 */
export function useStyles(): readonly string[] {
  return useValue(styles$());
}

/**
 * Whether the ground under the picked breakwater changed since its stones were laid.
 */
export function useIsOutdated(): boolean {
  return useValue(isOutdated$());
}

/**
 * The asset menu's category of stones, or undefined where there is none to open.
 */
export function useStoneCategory(): Entity | undefined {
  const category = useValue(stoneCategory$());

  return category.index == 0 ? undefined : category;
}

export function useStonesHint(): StonesHint {
  return useValue(stonesHint$());
}

/**
 * Sets one layout parameter, which lays the picked breakwater again.
 */
export function setParameter(name: ParameterName, value: number): void {
  trigger(GROUP, 'setParameter', name, value);
}

/**
 * Fills the parameters from a built-in style, and the stones while they are still a style's own.
 */
export function applyStyle(name: string): void {
  trigger(GROUP, 'applyStyle', name);
}

export function removeStone(prefab: Entity): void {
  trigger(GROUP, 'removeStone', prefab);
}

/**
 * Draws another arrangement of the same look.
 */
export function reshuffle(): void {
  trigger(GROUP, 'reshuffle');
}

/**
 * Clears the plants inside the picked breakwater's outline.
 */
export function clearVegetation(): void {
  trigger(GROUP, 'clearVegetation');
}

/**
 * Whether the asset menu shows a category.
 */
export function useIsCategoryShown(category: Entity | undefined): boolean {
  const shown = useValue(toolbar.selectedAssetCategory$);

  return category != null && shown.index == category.index && shown.version == category.version;
}

/**
 * Opens the asset menu on a category with the tool kept: the tool is told first, so that it takes
 * the asset the menu then offers, the one last selected there, as shown and not as chosen.
 * Call it only while the menu shows another category: selecting the one it shows offers the tool
 * its selected asset again, as a click on that asset does, which adds it.
 */
export function openStoneCategory(category: Entity): void {
  trigger(GROUP, 'expectOffer');
  toolbar.selectAssetCategory(category);
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
