import { Dropdown, DropdownItem, DropdownToggle } from 'cs2/ui';
import { preloadImages } from 'cs2/utils';
import type { ReactElement } from 'react';
import clearVegetationSrc from '../icons/breakwater-clear-vegetation.svg';
import deleteSrc from '../icons/breakwater-delete.svg';
import dumpedSrc from '../icons/breakwater-dumped.svg';
import fitSrc from '../icons/breakwater-fit.svg';
import laidSrc from '../icons/breakwater-laid.svg';
import reshuffleSrc from '../icons/breakwater-reshuffle.svg';
import revertSrc from '../icons/breakwater-revert.svg';
import { useTranslate } from '../utils';
import * as bindings from '../utils/bindings';
import { AutoNavigationScope } from '../vanilla-modules/game-ui/common/focus/auto-navigation-scope';
import { TooltipLayout } from '../vanilla-modules/game-ui/common/tooltip/description-tooltip/description-tooltip';
import {
  editorDropdownTheme,
  gameDropdownTheme,
  toolOptionsClasses
} from '../vanilla-modules/game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options';
import { ToolButton } from '../vanilla-modules/game-ui/game/components/tool-options/tool-button/tool-button';
import { Advanced } from './advanced';
import { ParameterRow } from './parameter-row';
import { Described, Row } from './row';
import { StonesRow } from './stones-row';
import * as styles from './tool-options.module.scss';

// Loaded with the game's UI and held for the session, so the panel's icons show at once.
preloadImages([clearVegetationSrc, deleteSrc, dumpedSrc, fitSrc, laidSrc, reshuffleSrc, revertSrc]);

/**
 * The tumble a breakwater takes as its placement turns to Dumped.
 */
const DUMPED_TUMBLE = 0.35;

/**
 * The tool's sections in the game's tool options panel, in a game and in the editor: what acts on
 * the picked breakwater, then its look, which is also what the next breakwater drawn takes: a
 * style, the stones, the placement, the essentials, the toe, and the rest under Advanced.
 */
export function ToolOptions(): ReactElement {
  const translate = useTranslate();

  const isPicked = bindings.useIsPicked();

  const canRevert = bindings.useCanRevert();

  const isOutdated = bindings.useIsOutdated();

  const editorOption = bindings.useEditorOptionOff();

  const look = bindings.useLook();

  return (
    <>
      {editorOption && (
        <div className={styles.toolOptionsWarning}>
          {translate('BreakwaterTool.Tool.EDITOR_OPTION_OFF')?.replace(
            '{OPTION}',
            translate(editorOption) ?? editorOption
          )}
        </div>
      )}

      {isOutdated && (
        <div className={styles.toolOptionsOutdated}>
          {translate('BreakwaterTool.Tool.OUTDATED')}
        </div>
      )}

      <Row
        title={translate('BreakwaterTool.Tool.BREAKWATER')}
        description={translate('BreakwaterTool.Tool.ROW_DESCRIPTION[Breakwater]')}>
        <AutoNavigationScope>
          <ToolButton
            className={styles.toolOptionsFit}
            focusKey='fit'
            src={fitSrc}
            disabled={!isPicked}
            tooltip={
              <TooltipLayout
                title={translate('BreakwaterTool.Tool.FIT')}
                description={translate('BreakwaterTool.Tool.FIT_DESCRIPTION')}
              />
            }
            onSelect={bindings.fitPicked}
          />

          <ToolButton
            className={styles.toolOptionsReshuffle}
            focusKey='reshuffle'
            src={reshuffleSrc}
            disabled={!isPicked}
            tooltip={
              <TooltipLayout
                title={translate('BreakwaterTool.Tool.RESHUFFLE')}
                description={translate('BreakwaterTool.Tool.RESHUFFLE_DESCRIPTION')}
              />
            }
            onSelect={bindings.reshuffle}
          />

          <ToolButton
            className={styles.toolOptionsClearVegetation}
            focusKey='clear-vegetation'
            src={clearVegetationSrc}
            disabled={!isPicked}
            tooltip={
              <TooltipLayout
                title={translate('BreakwaterTool.Tool.CLEAR_VEGETATION')}
                description={translate('BreakwaterTool.Tool.CLEAR_VEGETATION_DESCRIPTION')}
              />
            }
            onSelect={bindings.clearVegetation}
          />

          <ToolButton
            className={styles.toolOptionsRevert}
            focusKey='revert'
            src={revertSrc}
            disabled={!canRevert}
            tooltip={
              <TooltipLayout
                title={translate('BreakwaterTool.Tool.REVERT')}
                description={translate('BreakwaterTool.Tool.REVERT_DESCRIPTION')}
              />
            }
            onSelect={bindings.revertPicked}
          />

          <ToolButton
            className={styles.toolOptionsDelete}
            focusKey='delete'
            src={deleteSrc}
            disabled={!isPicked}
            tooltip={
              <TooltipLayout
                title={translate('BreakwaterTool.Tool.DELETE')}
                description={translate('BreakwaterTool.Tool.DELETE_DESCRIPTION')}
              />
            }
            onSelect={bindings.deletePicked}
          />
        </AutoNavigationScope>
      </Row>

      {look && <LookSections look={look} />}
    </>
  );
}

