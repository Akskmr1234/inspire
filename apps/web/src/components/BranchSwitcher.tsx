import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  chooseBranch,
  currentBranchId,
  sessionBranchIds,
  type ApiError,
} from '@/lib/api';
import { listMyBranches, type BranchSummary } from '@/lib/branches';

/**
 * The branch the session works in, in the top bar, for someone who may work in more
 * than one. Nothing at all for everybody else.
 *
 * Switching reloads the screen: every figure on it - stock, numbering, the documents
 * listed - belongs to the branch, and a page half of one branch and half of another
 * would be worse than the second it takes to draw it again.
 */
export function BranchSwitcher(): React.JSX.Element | null {
  const { t } = useTranslation();
  const branches = useQuery<readonly BranchSummary[], ApiError>({
    queryKey: ['my-branches'],
    queryFn: listMyBranches,
    staleTime: 5 * 60_000,
  });

  // Only the branches this sign-in carries: one granted since is offered once the
  // session renews, when the server will accept it too.
  const granted = sessionBranchIds();
  const usable = (branches.data ?? []).filter((branch) =>
    granted.includes(branch.branchId),
  );

  if (usable.length < 2) return null;

  const current = currentBranchId();

  return (
    <label className="hidden items-center sm:flex">
      <span className="sr-only">{t('branches.switch')}</span>
      <select
        value={current ?? ''}
        title={t('branches.switch')}
        onChange={(event) => {
          chooseBranch(event.target.value);
          window.location.reload();
        }}
        className="field-input-sm max-w-44 truncate py-1 text-xs font-medium"
      >
        {usable.map((branch) => (
          <option key={branch.branchId} value={branch.branchId}>
            {branch.name}
          </option>
        ))}
      </select>
    </label>
  );
}
