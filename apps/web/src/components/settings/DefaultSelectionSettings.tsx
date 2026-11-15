import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  defaultSelection,
  saveDefaultSelection,
  sessionBranchIds,
  sessionFirmIds,
  type ApiError,
} from '@/lib/api';
import { listMyFirms, type FirmSummary } from '@/lib/firms';

/**
 * Where this user starts when they sign in: a firm and one of its branches.
 *
 * Only the firms and branches the user may work in are offered. A firm list of one is
 * not a choice and is not shown; nor is the branch list when the firm has one branch.
 * Kept with this sign-in on this device, and applied at the next sign-in.
 */
export function DefaultSelectionSettings({
  card: Card,
}: {
  readonly card: (props: {
    readonly title: string;
    readonly children: React.ReactNode;
  }) => React.JSX.Element;
}): React.JSX.Element | null {
  const { t } = useTranslation();
  const firms = useQuery<readonly FirmSummary[], ApiError>({
    queryKey: ['my-firms'],
    queryFn: listMyFirms,
    staleTime: 5 * 60_000,
  });
  const [chosen, setChosen] = useState(defaultSelection);

  const grantedFirms = sessionFirmIds();
  const grantedBranches = sessionBranchIds();
  const usable = (firms.data ?? [])
    .filter((firm) => grantedFirms.includes(firm.firmId))
    .map((firm) => ({
      ...firm,
      branches: firm.branches.filter((branch) =>
        grantedBranches.includes(branch.branchId),
      ),
    }));

  // One firm with one branch leaves nothing to choose.
  const onlyFirm = usable.length === 1 ? usable[0] : undefined;
  if (usable.length === 0 || (onlyFirm && onlyFirm.branches.length < 2)) return null;

  const firmId = chosen?.firmId ?? onlyFirm?.firmId ?? '';
  const firm = usable.find((candidate) => candidate.firmId === firmId);
  const branches = firm?.branches ?? [];

  const save = (nextFirm: string, nextBranch: string | null): void => {
    const next = nextFirm === '' ? null : { firmId: nextFirm, branchId: nextBranch };
    saveDefaultSelection(next);
    setChosen(next);
  };

  return (
    <Card title={t('firms.defaultTitle')}>
      <div className="grid gap-3 sm:grid-cols-2">
        {usable.length > 1 && (
          <label className="field">
            <span className="field-label">{t('firms.defaultFirm')}</span>
            <select
              value={firmId}
              onChange={(event) => {
                const picked = usable.find(
                  (candidate) => candidate.firmId === event.target.value,
                );
                save(event.target.value, picked?.branches[0]?.branchId ?? null);
              }}
              className="field-input"
            >
              <option value="">{t('firms.noDefault')}</option>
              {usable.map((candidate) => (
                <option key={candidate.firmId} value={candidate.firmId}>
                  {candidate.name}
                </option>
              ))}
            </select>
          </label>
        )}
        {firm && branches.length > 1 && (
          <label className="field">
            <span className="field-label">{t('firms.defaultBranch')}</span>
            <select
              value={chosen?.branchId ?? ''}
              onChange={(event) => save(firm.firmId, event.target.value || null)}
              className="field-input"
            >
              <option value="">{t('firms.noDefault')}</option>
              {branches.map((branch) => (
                <option key={branch.branchId} value={branch.branchId}>
                  {branch.name}
                </option>
              ))}
            </select>
          </label>
        )}
      </div>
      <p className="field-hint">{t('firms.defaultHint')}</p>
    </Card>
  );
}
