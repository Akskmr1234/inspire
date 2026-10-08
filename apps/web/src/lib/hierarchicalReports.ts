import {
  ACCOUNT_HEAD_META,
  type AccountHeadMeta,
  type LedgerSummary,
} from '@/lib/ledgers';
import type {
  ReportHead,
  ReportGroup,
  ReportLedger,
  ReportTransaction,
} from '@/components/HierarchicalAccountsTable';

export function getHeadMeta(nature: number | undefined): AccountHeadMeta {
  if (nature && ACCOUNT_HEAD_META[nature]) {
    return ACCOUNT_HEAD_META[nature];
  }
  return ACCOUNT_HEAD_META[1]!; // fallback to Asset
}

/**
 * Builds standard 4-level hierarchy for Trial Balance.
 * Head (1..5) → Group → Ledger → Transactions/Details
 */
export function buildTrialBalanceHierarchy(
  rows: readonly {
    readonly ledgerId: string;
    readonly ledgerCode: string;
    readonly ledgerName: string;
    readonly groupCode: string;
    readonly groupName: string;
    readonly nature?: number | undefined;
    readonly openingDebit: number;
    readonly openingCredit: number;
    readonly periodDebit: number;
    readonly periodCredit: number;
    readonly closingDebit: number;
    readonly closingCredit: number;
  }[],
  ledgersMap?: ReadonlyMap<string, LedgerSummary>,
): readonly ReportHead[] {
  // Map rows by Nature (Head 1..5)
  const byHead = new Map<
    number,
    Map<
      string,
      {
        code: string;
        name: string;
        ledgers: ReportLedger[];
      }
    >
  >();

  // Ensure standard ordered heads exist
  [1, 2, 3, 4, 5].forEach((n) => byHead.set(n, new Map()));

  rows.forEach((row) => {
    const ledgerInfo = ledgersMap?.get(row.ledgerId) ?? ledgersMap?.get(row.ledgerCode);
    const nature = row.nature ?? ledgerInfo?.nature ?? inferNatureFromGroup(row.groupCode, row.groupName);

    let headGroups = byHead.get(nature);
    if (!headGroups) {
      headGroups = new Map();
      byHead.set(nature, headGroups);
    }

    const groupKey = row.groupCode || 'OTHER';
    let group = headGroups.get(groupKey);
    if (!group) {
      group = {
        code: row.groupCode,
        name: row.groupName || row.groupCode,
        ledgers: [],
      };
      headGroups.set(groupKey, group);
    }

    group.ledgers.push({
      id: row.ledgerId,
      code: row.ledgerCode,
      name: row.ledgerName,
      figures: {
        openingDebit: row.openingDebit,
        openingCredit: row.openingCredit,
        periodDebit: row.periodDebit,
        periodCredit: row.periodCredit,
        closingDebit: row.closingDebit,
        closingCredit: row.closingCredit,
      },
      openingBalance: row.openingDebit - row.openingCredit,
      closingBalance: row.closingDebit - row.closingCredit,
    });
  });

  const heads: ReportHead[] = [];

  [1, 2, 3, 4, 5].forEach((nature) => {
    const headGroups = byHead.get(nature);
    if (!headGroups || headGroups.size === 0) return;

    const meta = getHeadMeta(nature);
    const groups: ReportGroup[] = [];

    headGroups.forEach((g, gKey) => {
      if (g.ledgers.length === 0) return;

      groups.push({
        id: `g-${nature}-${gKey}`,
        code: g.code,
        name: g.name,
        ledgers: g.ledgers,
      });
    });

    if (groups.length > 0) {
      heads.push({
        id: `head-${nature}`,
        headId: nature,
        name: meta.name,
        nameArabic: meta.nameArabic,
        badgeTone: meta.tone,
        groups,
      });
    }
  });

  return heads;
}

/**
 * Builds standard 4-level hierarchy for Profit & Loss.
 * Head 4 (Income) & Head 5 (Expense) → Group → Ledger → Details
 */
