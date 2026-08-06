import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { DataGrid, type GridColumn } from '@/components/DataGrid';
import { ReportFrame } from '@/components/ReportFrame';
import { request, type ApiError } from '@/lib/api';

/** The ledger kinds, keyed by the wire value the API serialises them as. */
const KIND_NAME: Record<number, string> = {
  1: 'General',
  2: 'Cash',
  3: 'Bank',
  4: 'Customer',
  5: 'Supplier',
};

/** The account natures, keyed by the wire value. */
const NATURE_NAME: Record<number, string> = {
  1: 'Asset',
  2: 'Liability',
  3: 'Equity',
  4: 'Income',
  5: 'Expense',
};

interface LedgerSummary {
  readonly ledgerId: string;
  readonly code: string;
  readonly name: string;
  readonly kind: number;
  readonly groupCode: string;
  readonly groupName: string;
  readonly nature: number;
  readonly currency: string;
  readonly isBillWise: boolean;
}

/**
 * The chart of accounts, as a list.
 *
 * The first screen built on the data grid, and a deliberate choice of first: a chart
 * of accounts is the list people most want to slice differently from each other. A
 * bookkeeper wants code and name; a credit controller wants the customers and whether
 * they are settled bill by bill; an auditor wants the group and its nature. All three
 * are the same rows, arranged differently and remembered per person.
 */
export function LedgersPage(): React.JSX.Element {
  const { t } = useTranslation();

  const query = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers'],
    queryFn: () => request<readonly LedgerSummary[]>('/accounting/ledgers'),
  });

  const columns = useMemo<readonly GridColumn<LedgerSummary>[]>(
    () => [
      {
        key: 'code',
        header: t('reports.ledger'),
        value: (row) => row.code,
      },
      {
        key: 'name',
        header: t('ledgers.name'),
        value: (row) => row.name,
      },
      {
        key: 'kind',
        header: t('ledgers.kind'),
        value: (row) => t(`ledgerKinds.${KIND_NAME[row.kind]}`),
      },
      {
        key: 'groupCode',
        header: t('ledgers.groupCode'),
        value: (row) => row.groupCode,
        hiddenByDefault: true,
      },
      {
        key: 'groupName',
        header: t('reports.group'),
        value: (row) => row.groupName,
      },
      {
        key: 'nature',
        header: t('ledgers.nature'),
        value: (row) => t(`accountNatures.${NATURE_NAME[row.nature]}`),
      },
      {
        key: 'currency',
        header: t('ledgers.currency'),
        value: (row) => row.currency,
      },
      {
        key: 'billWise',
        header: t('ledgers.billWise'),
        value: (row) => (row.isBillWise ? t('common.yes') : t('common.no')),
      },
    ],
    [t],
  );

  return (
    <ReportFrame title={t('nav.ledgers')} controls={null} query={query}>
      {(data) => (
        <DataGrid
          gridKey="ledgers"
          rows={data}
          columns={columns}
          rowKey={(row) => row.ledgerId}
        />
      )}
    </ReportFrame>
  );
}
