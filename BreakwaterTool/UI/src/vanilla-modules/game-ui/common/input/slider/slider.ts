import type { FC } from 'react';
import { getModuleExport, isRenderable } from '../../../../../utils';

export interface SliderProps {
  readonly value: number;

  readonly start: number;

  readonly end: number;

  readonly gamepadStep?: number;

  readonly disabled?: boolean;

  readonly focusKey?: unknown;

  readonly className?: string;

  /**
   * The value `onChange` receives for a place along the slider, a fraction from 0 to 1 of the way
   * from `start` to `end`: where a value is rounded or snapped.
   */
  readonly valueTransformer?: (start: number, end: number, fraction: number) => number;

  readonly onChange?: (value: number) => void;

  readonly onDragStart?: () => void;

  readonly onDragEnd?: () => void;
}

/**
 * The game's slider, as the tool options panel draws the line tool's distance.
 */
export const Slider = getModuleExport<FC<SliderProps>>(
  'game-ui/common/input/slider/slider.tsx',
  'Slider',
  (value): value is FC<SliderProps> => isRenderable(value),
  // No-op fallback: only reached when the vanilla module is gone and already reported.
  () => null
);
