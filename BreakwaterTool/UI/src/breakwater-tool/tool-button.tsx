import { Button } from 'cs2/ui';
import { preloadImages } from 'cs2/utils';
import type { ReactElement } from 'react';
import { Tooltip } from '../components/tooltip';
import breakwaterToolSrc from '../icons/breakwater-tool.svg';
import { useTranslate } from '../utils';
import * as bindings from '../utils/bindings';
import { TooltipLayout } from '../vanilla-modules/game-ui/common/tooltip/description-tooltip/description-tooltip';
import * as styles from './tool-button.module.scss';

// Loaded with the game's UI and held for the session, so the button's icon shows at once.
preloadImages([breakwaterToolSrc]);

/**
 * The tool's button in the game's mods menu, selected while the tool is active, which opens and
 * leaves it.
 */
export function ToolButton(): ReactElement {
  const translate = useTranslate();

  const isActive = bindings.useIsToolActive();

  return (
    <Tooltip
      direction='left'
      tooltip={
        <TooltipLayout
          title={translate('BreakwaterTool.Tool.TITLE')}
          description={translate('BreakwaterTool.Tool.DESCRIPTION')}
        />
      }>
      <Button
        variant='floating'
        className={styles.toolButton}
        src={breakwaterToolSrc}
        selected={isActive}
        onSelect={bindings.toggleTool}
      />
    </Tooltip>
  );
}
