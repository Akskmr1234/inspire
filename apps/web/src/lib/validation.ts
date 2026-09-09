import { useCallback, useState } from 'react';

/** What a form has to say about its fields, keyed by field name. */
export type FieldErrors = Readonly<Record<string, string>>;

/**
 * Form validation, in the one place every screen can agree on.
 *
 * The pattern this replaces was a submit button disabled by a boolean expression:
 * `disabled={busy || code.trim() === '' || name.trim() === ''}`. It refuses the
 * form without ever saying which field is at fault, it cannot express a rule about
 * two fields at once, and each screen wrote a slightly different one — the customer
 * master demanded a code the product master issued automatically, and neither said
 * so. Worse, a dead button gives no feedback at all: the reader presses it, nothing
 * happens, and there is nothing on the screen to read.
 *
 * So the button stays live, the form is checked when it is submitted, and what is
 * wrong is said next to the field it is wrong about. Errors appear only after a
 * first submission — complaining that a field is empty before anybody has had the
 * chance to fill it in is the other way a form nags.
 */
export function useValidation<TValues>(validate: (values: TValues) => FieldErrors): {
  /** What to show. Empty until the form has been submitted once. */
  readonly errors: FieldErrors;
  /** Checks the values; returns true when the form may be sent. */
  readonly submit: (values: TValues) => boolean;
  /** Clears the messages — after a successful save, or when a dialog reopens. */
  readonly reset: () => void;
} {
  const [errors, setErrors] = useState<FieldErrors>({});

  const submit = useCallback(
    (values: TValues): boolean => {
      const found = validate(values);
      setErrors(found);

      return Object.keys(found).length === 0;
    },
    [validate],
  );

  const reset = useCallback(() => setErrors({}), []);

  return { errors, submit, reset };
}

/** Collects field messages, dropping the fields that have nothing wrong with them. */
export function collect(
  checks: Readonly<Record<string, string | null | undefined>>,
): FieldErrors {
  const errors: Record<string, string> = {};

  for (const [field, message] of Object.entries(checks)) {
    if (message) {
      errors[field] = message;
    }
  }

  return errors;
}

/** A value that has to be there. */
export function required(value: string, message: string): string | null {
  return value.trim() === '' ? message : null;
}

/**
 * A number that has to be a number, and within bounds where bounds are given.
 *
 * An empty string passes: whether the field is optional is the caller's question,
 * and pairing this with {@link required} is how they say it is not.
 */
export function numeric(
  value: string,
  message: string,
  options: {
    readonly min?: number;
    readonly max?: number;
    readonly integer?: boolean;
  } = {},
): string | null {
  if (value.trim() === '') {
    return null;
  }

  const parsed = Number(value);

  if (!Number.isFinite(parsed)) {
    return message;
  }

  if (options.integer === true && !Number.isInteger(parsed)) {
    return message;
  }

  if (options.min !== undefined && parsed < options.min) {
    return message;
  }

  if (options.max !== undefined && parsed > options.max) {
    return message;
  }

  return null;
}

/** A value no longer than the column behind it. */
export function maxLength(value: string, limit: number, message: string): string | null {
  return value.trim().length > limit ? message : null;
}

/**
 * The next code in a sequence, read off the codes already in use.
 *
 * A master whose codes are `C0001`, `C0002`, `C0007` answers `C0008`: the prefix and
 * the width come from the highest code that looks like a number with a prefix, so a
 * firm numbering its customers `CUST-001` keeps getting `CUST-002` rather than being
 * handed this application's idea of what a code should look like.
 *
 * Only ever a suggestion. The field it fills stays editable, and a firm that types
 * its own codes is not fighting anything — which is the whole reason the code is not
 * mandatory in the first place.
 */
export function nextCode(
  existing: readonly string[],
  fallbackPrefix = '',
  fallbackWidth = 4,
): string {
  let bestPrefix: string | null = null;
  let bestNumber = 0;
  let bestWidth = 0;

  for (const code of existing) {
    const match = /^(.*?)(\d+)$/.exec(code.trim());

    if (!match) {
      continue;
    }

    const prefix = match[1] ?? '';
    const digits = match[2] ?? '';
    const value = Number(digits);

    // The highest number wins, and its own prefix and width come with it. Comparing
    // across prefixes rather than per prefix on purpose: a master with two schemes
    // in it is a master somebody is midway through renaming, and continuing the one
    // they have got furthest with is the likelier guess.
    if (value > bestNumber) {
      bestPrefix = prefix;
      bestNumber = value;
      bestWidth = digits.length;
    }
  }

  if (bestPrefix === null) {
    return `${fallbackPrefix}${String(1).padStart(fallbackWidth, '0')}`;
  }

  return `${bestPrefix}${String(bestNumber + 1).padStart(bestWidth, '0')}`;
}
