import { type ReactElement, useCallback, useState } from 'react';
import addSrc from '../icons/breakwater-add-stone.svg';
import { useTranslate } from '../utils';
import * as bindings from '../utils/bindings';
import { AutoNavigationScope } from '../vanilla-modules/game-ui/common/focus/auto-navigation-scope';
import { TooltipLayout } from '../vanilla-modules/game-ui/common/tooltip/description-tooltip/description-tooltip';
import { ToolButton } from '../vanilla-modules/game-ui/game/components/tool-options/tool-button/tool-button';
import { Row } from './row';
import * as styles from './stones-row.module.scss';

export interface StonesRowProps {
  readonly stones: readonly bindings.Stone[];
}

/**
 * The chosen stones as thumbnails in one group of buttons, each removed by a click, a stone that
 * is not laid dimmed with the reason in its tooltip; then the tile that opens the asset menu's
 * stones, or in the editor, which has no asset menu, how stones are added there.
 */
export function StonesRow({ stones }: Readonly<StonesRowProps>): ReactElement {
  const translate = useTranslate();

  const category = bindings.useStoneCategory();

  const isCategoryShown = bindings.useIsCategoryShown(category);

  const hint = bindings.useStonesHint();

  // The thumbnails that failed to load: the game draws none of the smallest props.
  const [failed, setFailed] = useState<readonly string[]>([]);

  const onError = useCallback((thumbnail: string) => {
    setFailed(known => (known.includes(thumbnail) ? known : [...known, thumbnail]));
  }, []);

  return (
    <>
      <Row
        title={translate('BreakwaterTool.Tool.STONES')}
        description={translate('BreakwaterTool.Tool.ROW_DESCRIPTION[Stones]')}>
        <AutoNavigationScope>
          {stones.map(stone => (
            <StoneTile
              key={`${stone.prefab.index}:${stone.prefab.version}`}
              stone={stone}
              hasFailed={failed.includes(stone.thumbnail)}
            />
          ))}

          {category && (
            <ToolButton
              className={styles.stonesRowAdd}
              focusKey='stone-add'
              src={addSrc}
              selected={isCategoryShown}
              tooltip={
                <TooltipLayout
                  title={translate('BreakwaterTool.Tool.STONE_ADD')}
                  description={translate('BreakwaterTool.Tool.STONE_ADD_DESCRIPTION')}
                />
              }
              onSelect={() => {
                if (!isCategoryShown) {
                  bindings.openStoneCategory(category);
                }
              }}
            />
          )}
        </AutoNavigationScope>
      </Row>

      {/* A button's own image reports nothing: these, never shown, tell of a failed load. They
          stand outside the row, whose buttons the game spaces as neighbours. */}
      <div className={styles.stonesRowProbes}>
        {stones
          .filter(stone => stone.thumbnail)
          .map(stone => (
            <img
              key={stone.thumbnail}
              src={stone.thumbnail}
              onError={() => {
                onError(stone.thumbnail);
              }}
            />
          ))}
      </div>

      {hint && (
        <div className={styles.stonesRowHint}>
          {translate(`BreakwaterTool.Tool.STONES_HINT[${hint}]`)}
        </div>
      )}
    </>
  );
}

interface StoneTileProps {
  readonly stone: bindings.Stone;

  readonly hasFailed: boolean;
}

function StoneTile({ stone, hasFailed }: Readonly<StoneTileProps>): ReactElement {
  const translate = useTranslate();

  const onSelect = useCallback(() => {
    bindings.removeStone(stone.prefab);
  }, [stone.prefab]);

  const status = describe(stone);

  return (
    <ToolButton
      className={stone.laid == 0 ? styles.stonesRowLeftOut : styles.stonesRowLaid}
      focusKey={`stone-${stone.prefab.index}`}
      src={hasFailed || !stone.thumbnail ? stone.fallback : stone.thumbnail}
      tooltip={
        <TooltipLayout
          title={translate(`Assets.NAME[${stone.name}]`, stone.name)}
          description={[
            status &&
              translate(`BreakwaterTool.Tool.${status}`)
                ?.replace('{LAID}', `${stone.laid}`)
                .replace('{STONES}', `${stone.stones}`),
            translate('BreakwaterTool.Tool.STONE_REMOVE')
          ]
            .filter(Boolean)
            .join('\n\n')}
        />
      }
      onSelect={onSelect}
    />
  );
}

/**
 * The locale key of what the tooltip says of a stone that is not laid whole, or undefined.
 */
function describe(stone: bindings.Stone): string | undefined {
  if (stone.isUnprotectable) {
    return 'STONE_UNPROTECTABLE';
  }

  if (stone.laid == 0) {
    return 'STONE_LEFT_OUT';
  }

  return stone.laid < stone.stones ? 'STONE_PARTLY' : undefined;
}