export function buildProfitAndLossHierarchy(
  incomeLines: readonly {
    readonly groupCode: string;
    readonly groupName: string;
    readonly ledgerCode: string;
    readonly ledgerName: string;
    readonly amount: number;
    readonly ledgerId?: string | undefined;
  }[],
  expenseLines: readonly {
    readonly groupCode: string;
    readonly groupName: string;
    readonly ledgerCode: string;
    readonly ledgerName: string;
    readonly amount: number;
    readonly ledgerId?: string | undefined;
  }[],
  ledgersMap?: ReadonlyMap<string, LedgerSummary>,
): readonly ReportHead[] {
  function buildHead(
    nature: 4 | 5,
    lines: typeof incomeLines,
  ): ReportHead | null {
    if (lines.length === 0) return null;

    const meta = getHeadMeta(nature);
    const groupsMap = new Map<string, { code: string; name: string; ledgers: ReportLedger[] }>();

    lines.forEach((line) => {
      const gKey = line.groupCode || 'OTHER';
      let group = groupsMap.get(gKey);
      if (!group) {
        group = {
          code: line.groupCode,
          name: line.groupName || line.groupCode,
          ledgers: [],
        };
        groupsMap.set(gKey, group);
      }

      const ledgerId = line.ledgerId || ledgersMap?.get(line.ledgerCode)?.ledgerId || `pl-${line.ledgerCode}`;

      group.ledgers.push({
        id: ledgerId,
        code: line.ledgerCode,
        name: line.ledgerName,
        figures: {
          amount: line.amount,
        },
      });
    });

    const groups: ReportGroup[] = [];
    groupsMap.forEach((g, gKey) => {
      groups.push({
        id: `pl-${nature}-${gKey}`,
        code: g.code,
        name: g.name,
        ledgers: g.ledgers,
      });
    });

    return {
      id: `pl-head-${nature}`,
      headId: nature,
      name: meta.name,
      nameArabic: meta.nameArabic,
      badgeTone: meta.tone,
      groups,
    };
  }

  const incomeHead = buildHead(4, incomeLines);
  const expenseHead = buildHead(5, expenseLines);

  const heads: ReportHead[] = [];
  if (incomeHead) heads.push(incomeHead);
  if (expenseHead) heads.push(expenseHead);

  return heads;
}

/**
 * Builds standard 4-level hierarchy for Balance Sheet.
 * Head 1 (Assets), Head 2 (Liabilities), Head 3 (Equity + Retained Earnings) → Group → Ledger → Details
 */
export function buildBalanceSheetHierarchy(
  assets: readonly {
    readonly groupCode: string;
    readonly groupName: string;
    readonly ledgerCode: string;
    readonly ledgerName: string;
    readonly amount: number;
    readonly ledgerId?: string | undefined;
  }[],
  liabilities: readonly {
    readonly groupCode: string;
    readonly groupName: string;
    readonly ledgerCode: string;
    readonly ledgerName: string;
    readonly amount: number;
    readonly ledgerId?: string | undefined;
  }[],
  equity: readonly {
    readonly groupCode: string;
    readonly groupName: string;
    readonly ledgerCode: string;
    readonly ledgerName: string;
    readonly amount: number;
    readonly ledgerId?: string | undefined;
  }[],
  retainedEarnings?: number | undefined,
  ledgersMap?: ReadonlyMap<string, LedgerSummary>,
): readonly ReportHead[] {
  function buildSection(
    nature: 1 | 2 | 3,
    lines: typeof assets,
    extraEquityRow?: { label: string; amount: number } | undefined,
  ): ReportHead | null {
    const meta = getHeadMeta(nature);
    const groupsMap = new Map<string, { code: string; name: string; ledgers: ReportLedger[] }>();

    lines.forEach((line) => {
      const gKey = line.groupCode || 'OTHER';
      let group = groupsMap.get(gKey);
      if (!group) {
        group = {
          code: line.groupCode,
          name: line.groupName || line.groupCode,
          ledgers: [],
        };
        groupsMap.set(gKey, group);
      }

      const ledgerId = line.ledgerId || ledgersMap?.get(line.ledgerCode)?.ledgerId || `bs-${line.ledgerCode}`;

      group.ledgers.push({
        id: ledgerId,
        code: line.ledgerCode,
        name: line.ledgerName,
        figures: {
          amount: line.amount,
        },
      });
    });

    if (extraEquityRow) {
      const eqGroupKey = 'RE';
      let eqGroup = groupsMap.get(eqGroupKey);
      if (!eqGroup) {
        eqGroup = {
          code: 'RE',
          name: 'Retained Earnings & Reserves',
          ledgers: [],
        };
        groupsMap.set(eqGroupKey, eqGroup);
      }

      eqGroup.ledgers.push({
        id: 'bs-retained-earnings',
        code: 'RE-NET',
        name: extraEquityRow.label,
        figures: {
          amount: extraEquityRow.amount,
        },
      });
    }

    const groups: ReportGroup[] = [];
    groupsMap.forEach((g, gKey) => {
      groups.push({
        id: `bs-${nature}-${gKey}`,
        code: g.code,
        name: g.name,
        ledgers: g.ledgers,
      });
    });

    if (groups.length === 0) return null;

    return {
      id: `bs-head-${nature}`,
      headId: nature,
      name: meta.name,
      nameArabic: meta.nameArabic,
      badgeTone: meta.tone,
      groups,
    };
  }

  const assetHead = buildSection(1, assets);
  const liabHead = buildSection(2, liabilities);
  const equityHead = buildSection(
    3,
    equity,
    retainedEarnings !== undefined ? { label: 'Retained Earnings (Period Result)', amount: retainedEarnings } : undefined,
  );

  const heads: ReportHead[] = [];
  if (assetHead) heads.push(assetHead);
  if (liabHead) heads.push(liabHead);
  if (equityHead) heads.push(equityHead);

  return heads;
}

