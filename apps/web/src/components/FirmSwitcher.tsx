import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { chooseFirm, currentFirmId, sessionFirmIds, type ApiError } from '@/lib/api';
import { listMyFirms, type FirmSummary } from '@/lib/firms';

/**
 * The firm the session works in, in the top bar, for someone who may work in more
 * than one. Nothing at all for everybody else.
 *
 * Switching firm moves to one of its branches as well - its head office where the
 * user may work there - and reloads: a firm is a set of books of its own, and nothing
 * on the screen carries over from the last one.
 */
export function FirmSwitcher(): React.JSX.Element | null {
  const { t } = useTranslation();
  const firms = useQuery<readonly FirmSummary[], ApiError>({
    queryKey: ['my-firms'],
    queryFn: listMyFirms,
    staleTime: 5 * 60_000,
  });

  // Only the firms this sign-in carries: one granted since is offered once the
  // session renews, when the server will accept it too.
  const granted = sessionFirmIds();
  const usable = (firms.data ?? []).filter((firm) => granted.includes(firm.firmId));

  if (usable.length < 2) return null;

  return (
    <label className="hidden items-center sm:flex">
      <span className="sr-only">{t('firms.switch')}</span>
      <select
        value={currentFirmId() ?? ''}
        title={t('firms.switch')}
        onChange={(event) => {
          const firm = usable.find(
            (candidate) => candidate.firmId === event.target.value,
          );
          if (!firm) return;
          chooseFirm(firm.firmId, firm.branches[0]?.branchId ?? null);
          window.location.reload();
        }}
        className="field-input-sm max-w-48 truncate py-1 text-xs font-semibold"
      >
        {usable.map((firm) => (
          <option key={firm.firmId} value={firm.firmId}>
            {firm.name}
          </option>
        ))}
      </select>
    </label>
  );
}