interface LookSectionsProps {
  readonly look: bindings.Look;
}

function LookSections({ look }: Readonly<LookSectionsProps>): ReactElement {
  const translate = useTranslate();

  const styleNames = bindings.useStyles();

  // The editor's dropdowns have their own look, and the editor is where stones need a hint.
  const dropdownTheme = bindings.useStonesHint() ? editorDropdownTheme : gameDropdownTheme;

  const { parameters } = look;

  const isDumped = parameters.Tumble > 0;

  return (
    <>
      <Row
        title={translate('BreakwaterTool.Tool.STYLE')}
        description={translate('BreakwaterTool.Tool.ROW_DESCRIPTION[Style]')}>
        <Dropdown
          theme={dropdownTheme}
          content={styleNames.map(name => (
            <DropdownItem
              key={name}
              className={styles.toolOptionsStyleItem}
              focusKey={name}
              value={name}
              selected={name == look.style}
              onChange={bindings.applyStyle}>
              {translate(`BreakwaterTool.Style.NAME[${name}]`, name)}
            </DropdownItem>
          ))}>
          <DropdownToggle
            className={`${dropdownTheme.dropdownToggle} ${toolOptionsClasses.dropdownToggle} ${styles.toolOptionsStyle}`}>
            {look.style
              ? translate(`BreakwaterTool.Style.NAME[${look.style}]`, look.style)
              : translate('BreakwaterTool.Tool.STYLE_CUSTOM')}
          </DropdownToggle>
        </Dropdown>
      </Row>

      <StonesRow stones={look.stones} />

      <Row
        title={translate('BreakwaterTool.Tool.PLACEMENT')}
        description={translate('BreakwaterTool.Tool.ROW_DESCRIPTION[Placement]')}>
        <AutoNavigationScope>
          <ToolButton
            className={styles.toolOptionsLaid}
            focusKey='laid'
            src={laidSrc}
            selected={!isDumped}
            tooltip={
              <TooltipLayout
                title={translate('BreakwaterTool.Tool.PLACEMENT_VALUE[Laid]')}
                description={translate('BreakwaterTool.Tool.PLACEMENT_DESCRIPTION[Laid]')}
              />
            }
            onSelect={() => {
              bindings.setParameter('Tumble', 0);
            }}
          />

          <ToolButton
            className={styles.toolOptionsDumped}
            focusKey='dumped'
            src={dumpedSrc}
            selected={isDumped}
            tooltip={
              <TooltipLayout
                title={translate('BreakwaterTool.Tool.PLACEMENT_VALUE[Dumped]')}
                description={translate('BreakwaterTool.Tool.PLACEMENT_DESCRIPTION[Dumped]')}
              />
            }
            onSelect={() => {
              if (!isDumped) {
                bindings.setParameter('Tumble', DUMPED_TUMBLE);
              }
            }}
          />
        </AutoNavigationScope>
      </Row>

      <ParameterRow
        name='Density'
        value={parameters.Density}
        min={0.5}
        max={4}
        step={0.05}
        unit='percent'
      />

      <ParameterRow
        name='Embed'
        value={parameters.Embed}
        min={0}
        max={0.9}
        step={0.05}
        unit='percent'
      />

      <ParameterRow
        name='Irregularity'
        value={parameters.Irregularity}
        min={0}
        max={0.5}
        step={0.01}
        unit='percent'
      />

      <Described description={translate('BreakwaterTool.Tool.ROW_DESCRIPTION[Toe]')}>
        <div className={styles.toolOptionsLegend}>
          <div className={styles.toolOptionsLegendToe} />
          {translate('BreakwaterTool.Tool.TOE_LEGEND')}
        </div>
      </Described>

      <Advanced parameters={parameters} />
    </>
  );
}