/**
 * Builds standard 4-level hierarchy for Day Book.
 * Head → Group → Ledger → Pre-populated Day Book Vouchers/Lines
 */
export function buildDayBookHierarchy(
  entries: readonly {
    readonly voucherId: string;
    readonly date: string;
    readonly voucherNumber: string;
    readonly voucherType: string;
    readonly referenceNumber: string | null;
    readonly narration: string | null;
    readonly amount: number;
    readonly lines: readonly {
      readonly ledgerId: string;
      readonly ledgerCode: string;
      readonly ledgerName: string;
      readonly narration: string | null;
      readonly debit: number;
      readonly credit: number;
    }[];
  }[],
  ledgersMap?: ReadonlyMap<string, LedgerSummary>,
): readonly ReportHead[] {
  // Map ledgerId -> Ledger with its vouchers
  const ledgerMap = new Map<
    string,
    {
      ledgerId: string;
      code: string;
      name: string;
      nature: number;
      groupCode: string;
      groupName: string;
      totalDebit: number;
      totalCredit: number;
      transactions: ReportTransaction[];
    }
  >();

  entries.forEach((voucher) => {
    voucher.lines.forEach((line) => {
      const ledgerInfo = ledgersMap?.get(line.ledgerId) ?? ledgersMap?.get(line.ledgerCode);
      const nature = ledgerInfo?.nature ?? inferNatureFromGroup(ledgerInfo?.groupCode, line.ledgerName);
      const groupCode = ledgerInfo?.groupCode ?? 'GEN';
      const groupName = ledgerInfo?.groupName ?? 'General Accounts';

      let ledger = ledgerMap.get(line.ledgerId);
      if (!ledger) {
        ledger = {
          ledgerId: line.ledgerId,
          code: line.ledgerCode,
          name: line.ledgerName,
          nature,
          groupCode,
          groupName,
          totalDebit: 0,
          totalCredit: 0,
          transactions: [],
        };
        ledgerMap.set(line.ledgerId, ledger);
      }

      ledger.totalDebit += line.debit;
      ledger.totalCredit += line.credit;

      ledger.transactions.push({
        id: `${voucher.voucherId}-${line.ledgerId}-${ledger.transactions.length}`,
        date: voucher.date,
        voucherNumber: voucher.voucherNumber,
        voucherType: voucher.voucherType,
        referenceNumber: voucher.referenceNumber,
        particulars: line.narration || voucher.narration || undefined,
        narration: line.narration || voucher.narration,
        debit: line.debit,
        credit: line.credit,
      });
    });
  });

  // Organize by Head and Group
  const byHead = new Map<number, Map<string, { code: string; name: string; ledgers: ReportLedger[] }>>();
  [1, 2, 3, 4, 5].forEach((n) => byHead.set(n, new Map()));

  ledgerMap.forEach((l) => {
    let headGroups = byHead.get(l.nature);
    if (!headGroups) {
      headGroups = new Map();
      byHead.set(l.nature, headGroups);
    }

    let group = headGroups.get(l.groupCode);
    if (!group) {
      group = {
        code: l.groupCode,
        name: l.groupName,
        ledgers: [],
      };
      headGroups.set(l.groupCode, group);
    }

    group.ledgers.push({
      id: l.ledgerId,
      code: l.code,
      name: l.name,
      figures: {
        debit: l.totalDebit,
        credit: l.totalCredit,
      },
      transactions: l.transactions,
    });
  });

  const heads: ReportHead[] = [];

  [1, 2, 3, 4, 5].forEach((nature) => {
    const headGroups = byHead.get(nature);
    if (!headGroups || headGroups.size === 0) return;

    const meta = getHeadMeta(nature);
    const groups: ReportGroup[] = [];

    headGroups.forEach((g, gKey) => {
      if (g.ledgers.length === 0) return;
      groups.push({
        id: `db-${nature}-${gKey}`,
        code: g.code,
        name: g.name,
        ledgers: g.ledgers,
      });
    });

    if (groups.length > 0) {
      heads.push({
        id: `db-head-${nature}`,
        headId: nature,
        name: meta.name,
        nameArabic: meta.nameArabic,
        badgeTone: meta.tone,
        groups,
      });
    }
  });

  return heads;
}

