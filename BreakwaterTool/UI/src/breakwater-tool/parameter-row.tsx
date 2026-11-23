import { type ReactElement, useCallback, useEffect, useRef, useState } from 'react';
import { useTranslate } from '../utils';
import * as bindings from '../utils/bindings';
import { Slider } from '../vanilla-modules/game-ui/common/input/slider/slider';
import { toolOptionsClasses } from '../vanilla-modules/game-ui/game/components/tool-options/mouse-tool-options/mouse-tool-options';
import { Row } from './row';
import * as styles from './parameter-row.module.scss';

const PERCENT = 100;

/**
 * The least time between two values a drag sets, in milliseconds: each one lays the picked
 * breakwater again, which a large one takes a few frames to do.
 */
const RELAY_DELAY = 100;

export type ParameterUnit = 'percent' | 'degrees' | 'metres';

export interface ParameterRowProps {
  readonly name: bindings.ParameterName;

  /**
   * The parameter as the look holds it.
   */
  readonly value: number;

  readonly min: number;

  readonly max: number;

  /**
   * What a move of the slider snaps to, in the parameter's own unit.
   */
  readonly step: number;

  readonly unit: ParameterUnit;
}

/**
 * A layout parameter as a row of the tool options panel: its name, its value, and a slider that
 * sets it as it is dragged, at most once per `RELAY_DELAY`, so that the picked breakwater
 * follows the pointer; what it does shows beside the panel while the pointer is on the row.
 */
export function ParameterRow({
  name,
  value,
  min,
  max,
  step,
  unit
}: Readonly<ParameterRowProps>): ReactElement {
  const translate = useTranslate();

  // The value under the pointer while it drags, which the row shows in place of the look's.
  const [dragged, setDragged] = useState<number>();

  const draggedRef = useRef<number>(undefined);

  const isDragging = useRef(false);

  // The value the drag set last, and the wait before it may set the next.
  const sent = useRef<number>(undefined);

  const wait = useRef<ReturnType<typeof setTimeout>>(undefined);

  // Sets the value under the pointer, unless the drag already set that one; whether it set one.
  const send = useCallback((): boolean => {
    if (draggedRef.current == null || draggedRef.current == sent.current) {
      return false;
    }

    sent.current = draggedRef.current;
    bindings.setParameter(name, draggedRef.current);

    return true;
  }, [name]);

  useEffect(
    () => () => {
      if (wait.current != null) {
        clearTimeout(wait.current);
      }
    },
    []
  );

  // The game's slider gives its range and how far along it the pointer is, from 0 to 1.
  const snap = useCallback(
    (start: number, end: number, fraction: number) =>
      Math.min(end, Math.max(start, start + Math.round((fraction * (end - start)) / step) * step)),
    [step]
  );

  const onChange = useCallback(
    (changed: number) => {
      // A change outside a drag, as a step from the keyboard, sets the parameter at once.
      if (!isDragging.current) {
        bindings.setParameter(name, changed);

        return;
      }

      draggedRef.current = changed;
      setDragged(changed);

      // [PERF] The first value at once, then the one under the pointer each time the wait ends: a
      // wait that set a value starts another, so that a steady drag sets one value per wait, where
      // a one-shot wait let the next change set its value at once, two per wait.
      if (wait.current == null) {
        send();

        wait.current = setTimeout(function relay() {
          wait.current = send() ? setTimeout(relay, RELAY_DELAY) : undefined;
        }, RELAY_DELAY);
      }
    },
    [name, send]
  );

  const onDragStart = useCallback(() => {
    isDragging.current = true;
    sent.current = undefined;
  }, []);

  const onDragEnd = useCallback(() => {
    isDragging.current = false;

    if (wait.current != null) {
      clearTimeout(wait.current);
      wait.current = undefined;
    }

    send();

    draggedRef.current = undefined;
    setDragged(undefined);
  }, [send]);

  // A value past the slider's range, as a style or another mod can set, shows as it is.
  const shown = dragged ?? value;

  return (
    <Row
      title={translate(`BreakwaterTool.Tool.PARAMETER[${name}]`)}
      description={translate(`BreakwaterTool.Tool.PARAMETER_DESCRIPTION[${name}]`)}>
      <div className={styles.parameterRowValue}>{FORMATS[unit](shown)}</div>

      <Slider
        className={`${toolOptionsClasses.slider} ${styles.parameterRowSlider}`}
        focusKey={`parameter-${name}`}
        start={min}
        end={max}
        value={Math.min(max, Math.max(min, shown))}
        gamepadStep={step}
        valueTransformer={snap}
        onChange={onChange}
        onDragStart={onDragStart}
        onDragEnd={onDragEnd}
      />
    </Row>
  );
}

/**
 * A parameter's value as the row shows it, by unit; metres to a tenth.
 */
const FORMATS: Readonly<Record<ParameterUnit, (value: number) => string>> = {
  percent: value => `${Math.round(value * PERCENT)} %`,
  degrees: value => `${Math.round(value)}°`,
  metres: value => `${value.toFixed(1)} m`
};
