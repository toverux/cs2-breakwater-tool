import type { ReactElement, ReactNode } from 'react';
import { Tooltip } from '../components/tooltip';
import { Section } from '../vanilla-modules/game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options';
import * as styles from './row.module.scss';

export interface DescribedProps {
  /**
   * What the part is for, in a sentence, shown beside the panel while the pointer is on it.
   */
  readonly description: string | null;

  readonly children: ReactElement;
}

/**
 * A part of the tool options panel that says what it is for in a tooltip on the panel's left,
 * clear of the part's controls and of their own tooltips, which open above them.
 */
export function Described({ description, children }: Readonly<DescribedProps>): ReactElement {
  return (
    <Tooltip
      tooltip={description}
      direction='left'
      alignment='center'
      className={styles.rowBalloon}>
      {children}
    </Tooltip>
  );
}

export interface RowProps {
  readonly title: string | null;

  readonly description: string | null;

  readonly children: ReactNode;
}

/**
 * A row of the tool options panel, the game's own, that says what it is for beside the panel.
 */
export function Row({ title, description, children }: Readonly<RowProps>): ReactElement {
  return (
    <Described description={description}>
      <div className={styles.row}>
        <Section title={title}>{children}</Section>
      </div>
    </Described>
  );
}