/**
 * Builds standard 4-level hierarchy for Cash Book & Bank Book.
 * Head 1 (Assets) → Group (Cash & Bank Accounts) → Ledger → Pre-populated lines
 */
export function buildCashBankBookHierarchy(
  accounts: readonly {
    readonly ledgerId: string;
    readonly ledgerCode: string;
    readonly ledgerName: string;
    readonly openingBalance: number;
    readonly closingBalance: number;
    readonly totalReceipts: number;
    readonly totalPayments: number;
    readonly lines: readonly {
      readonly date: string;
      readonly voucherId: string;
      readonly voucherNumber: string;
      readonly referenceNumber: string | null;
      readonly narration: string | null;
      readonly contraLedgerNames: readonly string[];
      readonly debit: number;
      readonly credit: number;
      readonly runningBalance: number;
    }[];
  }[],
  bookType: 'cash-book' | 'bank-book',
): readonly ReportHead[] {
  const meta = getHeadMeta(1); // Asset

  const groupName = bookType === 'cash-book' ? 'Cash in Hand & Till Accounts' : 'Bank Accounts & Facilities';
  const groupCode = bookType === 'cash-book' ? 'CASH' : 'BANK';

  const ledgers: ReportLedger[] = accounts.map((acc) => {
    const transactions: ReportTransaction[] = acc.lines.map((l, idx) => ({
      id: `${l.voucherId}-${idx}`,
      date: l.date,
      voucherNumber: l.voucherNumber,
      referenceNumber: l.referenceNumber,
      particulars: l.contraLedgerNames.join(', '),
      narration: l.narration,
      debit: l.debit,
      credit: l.credit,
      balance: l.runningBalance,
    }));

    return {
      id: acc.ledgerId,
      code: acc.ledgerCode,
      name: acc.ledgerName,
      figures: {
        openingBalance: acc.openingBalance,
        receipts: acc.totalReceipts,
        payments: acc.totalPayments,
        closingBalance: acc.closingBalance,
      },
      openingBalance: acc.openingBalance,
      closingBalance: acc.closingBalance,
      transactions,
    };
  });

  const group: ReportGroup = {
    id: `cbb-${groupCode}`,
    code: groupCode,
    name: groupName,
    ledgers,
  };

  return [
    {
      id: `cbb-head-1`,
      headId: 1,
      name: meta.name,
      nameArabic: meta.nameArabic,
      badgeTone: meta.tone,
      groups: [group],
    },
  ];
}

