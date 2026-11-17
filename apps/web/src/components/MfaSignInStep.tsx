import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { Spinner } from '@/components/ReportFrame';
import {
  ApiError,
  MfaFactorKind,
  pollMfaPush,
  sendMfaPush,
  verifyMfaCode,
  type MfaChallenge,
} from '@/lib/api';
import { useSession } from '@/stores/session';

/** How often the phone's answer is asked for. */
const POLL_MS = 2000;

/**
 * The second step of signing in: approve on the phone, or type a code.
 *
 * Push is offered first when the user has it - one tap on the phone, nothing to
 * type - and is sent as soon as the step opens. The other factors are a tab away,
 * so a phone that is flat or out of reach never locks anybody out.
 */
export function MfaSignInStep({
  challenge,
  tenantCode,
  onCancel,
}: {
  readonly challenge: MfaChallenge;
  readonly tenantCode: string;
  /** Back to the password step: the ticket expired, or the user chose to. */
  readonly onCancel: (message?: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const completeSignIn = useSession((s) => s.completeSignIn);

  const first = challenge.factors[0];
  const [factorId, setFactorId] = useState(first?.factorId ?? '');
  const factor = challenge.factors.find((f) => f.factorId === factorId) ?? first;
  const isPush = factor?.kind === MfaFactorKind.push;

  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pushState, setPushState] = useState<'idle' | 'sending' | 'waiting' | 'expired'>(
    'idle',
  );
  const pollTimer = useRef<number | null>(null);

  const stopPolling = (): void => {
    if (pollTimer.current !== null) window.clearTimeout(pollTimer.current);
    pollTimer.current = null;
  };

  useEffect(() => stopPolling, []);

  const fail = useCallback(
    (cause: unknown): void => {
      if (
        cause instanceof ApiError &&
        cause.status === 401 &&
        cause.code !== 'Authentication.WrongCode'
      ) {
        onCancel(t('mfa.timedOut'));
        return;
      }
      setError(
        cause instanceof ApiError ? cause.detail || cause.code : t('mfa.unreachable'),
      );
    },
    [onCancel, t],
  );

  const sendPush = useCallback(async (): Promise<void> => {
    if (!factor) return;
    stopPolling();
    setError(null);
    setPushState('sending');

    try {
      const sent = await sendMfaPush(challenge, factor.factorId);
      const expires = Date.parse(sent.expiresAtUtc);
      setPushState('waiting');

      const poll = async (): Promise<void> => {
        if (Date.now() >= expires) {
          setPushState('expired');
          return;
        }
        try {
          const auth = await pollMfaPush(challenge, sent.pushTicket, tenantCode);
          if (auth) {
            await completeSignIn(auth, tenantCode);
            return;
          }
          pollTimer.current = window.setTimeout(() => void poll(), POLL_MS);
        } catch (cause) {
          setPushState('idle');
          fail(cause);
        }
      };

      pollTimer.current = window.setTimeout(() => void poll(), POLL_MS);
    } catch (cause) {
      setPushState('idle');
      fail(cause);
    }
  }, [challenge, completeSignIn, factor, fail, tenantCode]);

  // Push goes out the moment it is the chosen factor, so the phone is already
  // buzzing by the time the user reaches for it.
  useEffect(() => {
    if (isPush && pushState === 'idle') void sendPush();
    if (!isPush) {
      stopPolling();
      setPushState('idle');
    }
    // Only when the chosen factor changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [factorId]);

  const submit = async (event: React.FormEvent): Promise<void> => {
    event.preventDefault();
    if (!factor || isPush) return;
    setError(null);
    setBusy(true);

    try {
      const auth = await verifyMfaCode(challenge, factor.factorId, code, tenantCode);
      await completeSignIn(auth, tenantCode);
    } catch (cause) {
      setCode('');
      fail(cause);
    } finally {
      setBusy(false);
    }
  };

  const kindLabel = (kind: number): string =>
    kind === MfaFactorKind.push
      ? t('mfa.kindPush')
      : kind === MfaFactorKind.authenticator
        ? t('mfa.kindAuthenticator')
        : t('mfa.kindBackup');

  return (
    <form
      onSubmit={(event) => void submit(event)}
      className="card animate-rise relative w-full max-w-sm rounded-2xl border border-line p-6 shadow-float sm:p-8"
    >
      <h1 className="text-lg font-bold tracking-tight text-ink">{t('mfa.title')}</h1>
      <p className="mt-1 text-sm text-ink-muted">{t('mfa.subtitle')}</p>

      {challenge.factors.length > 1 && (
        <div
          role="tablist"
          aria-label={t('mfa.chooseFactor')}
          className="mt-5 flex flex-wrap gap-1 rounded-lg border border-line bg-surface-2 p-1"
        >
          {challenge.factors.map((choice) => (
            <button
              key={choice.factorId}
              type="button"
              role="tab"
              aria-selected={choice.factorId === factor?.factorId}
              onClick={() => {
                setFactorId(choice.factorId);
                setError(null);
                setCode('');
              }}
              className={clsx(
                'flex-1 rounded-md px-2.5 py-1.5 text-xs font-medium transition',
                choice.factorId === factor?.factorId
                  ? 'bg-surface text-ink shadow-sm ring-1 ring-line'
                  : 'text-ink-muted hover:text-ink',
              )}
            >
              {kindLabel(choice.kind)}
            </button>
          ))}
        </div>
      )}

      {error !== null && (
        <p role="alert" className="alert-error mt-4">
          {error}
        </p>
      )}

      {isPush ? (
        <div className="mt-5 space-y-4 text-center" aria-live="polite">
          {pushState === 'expired' ? (
            <p className="text-sm text-ink-muted">{t('mfa.pushExpired')}</p>
          ) : (
            <>
              <div className="mx-auto grid size-14 place-items-center rounded-full bg-brand-500/10 text-brand-600 dark:text-brand-300">
                <Spinner />
              </div>
              <p className="text-sm text-ink">
                {pushState === 'waiting' ? t('mfa.pushWaiting') : t('mfa.pushSending')}
              </p>
              <p className="text-xs text-ink-muted">{factor?.label}</p>
            </>
          )}
          <button
            type="button"
            onClick={() => void sendPush()}
            disabled={pushState === 'sending'}
            className="btn-secondary w-full"
          >
            {t('mfa.pushResend')}
          </button>
        </div>
      ) : (
        <div className="mt-5 space-y-4">
          <label className="block">
            <span className="field-label">
              {factor?.kind === MfaFactorKind.backupCodes
                ? t('mfa.backupCodeLabel')
                : t('mfa.codeLabel')}
            </span>
            <input
              value={code}
              onChange={(event) => setCode(event.target.value)}
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={12}
              autoFocus
              required
              className="field-input text-center text-lg tracking-[0.3em] tabular-nums"
            />
          </label>
          <button
            type="submit"
            disabled={busy || code.trim().length < 6}
            className="btn-primary w-full py-2.5"
          >
            {busy && <Spinner />}
            {t('mfa.verify')}
          </button>
        </div>
      )}

      <button
        type="button"
        onClick={() => {
          stopPolling();
          onCancel();
        }}
        className="mt-5 w-full text-center text-xs font-medium text-ink-muted underline underline-offset-2 hover:text-ink"
      >
        {t('mfa.backToSignIn')}
      </button>
    </form>
  );
}
