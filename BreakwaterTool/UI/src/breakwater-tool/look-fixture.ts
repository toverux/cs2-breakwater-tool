import type { Look, ParameterName, Stone } from '../utils/bindings';

/**
 * The default look as the tool writes it, for the tests of the panel: the defaults of C#
 * `BreakwaterParameters`, no stone.
 */
export function lookOf(
  changes: Partial<Look> = {},
  parameters: Partial<Look['parameters']> = {}
): Look {
  return {
    parameters: {
      Density: 1.9047619,
      Embed: 0.5,
      Irregularity: 0.03,
      Seed: 1,
      RowRatio: 0.9333333,
      LevelTops: 0.5,
      IsLongAxisAcross: 0,
      FollowSlope: 1,
      TurnJitter: 6,
      SizeTolerance: 0.15,
      Mixing: 0.3,
      Layers: 1,
      UnderLayerDepth: 0.4,
      Tumble: 0,
      ToeShare: 0.4,
      LevelHeight: 2,
      ...parameters
    },
    stones: [],
    style: 'Coursed',
    ...changes
  };
}

/**
 * A chosen prefab that lays every stone it lists, for the tests of the panel.
 */
export function stoneOf(index: number, changes: Partial<Stone> = {}): Stone {
  return {
    prefab: { index, version: 1 },
    name: `Stone${index}`,
    thumbnail: `thumbnail://stone/${index}`,
    fallback: `Media/fallback/${index}.svg`,
    stones: 1,
    laid: 1,
    isUnprotectable: false,
    ...changes
  };
}

/**
 * The value a parameter's row shows, the row found by its title, which the tests render as its
 * localization id.
 */
export function shownValue(name: ParameterName): string | null | undefined {
  const title = `BreakwaterTool.Tool.PARAMETER[${name}]`;
  const label = [...document.querySelectorAll('[class*="label_"]')].find(
    element => element.textContent == title
  );

  // The game's row holds its label, then its content, the value first.
  return label?.nextElementSibling?.firstElementChild?.textContent;
}
