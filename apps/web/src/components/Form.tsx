import { useId } from 'react';
import { SearchSelect } from '@/components/SearchSelect';
import clsx from 'clsx';

/**
 * The form controls every screen in the application builds its fields from.
 *
 * Written once here because the alternative was what the screens had: a `<label>`
 * with a `<span className="field-label">` on the masters, a bare `<label>` carrying
 * its own text on the purchase screen, a `<div>` with a separate `htmlFor` on
 * voucher entry, and three different ideas of where the caption sits relative to the
 * box. The captions did not line up across a form because nothing made them, and a
 * mandatory field looked exactly like an optional one because nothing said
 * otherwise.
 *
 * Three rules, applied by every field here:
 *
 *   The caption sits above the control, on the reading edge, at one size — so a row
 *   of fields has one baseline for its captions and one for its boxes.
 *
 *   A mandatory field is marked, in red, next to its caption. Not by colouring the
 *   box, which would say "wrong" rather than "needed", and not by an asterisk in a
 *   colour nobody can see.
 *
 *   An error belongs under the field it is about, and turns that field's box red.
 *   A form that reports its problems in a banner at the top makes the reader find
 *   which of eleven boxes it meant.
 */
export function Field({
  label,
  required = false,
  hint,
  error,
  htmlFor,
  className,
  children,
}: {
  readonly label: string;
  /** Marks the field mandatory and colours the marker red. */
  readonly required?: boolean;
  readonly hint?: string;
  /** Shown under the control, and expected to have made the control red too. */
  readonly error?: string | undefined;
  readonly htmlFor?: string;
  readonly className?: string;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <div className={clsx('field', className)}>
      <label htmlFor={htmlFor} className="field-label">
        {label}
        {required && (
          <span aria-hidden="true" className="field-required">
            *
          </span>
        )}
      </label>

      {children}

      {error ? (
        <p className="field-message-error">{error}</p>
      ) : (
        hint && <p className="field-hint">{hint}</p>
      )}
    </div>
  );
}

/** The properties every control here shares with the field wrapping it. */
interface ControlProps {
  readonly label: string;
  readonly required?: boolean;
  readonly hint?: string;
  readonly error?: string | undefined;
  readonly disabled?: boolean;
  readonly className?: string;
  /** `sm` for the dense rows of a line-item table. */
  readonly size?: 'default' | 'sm';
}

/** A single-line text field. */
export function TextField({
  value,
  onChange,
  placeholder,
  dir,
  type = 'text',
  autoFocus = false,
  maxLength,
  label,
  required = false,
  hint,
  error,
  disabled = false,
  className,
  size = 'default',
}: ControlProps & {
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly placeholder?: string;
  /** `rtl` for the Arabic name fields, so the caret starts on the right. */
  readonly dir?: 'rtl' | 'ltr';
  readonly type?: 'text' | 'search' | 'email' | 'tel';
  readonly autoFocus?: boolean;
  readonly maxLength?: number;
}): React.JSX.Element {
  const id = useId();

  return (
    <Field
      label={label}
      required={required}
      {...(hint === undefined ? {} : { hint })}
      error={error}
      htmlFor={id}
      {...(className === undefined ? {} : { className })}
    >
      <input
        id={id}
        type={type}
        value={value}
        disabled={disabled}
        // eslint-disable-next-line jsx-a11y/no-autofocus
        autoFocus={autoFocus}
        {...(dir === undefined ? {} : { dir })}
        {...(maxLength === undefined ? {} : { maxLength })}
        placeholder={placeholder ?? ''}
        aria-invalid={error ? true : undefined}
        onChange={(event) => onChange(event.target.value)}
        className={clsx(
          size === 'sm' ? 'field-input-sm' : 'field-input',
          error && 'field-invalid',
        )}
      />
    </Field>
  );
}

/** A number field, right-aligned and in figures the eye can compare down a column. */
export function NumberField({
  value,
  onChange,
  min,
  step = 'any',
  placeholder,
  label,
  required = false,
  hint,
  error,
  disabled = false,
  className,
  size = 'default',
}: ControlProps & {
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly min?: number;
  readonly step?: string;
  readonly placeholder?: string;
}): React.JSX.Element {
  const id = useId();

  return (
    <Field
      label={label}
      required={required}
      {...(hint === undefined ? {} : { hint })}
      error={error}
      htmlFor={id}
      {...(className === undefined ? {} : { className })}
    >
      <input
        id={id}
        type="number"
        inputMode="decimal"
        step={step}
        {...(min === undefined ? {} : { min })}
        value={value}
        disabled={disabled}
        placeholder={placeholder ?? ''}
        aria-invalid={error ? true : undefined}
        onChange={(event) => onChange(event.target.value)}
        className={clsx(
          size === 'sm' ? 'field-input-sm' : 'field-input',
          'text-end font-mono tabular-nums',
          error && 'field-invalid',
        )}
      />
    </Field>
  );
}

