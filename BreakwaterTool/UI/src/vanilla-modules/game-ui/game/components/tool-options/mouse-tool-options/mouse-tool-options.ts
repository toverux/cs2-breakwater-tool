import type { FC, ReactNode } from 'react';
import { getModuleExport, isRenderable } from '../../../../../../utils';

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
