import { preloadImages } from 'cs2/utils';
import { type ReactElement, useState } from 'react';
import acrossSrc from '../icons/breakwater-axis-across.svg';
import alongSrc from '../icons/breakwater-axis-along.svg';
import oneLayerSrc from '../icons/breakwater-layers-one.svg';
import twoLayersSrc from '../icons/breakwater-layers-two.svg';
import { useTranslate } from '../utils';
import * as bindings from '../utils/bindings';
import { AutoNavigationScope } from '../vanilla-modules/game-ui/common/focus/auto-navigation-scope';
import { TooltipLayout } from '../vanilla-modules/game-ui/common/tooltip/description-tooltip/description-tooltip';
import { ToolButton } from '../vanilla-modules/game-ui/game/components/tool-options/tool-button/tool-button';
import { ParameterRow } from './parameter-row';
import { Row } from './row';
import * as styles from './advanced.module.scss';

preloadImages([acrossSrc, alongSrc, oneLayerSrc, twoLayersSrc]);

const OPEN_SRC = 'Media/Glyphs/ThickStrokeArrowDown.svg';

const CLOSE_SRC = 'Media/Glyphs/ThickStrokeArrowUp.svg';

export interface AdvancedProps {
  readonly parameters: bindings.Look['parameters'];
}

/**
 * Every layout parameter the essentials leave out, in a section that folds, closed until asked for.
 */
export function Advanced({ parameters }: Readonly<AdvancedProps>): ReactElement {
  const translate = useTranslate();

  const [isOpen, setIsOpen] = useState(false);

  return (
    <>
      <Row
        title={translate('BreakwaterTool.Tool.ADVANCED')}
        description={translate('BreakwaterTool.Tool.ADVANCED_DESCRIPTION')}>
        <ToolButton
          className={styles.advancedToggle}
          focusKey='advanced'
          src={isOpen ? CLOSE_SRC : OPEN_SRC}
          selected={isOpen}
          onSelect={() => {
            setIsOpen(!isOpen);
          }}
        />
      </Row>

      {isOpen && (
        <div className={styles.advancedRows}>
          <ParameterRow
            name='RowRatio'
            value={parameters.RowRatio}
            min={0.5}
            max={2}
            step={0.01}
            unit='percent'
          />

          <ParameterRow
            name='TurnJitter'
            value={parameters.TurnJitter}
            min={0}
            max={180}
            step={1}
            unit='degrees'
          />

          <ParameterRow
            name='Mixing'
            value={parameters.Mixing}
            min={0}
            max={1}
            step={0.05}
            unit='percent'
          />

          <ParameterRow
            name='SizeTolerance'
            value={parameters.SizeTolerance}
            min={0}
            max={5}
            step={0.05}
            unit='percent'
          />

          <ParameterRow
            name='LevelTops'
            value={parameters.LevelTops}
            min={0}
            max={1}
            step={0.05}
            unit='percent'
          />

          <ParameterRow
            name='FollowSlope'
            value={parameters.FollowSlope}
            min={0}
            max={1}
            step={0.05}
            unit='percent'
          />

          {parameters.Tumble > 0 && (
            <ParameterRow
              name='Tumble'
              value={parameters.Tumble}
              min={0.05}
              max={1}
              step={0.05}
              unit='percent'
            />
          )}

          <Choice
            name='IsLongAxisAcross'
            value={parameters.IsLongAxisAcross}
            options={[
              { value: 0, label: 'Along', src: alongSrc },
              { value: 1, label: 'Across', src: acrossSrc }
            ]}
          />

          <Choice
            name='Layers'
            value={parameters.Layers}
            options={[
              { value: 1, label: 'One', src: oneLayerSrc },
              { value: 2, label: 'Two', src: twoLayersSrc }
            ]}
          />

          {parameters.Layers > 1 && (
            <ParameterRow
              name='UnderLayerDepth'
              value={parameters.UnderLayerDepth}
              min={0}
              max={1}
              step={0.05}
              unit='percent'
            />
          )}

          <ParameterRow
            name='ToeShare'
            value={parameters.ToeShare}
            min={0}
            max={1}
            step={0.05}
            unit='percent'
          />

          <ParameterRow
            name='LevelHeight'
            value={parameters.LevelHeight}
            min={0}
            max={20}
            step={0.5}
            unit='metres'
          />
        </div>
      )}
    </>
  );
}

interface ChoiceProps {
  readonly name: bindings.ParameterName;

  readonly value: number;

  /**
   * The values to choose from, each with its icon and the suffix of its locale keys.
   */
  readonly options: ReadonlyArray<{
    readonly value: number;
    readonly label: string;
    readonly src: string;
  }>;
}

/**
 * A parameter of a few values, as a row of buttons, the current one lit.
 */
function Choice({ name, value, options }: Readonly<ChoiceProps>): ReactElement {
  const translate = useTranslate();

  return (
    <Row
      title={translate(`BreakwaterTool.Tool.PARAMETER[${name}]`)}
      description={translate(`BreakwaterTool.Tool.PARAMETER_DESCRIPTION[${name}]`)}>
      <AutoNavigationScope>
        {options.map(option => (
          <ToolButton
            key={option.value}
            className={styles.advancedOption}
            focusKey={`${name}-${option.label}`}
            src={option.src}
            selected={option.value == value}
            tooltip={
              <TooltipLayout
                title={translate(`BreakwaterTool.Tool.PARAMETER_VALUE[${name}][${option.label}]`)}
                description={translate(
                  `BreakwaterTool.Tool.PARAMETER_VALUE_DESCRIPTION[${name}][${option.label}]`
                )}
              />
            }
            onSelect={() => {
              bindings.setParameter(name, option.value);
            }}
          />
        ))}
      </AutoNavigationScope>
    </Row>
  );
}