/**
 * Builds standard 4-level hierarchy for Account Group Summary.
 * Head (from nature) → Group → Ledger → Details
 */
export function buildAccountGroupSummaryHierarchy(
  groups: readonly {
    readonly groupCode: string;
    readonly groupName: string;
    readonly nature: number;
    readonly openingDebit: number;
    readonly openingCredit: number;
    readonly periodDebit: number;
    readonly periodCredit: number;
    readonly closingDebit: number;
    readonly closingCredit: number;
    readonly ledgerCount: number;
    readonly ledgers: readonly {
      readonly ledgerId: string;
      readonly ledgerCode: string;
      readonly ledgerName: string;
      readonly openingDebit: number;
      readonly openingCredit: number;
      readonly periodDebit: number;
      readonly periodCredit: number;
      readonly closingDebit: number;
      readonly closingCredit: number;
    }[];
  }[],
): readonly ReportHead[] {
  const byHead = new Map<number, ReportGroup[]>();
  [1, 2, 3, 4, 5].forEach((n) => byHead.set(n, []));

  groups.forEach((g) => {
    const ledgers: ReportLedger[] = g.ledgers.map((l) => ({
      id: l.ledgerId,
      code: l.ledgerCode,
      name: l.ledgerName,
      figures: {
        openingDebit: l.openingDebit,
        openingCredit: l.openingCredit,
        periodDebit: l.periodDebit,
        periodCredit: l.periodCredit,
        closingDebit: l.closingDebit,
        closingCredit: l.closingCredit,
      },
      openingBalance: l.openingDebit - l.openingCredit,
      closingBalance: l.closingDebit - l.closingCredit,
    }));

    const reportGroup: ReportGroup = {
      id: `ags-${g.nature}-${g.groupCode}`,
      code: g.groupCode,
      name: g.groupName,
      ledgers,
      subtotals: {
        openingDebit: g.openingDebit,
        openingCredit: g.openingCredit,
        periodDebit: g.periodDebit,
        periodCredit: g.periodCredit,
        closingDebit: g.closingDebit,
        closingCredit: g.closingCredit,
      },
    };

    let headList = byHead.get(g.nature);
    if (!headList) {
      headList = [];
      byHead.set(g.nature, headList);
    }
    headList.push(reportGroup);
  });

  const heads: ReportHead[] = [];

  [1, 2, 3, 4, 5].forEach((nature) => {
    const groupList = byHead.get(nature);
    if (!groupList || groupList.length === 0) return;

    const meta = getHeadMeta(nature);
    heads.push({
      id: `ags-head-${nature}`,
      headId: nature,
      name: meta.name,
      nameArabic: meta.nameArabic,
      badgeTone: meta.tone,
      groups: groupList,
    });
  });

  return heads;
}

/**
 * Builds standard 4-level hierarchy for Chart of Accounts / General Ledger list.
 * Head (1..5) → Group → Ledger → Details
 */
