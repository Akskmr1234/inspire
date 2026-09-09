import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Field } from '@/components/Form';
import { translate } from '@/lib/translate';
import { useSettings } from '@/stores/settings';

/**
 * The Arabic half of a bilingual name, filled in from the English one.
 *
 * Three rules, and the second is the one that matters:
 *
 *   It only fills in when the firm has turned translation on in Settings.
 *
 *   It stops filling in the moment somebody types in it. A field that kept
 *   overwriting a correction every time the English name was touched would be worse
 *   than one that never filled itself in at all — the reader would have to fix it
 *   last, every time, and would not know that.
 *
 *   Whatever arrives is an ordinary editable value. Machine translation of a product
 *   name is a first draft, and the firm printing it on an invoice is the one who
 *   decides whether it is right.
 */
export function ArabicNameField({
  label,
  source,
  value,
  onChange,
  error,
  required = false,
}: {
  readonly label: string;
  /** The English text this is the translation of. */
  readonly source: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly error?: string | undefined;
  readonly required?: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();
  const { autoTranslateArabic, translationApiKey } = useSettings();

  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  // Set the first time the reader types here, and never unset. This is what makes
  // the automatic fill a suggestion rather than a struggle.
  const touched = useRef(false);
  // What was last translated, so re-rendering on an unrelated keystroke does not
  // send the same phrase to the service again.
  const lastTranslated = useRef('');

  const run = async (text: string): Promise<void> => {
    setBusy(true);
    setFailed(false);

    const translated = await translate(text, 'ar', 'en', translationApiKey);

    setBusy(false);

    if (translated === null) {
      setFailed(true);
      return;
    }

    lastTranslated.current = text.trim();
    onChange(translated);
  };

  useEffect(() => {
    const phrase = source.trim();

    if (
      !autoTranslateArabic ||
      touched.current ||
      phrase === '' ||
      phrase === lastTranslated.current
    ) {
      return;
    }

    // Debounced, because the source is a text box somebody is still typing in and
    // every keystroke would otherwise be its own request.
    const timer = window.setTimeout(() => void run(phrase), 700);

    return () => window.clearTimeout(timer);
    // `run` is recreated every render and depends only on values read at call time.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [source, autoTranslateArabic, translationApiKey]);

  return (
    <Field
      label={label}
      required={required}
      error={error}
      {...(failed ? { hint: t('settings.translateFailed') } : {})}
    >
      <div className="flex items-stretch gap-2">
        <input
          dir="rtl"
          value={value}
          aria-label={label}
          aria-invalid={error ? true : undefined}
          onChange={(event) => {
            touched.current = true;
            onChange(event.target.value);
          }}
          className={error ? 'field-input field-invalid' : 'field-input'}
        />

        {/*
          Offered whether or not the automatic fill is on, because somebody who has
          just corrected the English name wants this one phrase translated without
          turning a setting on for the whole installation.
        */}
        <button
          type="button"
          disabled={busy || source.trim() === ''}
          onClick={() => void run(source)}
          className="btn-secondary btn-sm shrink-0"
          title={t('settings.translateNow')}
        >
          {busy ? t('settings.translating') : t('settings.translateNow')}
        </button>
      </div>
    </Field>
  );
}
