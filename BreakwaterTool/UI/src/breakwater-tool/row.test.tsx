import { afterEach, describe, expect, it } from 'bun:test';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { resetBindings } from '../testing/game-setup';
import { Row } from './row';

const TITLE = 'TITLE';
const DESCRIPTION = 'DESCRIPTION';
const CONTROL = 'CONTROL';

// Longer than the delay a tooltip opens after.
const PAST_THE_DELAY = 700;

function renderRow(description: string | null): void {
  render(
    <Row title={TITLE} description={description}>
      <span>{CONTROL}</span>
    </Row>
  );
}

afterEach(() => {
  cleanup();
  resetBindings();

  // A hover holds a tooltip until the mouse moves.
  fireEvent.mouseMove(document.body);
});

describe(`a row of the panel`, () => {
  it(`says what it is for while the pointer is on it, on its title or its controls`, async () => {
    renderRow(DESCRIPTION);

    expect(screen.queryByText(DESCRIPTION)).toBeNull();

    fireEvent.mouseOver(screen.getByText(TITLE));

    expect(await screen.findByText(DESCRIPTION)).not.toBeNull();

    cleanup();
    fireEvent.mouseMove(document.body);
    renderRow(DESCRIPTION);

    fireEvent.mouseOver(screen.getByText(CONTROL));

    expect(await screen.findByText(DESCRIPTION)).not.toBeNull();
  });

  it(`shows no tooltip where the locale gives it nothing to say`, async () => {
    renderRow(null);

    fireEvent.mouseOver(screen.getByText(TITLE));

    await Bun.sleep(PAST_THE_DELAY);

    expect(document.body.textContent).toBe(`${TITLE}${CONTROL}`);
  });
});