export function buildChartOfAccountsHierarchy(
  ledgers: readonly LedgerSummary[],
  balancesMap?: ReadonlyMap<string, { opening: number; debit: number; credit: number; closing: number }>,
): readonly ReportHead[] {
  const byHead = new Map<number, Map<string, { code: string; name: string; ledgers: ReportLedger[] }>>();
  [1, 2, 3, 4, 5].forEach((n) => byHead.set(n, new Map()));

  ledgers.forEach((l) => {
    let headGroups = byHead.get(l.nature);
    if (!headGroups) {
      headGroups = new Map();
      byHead.set(l.nature, headGroups);
    }

    const gKey = l.groupCode || 'OTHER';
    let group = headGroups.get(gKey);
    if (!group) {
      group = {
        code: l.groupCode,
        name: l.groupName || l.groupCode,
        ledgers: [],
      };
      headGroups.set(gKey, group);
    }

    const bal = balancesMap?.get(l.ledgerId) ?? balancesMap?.get(l.code);

    group.ledgers.push({
      id: l.ledgerId,
      code: l.code,
      name: l.name,
      currency: l.currency,
      figures: {
        opening: bal?.opening ?? 0,
        debit: bal?.debit ?? 0,
        credit: bal?.credit ?? 0,
        closing: bal?.closing ?? 0,
      },
      openingBalance: bal?.opening,
      closingBalance: bal?.closing,
    });
  });

  const heads: ReportHead[] = [];

  [1, 2, 3, 4, 5].forEach((nature) => {
    const headGroups = byHead.get(nature);
    if (!headGroups || headGroups.size === 0) return;

    const meta = getHeadMeta(nature);
    const groups: ReportGroup[] = [];

    headGroups.forEach((g, gKey) => {
      groups.push({
        id: `coa-${nature}-${gKey}`,
        code: g.code,
        name: g.name,
        ledgers: g.ledgers,
      });
    });

    heads.push({
      id: `coa-head-${nature}`,
      headId: nature,
      name: meta.name,
      nameArabic: meta.nameArabic,
      badgeTone: meta.tone,
      groups,
    });
  });

  return heads;
}

/**
 * Builds standard 4-level hierarchy for Cash Flow Statement.
 * Head (Operating / Investing / Financing) → Category Group → Ledger → Details
 */
export function buildCashFlowHierarchy(
  sections: readonly {
    readonly category: number;
    readonly lines: readonly {
      readonly ledgerId: string;
      readonly ledgerCode: string;
      readonly ledgerName: string;
      readonly inflow: number;
      readonly outflow: number;
      readonly net: number;
    }[];
    readonly inflow: number;
    readonly outflow: number;
    readonly net: number;
  }[],
): readonly ReportHead[] {
  const CATEGORY_META: Record<number, { name: string; nameArabic: string; tone: 'asset' | 'income' | 'expense' }> = {
    1: { name: 'Operating Activities', nameArabic: 'الأنشطة التشغيلية', tone: 'asset' },
    2: { name: 'Investing Activities', nameArabic: 'الأنشطة الاستثمارية', tone: 'income' },
    3: { name: 'Financing Activities', nameArabic: 'الأنشطة التمويلية', tone: 'expense' },
  };

  return sections.map((sec) => {
    const meta = CATEGORY_META[sec.category] ?? {
      name: `Category ${sec.category}`,
      nameArabic: `الفئة ${sec.category}`,
      tone: 'asset' as const,
    };

    const ledgers: ReportLedger[] = sec.lines.map((l) => ({
      id: l.ledgerId,
      code: l.ledgerCode,
      name: l.ledgerName,
      figures: {
        inflow: l.inflow,
        outflow: l.outflow,
        net: l.net,
      },
    }));

    const group: ReportGroup = {
      id: `cf-group-${sec.category}`,
      code: `CAT-${sec.category}`,
      name: meta.name,
      ledgers,
      subtotals: {
        inflow: sec.inflow,
        outflow: sec.outflow,
        net: sec.net,
      },
    };

    return {
      id: `cf-head-${sec.category}`,
      headId: sec.category,
      name: meta.name,
      nameArabic: meta.nameArabic,
      badgeTone: meta.tone,
      groups: [group],
      subtotals: {
        inflow: sec.inflow,
        outflow: sec.outflow,
        net: sec.net,
      },
    };
  });
}

function inferNatureFromGroup(code?: string | null, name?: string | null): number {
  const upper = `${code ?? ''} ${name ?? ''}`.toUpperCase();
  if (upper.includes('ASSET') || upper.includes('BANK') || upper.includes('CASH') || upper.includes('DEBTOR')) {
    return 1;
  }
  if (upper.includes('LIAB') || upper.includes('CREDITOR') || upper.includes('PAYABLE')) {
    return 2;
  }
  if (upper.includes('EQUITY') || upper.includes('CAPITAL') || upper.includes('RETAINED')) {
    return 3;
  }
  if (upper.includes('INCOME') || upper.includes('REVENUE') || upper.includes('SALE')) {
    return 4;
  }
  return 5; // default Expense
}
