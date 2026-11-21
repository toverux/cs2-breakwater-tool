import { preloadImages } from 'cs2/utils';
import type { ReactElement } from 'react';
import deleteSrc from '../icons/breakwater-delete.svg';
import fitSrc from '../icons/breakwater-fit.svg';
import revertSrc from '../icons/breakwater-revert.svg';
import { useTranslate } from '../utils';
import * as bindings from '../utils/bindings';
import { AutoNavigationScope } from '../vanilla-modules/game-ui/common/focus/auto-navigation-scope';
import { TooltipLayout } from '../vanilla-modules/game-ui/common/tooltip/description-tooltip/description-tooltip';
import { Section } from '../vanilla-modules/game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options';
import { ToolButton } from '../vanilla-modules/game-ui/game/components/tool-options/tool-button/tool-button';
import * as styles from './tool-options.module.scss';

// Loaded with the game's UI and held for the session, so the panel's icons show at once.
preloadImages([deleteSrc, fitSrc, revertSrc]);

/**
 * The tool's sections in the game's tool options panel, in a game and in the editor: what acts on
 * the picked breakwater, what the overlay's green edges are, and Anarchy's editor option when it
 * leaves the stones unprotected.
 */
export function ToolOptions(): ReactElement {
  const translate = useTranslate();

  const isPicked = bindings.useIsPicked();

  const canRevert = bindings.useCanRevert();

  const editorOption = bindings.useEditorOptionOff();

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

      <Section title={translate('BreakwaterTool.Tool.BREAKWATER')}>
        <AutoNavigationScope>
          <ToolButton
            className={styles.toolOptionsFit}
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
            className={styles.toolOptionsRevert}
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
      </Section>

      <div className={styles.toolOptionsLegend}>
        <div className={styles.toolOptionsLegendToe} />
        {translate('BreakwaterTool.Tool.TOE_LEGEND')}
      </div>
    </>
  );
}