/** A date field. */
export function DateField({
  value,
  onChange,
  label,
  required = false,
  hint,
  error,
  disabled = false,
  className,
  size = 'default',
}: ControlProps & {
  readonly value: string;
  readonly onChange: (value: string) => void;
}): React.JSX.Element {
  const id = useId();

  return (
    <Field
      label={label}
      required={required}
      {...(hint === undefined ? {} : { hint })}
      error={error}
      htmlFor={id}
      {...(className === undefined ? {} : { className })}
    >
      <input
        id={id}
        type="date"
        value={value}
        disabled={disabled}
        aria-invalid={error ? true : undefined}
        onChange={(event) => onChange(event.target.value)}
        className={clsx(
          size === 'sm' ? 'field-input-sm' : 'field-input',
          error && 'field-invalid',
        )}
      />
    </Field>
  );
}

/**
 * A native select, for the short fixed lists.
 *
 * Anything reaching a master — a ledger, a product, a customer — uses
 * `SearchSelect` instead: a native select of four thousand rows can only be walked,
 * and the browser's own type-ahead matches the start of the option text, which is
 * the code rather than the name somebody is looking up by. This is for the lists
 * that are genuinely short and fixed: a document kind, a status, a tax mode.
 */
export function SelectField<TValue extends string | number>({
  value,
  onChange,
  options,
  label,
  required = false,
  hint,
  error,
  disabled = false,
  className,
  size = 'default',
}: ControlProps & {
  readonly value: TValue;
  readonly onChange: (value: TValue) => void;
  readonly options: readonly {
    readonly value: TValue;
    readonly label: string;
  }[];
}): React.JSX.Element {
  const id = useId();

  return (
    <Field
      label={label}
      required={required}
      {...(hint === undefined ? {} : { hint })}
      error={error}
      htmlFor={id}
      {...(className === undefined ? {} : { className })}
    >
      {/*
        The searchable picker, not the browser's own control.

        Two dropdowns that look and behave differently on the same form read as two
        different kinds of field, and until now which one a screen got depended on
        whether its list came from a master or was written out in the source. They
        are all this one now. A list of three still opens and closes like a list of
        three thousand; what it gains is the same box, the same arrow, the same
        keyboard, and a search that costs nothing when there is nothing to search.
      */}
      <SearchSelect
        id={id}
        value={String(value)}
        onChange={(next) =>
          onChange((typeof value === 'number' ? Number(next) : next) as TValue)
        }
        disabled={disabled}
        invalid={error !== undefined}
        label={label}
        size={size}
        options={options.map((option) => ({
          value: String(option.value),
          label: option.label,
        }))}
      />
    </Field>
  );
}

/** A multi-line field, for narrations and pasted blocks of serial numbers. */
export function TextAreaField({
  value,
  onChange,
  rows = 2,
  placeholder,
  mono = false,
  label,
  required = false,
  hint,
  error,
  disabled = false,
  className,
  size = 'default',
}: ControlProps & {
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly rows?: number;
  readonly placeholder?: string;
  readonly mono?: boolean;
}): React.JSX.Element {
  const id = useId();

  return (
    <Field
      label={label}
      required={required}
      {...(hint === undefined ? {} : { hint })}
      error={error}
      htmlFor={id}
      {...(className === undefined ? {} : { className })}
    >
      <textarea
        id={id}
        rows={rows}
        value={value}
        disabled={disabled}
        placeholder={placeholder ?? ''}
        aria-invalid={error ? true : undefined}
        onChange={(event) => onChange(event.target.value)}
        className={clsx(
          size === 'sm' ? 'field-input-sm' : 'field-input',
          mono && 'font-mono text-xs',
          error && 'field-invalid',
        )}
      />
    </Field>
  );
}

/** A checkbox and the sentence explaining what turning it on does. */
export function CheckField({
  checked,
  onChange,
  label,
  hint,
  disabled = false,
  className,
}: {
  readonly checked: boolean;
  readonly onChange: (checked: boolean) => void;
  readonly label: string;
  readonly hint?: string;
  readonly disabled?: boolean;
  readonly className?: string;
}): React.JSX.Element {
  return (
    <div className={clsx('field', className)}>
      <label className="field-check">
        <input
          type="checkbox"
          checked={checked}
          disabled={disabled}
          onChange={(event) => onChange(event.target.checked)}
        />
        {label}
      </label>
      {hint && <p className="field-hint">{hint}</p>}
    </div>
  );
}
