import type { FC, ReactNode } from 'react';
import { getModuleExport, isRenderable } from '../../../../utils';

export interface AutoNavigationScopeProps {
  readonly children?: ReactNode;
}

/**
 * A focus node that hosts several children and moves the gamepad's focus between them, as a tool
 * options section wraps its buttons in, since a section's own focus node hosts one child only.
 */
export const AutoNavigationScope = getModuleExport<FC<AutoNavigationScopeProps>>(
  'game-ui/common/focus/auto-navigation-scope.tsx',
  'AutoNavigationScope',
  (value): value is FC<AutoNavigationScopeProps> => isRenderable(value),
  // Fallback: the children without focus navigation, when the vanilla module is gone and already
  // reported.
  ({ children }) => children
);
