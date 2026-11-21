import type { FC, ReactNode } from 'react';
import { getModuleExport, isRenderable } from '../../../../../../utils';

export interface ToolButtonProps {
  readonly src: string;

  readonly tooltip?: ReactNode;

  readonly selected?: boolean;

  readonly disabled?: boolean;

  readonly focusKey?: unknown;

  readonly className?: string;

  readonly uiTag?: string;

  readonly onSelect?: () => void;
}

/**
 * The tool options panel's icon button, with its tooltip and sound.
 */
export const ToolButton = getModuleExport<FC<ToolButtonProps>>(
  'game-ui/game/components/tool-options/tool-button/tool-button.tsx',
  'ToolButton',
  (value): value is FC<ToolButtonProps> => isRenderable(value),
  // No-op fallback: only reached when the vanilla module is gone and already reported.
  () => null
);
