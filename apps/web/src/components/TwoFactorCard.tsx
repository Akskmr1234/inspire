import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { MfaFactorKind, type ApiError } from '@/lib/api';
import {
  beginMfa,
  confirmMfa,
  fetchMfa,
  removeMfa,
  secretOf,
  type MfaEnrolmentStarted,
  type MfaFactorView,
  type MfaOverview,
} from '@/lib/account';

const MFA_KEY = ['account-mfa'] as const;

/** How often a phone's registration is checked while push is being set up. */
const REGISTER_POLL_MS = 3000;

/**
 * Two-step sign-in on the profile's security tab: the factors the user has, and
 * adding an authenticator app, a phone for push approval, or backup codes.
 *
 * The secrets are held by the second-factor server (privacyIDEA); this screen only
 * ever shows a QR code to scan, or backup codes once, at the moment they are made.
 */
export function TwoFactorCard({
  icon,
  title,
}: {
  readonly icon: React.ReactNode;
  /** The card's heading component, so it matches the cards beside it. */
  readonly title: (props: {
    readonly icon?: React.ReactNode;
    readonly title: string;
  }) => React.JSX.Element;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const Title = title;

  const overview = useQuery<MfaOverview, ApiError>({
    queryKey: MFA_KEY,
    queryFn: fetchMfa,
  });
  const [setup, setSetup] = useState<MfaEnrolmentStarted | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const refresh = (): Promise<void> =>
    queryClient.invalidateQueries({ queryKey: MFA_KEY });

  const begin = useMutation<MfaEnrolmentStarted, ApiError, number>({
    mutationFn: (kind) => beginMfa(kind),
    onSuccess: async (started) => {
      setNotice(null);
      setSetup(started);
      await refresh();
    },
  });

  const remove = useMutation<void, ApiError, MfaFactorView>({
    mutationFn: (factor) => removeMfa(factor.factorId),
    onSuccess: async () => {
      setNotice(t('mfa.removed'));
      await refresh();
    },
  });

  const finished = async (message: string): Promise<void> => {
    setSetup(null);
    setNotice(message);
    await refresh();
  };

  const factors = overview.data?.factors ?? [];
  const active = factors.filter((f) => f.isActive);
  const hasBackup = active.some((f) => f.kind === MfaFactorKind.backupCodes);
  const failure = begin.error ?? remove.error;

  return (
    <section className="card card-body space-y-4 lg:col-span-2">
      <Title icon={icon} title={t('mfa.cardTitle')} />

      {overview.isPending && (
        <p className="text-xs text-ink-muted">{t('common.loading')}</p>
      )}

      {overview.data && !overview.data.available && (
        <p className="rounded-lg border border-dashed border-line p-4 text-sm text-ink-muted">
          {t('mfa.notOffered')}
        </p>
      )}

      {overview.data?.available && (
        <>
          <p className="text-sm text-ink-muted">
            {active.length > 0 ? t('mfa.onIntro') : t('mfa.offIntro')}
          </p>

          {failure && (
            <p role="alert" className="alert-error">
              {failure.detail || failure.code}
            </p>
          )}
          {notice && (
            <p role="status" className="alert-success">
              {notice}
            </p>
          )}

          {factors.length > 0 && (
            <ul className="divide-y divide-line rounded-xl border border-line">
              {factors.map((factor) => (
                <li
                  key={factor.factorId}
                  className="flex flex-wrap items-center gap-3 px-4 py-3"
                >
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-medium text-ink">
                      {kindName(factor.kind, t)}
                      {!DEFAULT_LABELS.includes(factor.label) && (
                        <span className="text-ink-muted"> · {factor.label}</span>
                      )}
                    </p>
                    <p className="text-xs text-ink-muted">
                      {factor.isActive
                        ? factor.lastUsedAtUtc
                          ? t('mfa.lastUsed', {
                              when: new Date(factor.lastUsedAtUtc).toLocaleString(),
                            })
                          : t('mfa.neverUsed')
                        : t('mfa.unfinished')}
                    </p>
                  </div>
                  <span className={factor.isActive ? 'badge-success' : 'badge-warn'}>
                    {factor.isActive ? t('mfa.active') : t('mfa.pending')}
                  </span>
                  <button
                    type="button"
                    disabled={remove.isPending}
                    onClick={() => {
                      if (window.confirm(t('mfa.confirmRemove'))) remove.mutate(factor);
                    }}
                    className="row-action row-action-neutral"
                  >
                    {t('common.remove')}
                  </button>
                </li>
              ))}
            </ul>
          )}

          {setup ? (
            <SetupPanel setup={setup} onDone={finished} onCancel={() => setSetup(null)} />
          ) : (
            <div className="flex flex-wrap gap-2">
              <button
                type="button"
                disabled={begin.isPending}
                onClick={() => begin.mutate(MfaFactorKind.authenticator)}
                className="btn-primary btn-sm"
              >
                {t('mfa.addAuthenticator')}
              </button>
              {overview.data.pushAvailable && (
                <button
                  type="button"
                  disabled={begin.isPending}
                  onClick={() => begin.mutate(MfaFactorKind.push)}
                  className="btn-secondary btn-sm"
                >
                  {t('mfa.addPush')}
                </button>
              )}
              <button
                type="button"
                disabled={begin.isPending}
                onClick={() => {
                  if (!hasBackup || window.confirm(t('mfa.confirmNewBackup'))) {
                    begin.mutate(MfaFactorKind.backupCodes);
                  }
                }}
                className="btn-secondary btn-sm"
              >
                {hasBackup ? t('mfa.newBackupCodes') : t('mfa.addBackupCodes')}
              </button>
            </div>
          )}
        </>
      )}
    </section>
  );
}

/** Finishing one factor's set-up: scan and type a code, scan and wait, or save the codes. */
function SetupPanel({
  setup,
  onDone,
  onCancel,
}: {
  readonly setup: MfaEnrolmentStarted;
  readonly onDone: (message: string) => Promise<void>;
  readonly onCancel: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [code, setCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Push is finished by the phone, not the user: ask until it has registered.
  useEffect(() => {
    if (setup.kind !== MfaFactorKind.push) return undefined;

    let stopped = false;
    let timer = 0;
    const check = async (): Promise<void> => {
      try {
        const result = await confirmMfa(setup.factorId);
        if (result.active) {
          if (!stopped) await onDone(t('mfa.pushReady'));
          return;
        }
      } catch {
        // A blip while waiting: keep asking.
      }
      if (!stopped) timer = window.setTimeout(() => void check(), REGISTER_POLL_MS);
    };

    timer = window.setTimeout(() => void check(), REGISTER_POLL_MS);
    return () => {
      stopped = true;
      window.clearTimeout(timer);
    };
  }, [setup, onDone, t]);

  if (setup.kind === MfaFactorKind.backupCodes) {
    const codes = setup.backupCodes ?? [];
    const text = codes.join('\n');

    return (
      <div className="space-y-3 rounded-xl border border-line bg-surface-2/50 p-4">
        <p className="text-sm font-medium text-ink">{t('mfa.backupTitle')}</p>
        <p className="text-xs text-ink-muted">{t('mfa.backupHint')}</p>
        <ol className="grid grid-cols-2 gap-2 font-mono text-sm tabular-nums sm:grid-cols-5">
          {codes.map((c) => (
            <li
              key={c}
              className="rounded-md border border-line bg-surface px-2 py-1 text-center"
            >
              {c}
            </li>
          ))}
        </ol>
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => void navigator.clipboard?.writeText(text)}
            className="btn-secondary btn-sm"
          >
            {t('mfa.copy')}
          </button>
          <a
            href={`data:text/plain;charset=utf-8,${encodeURIComponent(text)}`}
            download="backup-codes.txt"
            className="btn-secondary btn-sm"
          >
            {t('mfa.download')}
          </a>
          <button
            type="button"
            onClick={() => void onDone(t('mfa.backupSaved'))}
            className="btn-primary btn-sm ms-auto"
          >
            {t('mfa.savedThem')}
          </button>
        </div>
      </div>
    );
  }

  const isPush = setup.kind === MfaFactorKind.push;
  const secret = secretOf(setup.setupUrl);

  const confirm = async (event: React.FormEvent): Promise<void> => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = await confirmMfa(setup.factorId, code);
      if (result.active) await onDone(t('mfa.authenticatorReady'));
    } catch (cause) {
      setError((cause as ApiError).detail || t('mfa.wrongCode'));
      setCode('');
    } finally {
      setBusy(false);
    }
  };

  return (
    <form
      onSubmit={(event) => void confirm(event)}
      className="grid gap-4 rounded-xl border border-line bg-surface-2/50 p-4 sm:grid-cols-[auto_1fr]"
    >
      {setup.qrImage && (
        <img
          src={setup.qrImage}
          alt={t('mfa.qrAlt')}
          className="mx-auto size-44 rounded-lg bg-white p-2 sm:mx-0"
        />
      )}
      <div className="min-w-0 space-y-3">
        <ol className="list-decimal space-y-1 ps-5 text-sm text-ink">
          {isPush ? (
            <>
              <li>{t('mfa.pushStep1')}</li>
              <li>{t('mfa.pushStep2')}</li>
              <li>{t('mfa.pushStep3')}</li>
            </>
          ) : (
            <>
              <li>{t('mfa.authStep1')}</li>
              <li>{t('mfa.authStep2')}</li>
              <li>{t('mfa.authStep3')}</li>
            </>
          )}
        </ol>

        {!isPush && secret && (
          <details className="text-xs text-ink-muted">
            <summary className="cursor-pointer">{t('mfa.cantScan')}</summary>
            <code className="mt-1 block break-all rounded bg-surface px-2 py-1 font-mono text-ink">
              {secret}
            </code>
          </details>
        )}

        {error && (
          <p role="alert" className="alert-error">
            {error}
          </p>
        )}

        {isPush ? (
          <p
            className="flex items-center gap-2 text-sm text-ink-muted"
            aria-live="polite"
          >
            <span className="size-2 animate-pulse rounded-full bg-brand-500" />
            {t('mfa.waitingForPhone')}
          </p>
        ) : (
          <label className="block max-w-56">
            <span className="field-label">{t('mfa.codeLabel')}</span>
            <input
              value={code}
              onChange={(event) => setCode(event.target.value)}
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={6}
              required
              autoFocus
              className="field-input text-center tracking-[0.3em] tabular-nums"
            />
          </label>
        )}

        <div className="flex flex-wrap gap-2">
          {!isPush && (
            <button
              type="submit"
              disabled={busy || code.trim().length < 6}
              className="btn-primary btn-sm"
            >
              {t('mfa.turnOn')}
            </button>
          )}
          <button type="button" onClick={onCancel} className="btn-ghost btn-sm">
            {t('common.cancel')}
          </button>
        </div>
      </div>
    </form>
  );
}

/** The names the server gives a factor when the user gives none: not worth repeating. */
const DEFAULT_LABELS = ['Authenticator app', 'Phone', 'Backup codes'];

function kindName(kind: number, t: (key: string) => string): string {
  if (kind === MfaFactorKind.push) return t('mfa.kindPush');
  if (kind === MfaFactorKind.authenticator) return t('mfa.kindAuthenticator');
  return t('mfa.kindBackupList');
}
