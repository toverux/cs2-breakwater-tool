import type { FC, ReactNode } from 'react';
import { getClassesModule, getModuleExport, isRenderable } from '../../../../../../utils';

/**
 * The module of the tool options panel's mouse layout, which mods extend to add their sections.
 */
export const MOUSE_TOOL_OPTIONS_MODULE =
  'game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options.tsx';

export interface SectionProps {
  readonly title: ReactNode;

  readonly uiTag?: string;

  readonly children?: ReactNode;
}

/**
 * A row of the tool options panel: its title on the left, its controls on the right.
 */
export const Section = getModuleExport<FC<SectionProps>>(
  MOUSE_TOOL_OPTIONS_MODULE,
  'Section',
  (value): value is FC<SectionProps> => isRenderable(value),
  // No-op fallback: only reached when the vanilla module is gone and already reported.
  () => null
);

/**
 * The classes the vanilla rows give their controls: the slider of a row, and its dropdown's toggle.
 */
export const toolOptionsClasses = getClassesModule(
  'game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options.module.scss',
  ['slider', 'dropdownToggle']
);

/**
 * The dropdown's look in the tool options panel, in a game and in the editor.
 */
export const gameDropdownTheme = getClassesModule('game-ui/game/themes/game-dropdown.module.scss', [
  'dropdownToggle',
  'dropdownMenu',
  'dropdownItem'
]);

export const editorDropdownTheme = getClassesModule(
  'game-ui/editor/themes/editor-dropdown.module.scss',
  ['dropdownToggle', 'dropdownMenu', 'dropdownItem']
);
