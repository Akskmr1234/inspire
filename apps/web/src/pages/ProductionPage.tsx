import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { DataGrid, GridAction, type GridColumn } from '@/components/DataGrid';
import { Modal } from '@/components/Modal';
import { ReportFrame } from '@/components/ReportFrame';
import { SearchSelect } from '@/components/SearchSelect';
import { BatchPick } from '@/components/BatchPick';
import { productOption, stockByProduct } from '@/components/DocumentLines';
import type { ApiError } from '@/lib/api';
import {
  createBom,
  getBom,
  listBoms,
  setBomActive,
  updateBom,
  type BomDetail,
  type BomInput,
  type BomSummary,
} from '@/lib/boms';
import { moneyAlways } from '@/lib/money';
import { listProducts, type ProductSummary } from '@/lib/products';
import {
  fetchStockValuation,
  StockDocumentType,
  type StockLineInput,
  type StockValuationReport,
} from '@/lib/stock';
import { useSession } from '@/stores/session';
import {
  Alert,
  StockOperationsPage,
  trim,
  type StockEntryProps,
} from '@/pages/StockOperationsPage';

/** Every stocked product of the firm, for the pickers. */
function useStockedProducts(): readonly ProductSummary[] {
  const products = useQuery<readonly ProductSummary[], ApiError>({
    queryKey: ['products', '', '', false],
    queryFn: () => listProducts('', '', false),
  });

  return useMemo(
    () => (products.data ?? []).filter((product) => product.itemType === 1),
    [products.data],
  );
}

// ------------------------------------------------------------------------- bills of materials

/**
 * Bills of materials: what goes into each finished good.
 *
 * One per product, with an estimate of what making it costs at today's average stock
 * cost, so somebody pricing a finished good can see what it is made of and what that
 * comes to before anything is made.
 */
export function BomsPage(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const can = useSession((state) => state.can);

  const [showInactive, setShowInactive] = useState(false);
  const [editing, setEditing] = useState<BomDetail | 'new' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const query = useQuery<readonly BomSummary[], ApiError>({
    queryKey: ['boms', showInactive],
    queryFn: () => listBoms(showInactive),
  });

  const mutation = useMutation<string, ApiError, () => Promise<string>>({
    mutationFn: (action) => action(),
    onSuccess: async (message) => {
      setError(null);
      setEditing(null);
      setNotice(message);
      window.setTimeout(() => setNotice(null), 4000);
      await queryClient.invalidateQueries({ queryKey: ['boms'] });
      await queryClient.invalidateQueries({ queryKey: ['bom'] });
    },
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const open = async (id: string): Promise<void> => {
    setError(null);
    setEditing(await getBom(id));
  };

  const columns: readonly GridColumn<BomSummary>[] = [
    {
      key: 'code',
      header: t('boms.product'),
      value: (row) => `${row.productCode} ${row.productName}`,
      render: (row) => (
        <button type="button" onClick={() => void open(row.id)} className="cell-link">
          <span className="font-mono text-xs">{row.productCode}</span> {row.productName}
        </button>
      ),
    },
    {
      key: 'makes',
      header: t('boms.makes'),
      value: (row) => row.outputQuantity,
      numeric: true,
      render: (row) => `${trim(row.outputQuantity)} ${row.stockUnitCode}`,
    },
    {
      key: 'components',
      header: t('boms.components'),
      value: (row) => row.componentCount,
      numeric: true,
    },
    {
      key: 'cost',
      header: t('boms.estimatedCost'),
      value: (row) => row.estimatedCost,
      numeric: true,
      render: (row) => moneyAlways(row.estimatedCost),
      requiredPermission: 'inventory:report:view',
    },
    {
      key: 'active',
      header: t('boms.status'),
      value: (row) => (row.isActive ? t('boms.active') : t('boms.withdrawn')),
    },
  ];

  const controls = (
    <div className="toolbar">
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={showInactive}
          onChange={(event) => setShowInactive(event.target.checked)}
        />
        {t('boms.showWithdrawn')}
      </label>
    </div>
  );

  return (
    <>
      <ReportFrame title={t('nav.boms')} controls={controls} query={query}>
        {(rows) => (
          <div className="space-y-3">
            {error && !editing && <Alert tone="error">{error}</Alert>}
            {notice && <Alert tone="ok">{notice}</Alert>}
            <DataGrid
              gridKey="boms"
              rows={rows}
              columns={columns}
              rowKey={(row) => row.id}
              emptyMessage={t('boms.none')}
              actions={
                can('inventory:product:create') ? (
                  <GridAction label={t('boms.new')} onClick={() => setEditing('new')} />
                ) : undefined
              }
            />
          </div>
        )}
      </ReportFrame>

      {editing !== null && (
        <Modal
          title={editing === 'new' ? t('boms.new') : t('boms.edit')}
          size="full"
          onClose={() => setEditing(null)}
        >
          {error && <Alert tone="error">{error}</Alert>}
          <BomForm
            initial={editing === 'new' ? null : editing}
            busy={mutation.isPending}
            onCancel={() => setEditing(null)}
            onToggle={
              editing === 'new' || !can('inventory:product:edit')
                ? null
                : () =>
                    mutation.mutate(async () => {
                      await setBomActive(editing.summary.id, !editing.summary.isActive);
                      return editing.summary.isActive
                        ? t('boms.withdrawnNotice')
                        : t('boms.restoredNotice');
                    })
            }
            onSubmit={(input) =>
              mutation.mutate(async () => {
                if (editing === 'new') {
                  await createBom(input);
                } else {
                  await updateBom(editing.summary.id, input);
                }
                return t('boms.savedNotice');
              })
            }
          />
        </Modal>
      )}
    </>
  );
}

interface ComponentDraft {
  readonly key: string;
  productId: string;
  quantity: string;
  remarks: string;
}

function blankComponent(): ComponentDraft {
  return { key: crypto.randomUUID(), productId: '', quantity: '', remarks: '' };
}

function BomForm({
  initial,
  busy,
  onCancel,
  onToggle,
  onSubmit,
}: {
  readonly initial: BomDetail | null;
  readonly busy: boolean;
  readonly onCancel: () => void;
  readonly onToggle: (() => void) | null;
  readonly onSubmit: (input: BomInput) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const products = useStockedProducts();

  const [productId, setProductId] = useState(initial?.summary.productId ?? '');
  const [output, setOutput] = useState(String(initial?.summary.outputQuantity ?? 1));
  const [narration, setNarration] = useState(initial?.narration ?? '');
  const [lines, setLines] = useState<readonly ComponentDraft[]>(() =>
    initial
      ? initial.components.map((component) => ({
          key: crypto.randomUUID(),
          productId: component.productId,
          quantity: String(component.quantity),
          remarks: component.remarks ?? '',
        }))
      : [blankComponent()],
  );
  const [problem, setProblem] = useState<string | null>(null);

  const costs = new Map(
    (initial?.components ?? []).map((component) => [
      component.productId,
      component.unitCost,
    ]),
  );
  const options = products.map((product) => productOption(product, undefined));

  const update = (key: string, patch: Partial<ComponentDraft>): void =>
    setLines((current) =>
      current.map((line) => (line.key === key ? { ...line, ...patch } : line)),
    );

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();

        const components = lines
          .filter((line) => line.productId && Number(line.quantity) > 0)
          .map((line) => ({
            productId: line.productId,
            quantity: Number(line.quantity),
            remarks: line.remarks.trim() || null,
          }));

        if (!productId || !(Number(output) > 0) || components.length === 0) {
          setProblem(t('boms.incomplete'));
          return;
        }

        setProblem(null);
        onSubmit({
          productId,
          outputQuantity: Number(output),
          narration: narration.trim() || null,
          components,
        });
      }}
    >
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <label className="block lg:col-span-2">
          <span className="field-label">{t('boms.finishedGood')}</span>
          {initial ? (
            <input
              value={`${initial.summary.productCode} — ${initial.summary.productName}`}
              disabled
              className="field-input"
            />
          ) : (
            <SearchSelect
              value={productId}
              onChange={setProductId}
              label={t('boms.finishedGood')}
              placeholder={t('stock.choose')}
              options={options}
            />
          )}
        </label>
        <label className="block">
          <span className="field-label">{t('boms.makesQuantity')}</span>
          <input
            type="number"
            step="any"
            min="0"
            value={output}
            onChange={(event) => setOutput(event.target.value)}
            className="field-input text-end font-mono tabular-nums"
          />
        </label>
        <label className="block">
          <span className="field-label">{t('stock.narration')}</span>
          <input
            value={narration}
            onChange={(event) => setNarration(event.target.value)}
            className="field-input"
          />
        </label>
      </div>

      <div className="overflow-x-auto sm:rounded-xl sm:border sm:border-line sm:bg-surface sm:shadow-card">
        <table className="line-table">
          <thead className="bg-surface-3 text-xs tracking-wide text-ink-muted uppercase">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">
                {t('boms.component')}
              </th>
              <th className="px-3 py-2 text-end font-semibold">{t('stock.quantity')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('boms.cost')}</th>
              <th className="px-3 py-2 text-start font-semibold">{t('stock.remarks')}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {lines.map((line) => {
              const unit = products.find((product) => product.id === line.productId);
              const unitCost = costs.get(line.productId) ?? unit?.cost ?? 0;

              return (
                <tr key={line.key} className="border-t border-line">
                  <td data-label={t('boms.component')} className="min-w-64 px-2 py-1">
                    <SearchSelect
                      value={line.productId}
                      onChange={(id) => update(line.key, { productId: id })}
                      size="sm"
                      label={t('boms.component')}
                      placeholder={t('stock.choose')}
                      options={options.filter((option) => option.value !== productId)}
                    />
                  </td>
                  <td data-label={t('stock.quantity')} className="px-2 py-1">
                    <input
                      type="number"
                      step="any"
                      min="0"
                      value={line.quantity}
                      onChange={(event) =>
                        update(line.key, { quantity: event.target.value })
                      }
                      className="field-input-sm text-end font-mono tabular-nums"
                    />
                  </td>
                  <td data-label={t('boms.cost')} className="cell-numeric">
                    {line.productId && Number(line.quantity) > 0
                      ? moneyAlways(Number(line.quantity) * unitCost)
                      : ''}
                  </td>
                  <td data-label={t('stock.remarks')} className="px-2 py-1">
                    <input
                      value={line.remarks}
                      onChange={(event) =>
                        update(line.key, { remarks: event.target.value })
                      }
                      className="field-input-sm"
                    />
                  </td>
                  <td className="px-2 py-1 text-end">
                    <button
                      type="button"
                      disabled={lines.length === 1}
                      onClick={() =>
                        setLines((current) =>
                          current.filter((candidate) => candidate.key !== line.key),
                        )
                      }
                      className="rounded-md border border-line px-2 py-0.5 text-xs text-ink-muted disabled:opacity-40"
                    >
                      {t('stock.removeLine')}
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {problem && <Alert tone="error">{problem}</Alert>}

      <div className="flex flex-wrap items-center gap-3 border-t border-line/60 pt-3">
        <button
          type="button"
          onClick={() => setLines((current) => [...current, blankComponent()])}
          className="btn-secondary"
        >
          {t('stock.addLine')}
        </button>
        {onToggle && initial && (
          <button
            type="button"
            onClick={onToggle}
            disabled={busy}
            className="btn-secondary"
          >
            {initial.summary.isActive ? t('boms.withdraw') : t('boms.restore')}
          </button>
        )}
        <button type="submit" disabled={busy} className="btn-primary ms-auto">
          {t('stock.save')}
        </button>
        <button type="button" onClick={onCancel} className="btn-secondary">
          {t('products.cancel')}
        </button>
      </div>
    </form>
  );
}

// ------------------------------------------------------------------------- production

/**
 * Production: materials used up and finished goods made.
 *
 * The register of runs, entered with a form of two lists - what was made, and what
 * was used to make it. Choosing a finished good with a bill of materials fills the
 * materials in from the bill, scaled to the quantity made; they stay editable, because
 * a run that used a little more or less than the recipe is the run that happened.
 */
export function ProductionPage(): React.JSX.Element {
  const { t } = useTranslation();

  return (
    <StockOperationsPage
      lockedType={StockDocumentType.production}
      title={t('nav.production')}
      renderEntry={(props) => <ProductionEntry {...props} />}
    />
  );
}

interface RunLine {
  readonly key: string;
  productId: string;
  quantity: string;
  batchId: string;
  batchNumber: string;
}

function blankRunLine(): RunLine {
  return {
    key: crypto.randomUUID(),
    productId: '',
    quantity: '',
    batchId: '',
    batchNumber: '',
  };
}

function ProductionEntry({
  warehouses,
  busy,
  onCancel,
  onSubmit,
}: StockEntryProps): React.JSX.Element {
  const { t } = useTranslation();
  const products = useStockedProducts();

  const [date, setDate] = useState(new Date().toISOString().slice(0, 10));
  const [warehouseId, setWarehouseId] = useState(
    warehouses.find((warehouse) => warehouse.isDefault)?.id ?? warehouses[0]?.id ?? '',
  );
  const [narration, setNarration] = useState('');
  const [made, setMade] = useState<readonly RunLine[]>([blankRunLine()]);
  const [used, setUsed] = useState<readonly RunLine[]>([blankRunLine()]);
  const [problem, setProblem] = useState<string | null>(null);
  const [filledFrom, setFilledFrom] = useState<string | null>(null);

  const boms = useQuery<readonly BomSummary[], ApiError>({
    queryKey: ['boms', false],
    queryFn: () => listBoms(false),
  });

  const valuation = useQuery<StockValuationReport, ApiError>({
    queryKey: ['stock-valuation', 'picker', warehouseId],
    queryFn: () => fetchStockValuation(warehouseId, '', true),
    enabled: warehouseId !== '',
    staleTime: 60 * 1000,
  });

  const onHand = useMemo(() => stockByProduct(valuation.data?.rows), [valuation.data]);
  const options = products.map((product) =>
    productOption(product, onHand.get(product.id) ?? 0),
  );
  const batched = (id: string): boolean =>
    products.some((product) => product.id === id && product.tracksBatches);

  // The materials a bill calls for, scaled to what is being made, added up across every
  // finished good on the run that has a bill.
  const fillFromBoms = async (lines: readonly RunLine[]): Promise<void> => {
    const needed = new Map<string, number>();
    const named: string[] = [];

    for (const line of lines) {
      const bill = (boms.data ?? []).find(
        (candidate) => candidate.productId === line.productId,
      );
      const quantity = Number(line.quantity);

      if (!bill || !(quantity > 0)) continue;

      const detail = await getBom(bill.id);
      named.push(bill.productCode);

      for (const component of detail.components) {
        const share = (component.quantity * quantity) / detail.summary.outputQuantity;
        needed.set(component.productId, (needed.get(component.productId) ?? 0) + share);
      }
    }

    if (needed.size === 0) {
      setProblem(t('production.noBom'));
      return;
    }

    setProblem(null);
    setFilledFrom(named.join(', '));
    setUsed(
      [...needed.entries()].map(([productId, quantity]) => ({
        ...blankRunLine(),
        productId,
        quantity: String(Math.round(quantity * 1_000_000) / 1_000_000),
      })),
    );
  };

  const toLines = (lines: readonly RunLine[], sign: 1 | -1): StockLineInput[] =>
    lines
      .filter((line) => line.productId && Number(line.quantity) > 0)
      .map((line) => ({
        productId: line.productId,
        quantity: sign * Number(line.quantity),
        batchId: line.batchId || null,
        batchNumber: line.batchId ? null : line.batchNumber.trim() || null,
      }));

  const hasBom = made.some((line) =>
    (boms.data ?? []).some((bill) => bill.productId === line.productId),
  );

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();

        const outputs = toLines(made, 1);
        const inputs = toLines(used, -1);

        if (!warehouseId || outputs.length === 0 || inputs.length === 0) {
          setProblem(t('production.incomplete'));
          return;
        }

        setProblem(null);
        onSubmit({
          type: StockDocumentType.production,
          date,
          warehouseId,
          narration: narration.trim() || null,
          postImmediately: true,
          lines: [...inputs, ...outputs],
        });
      }}
    >
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <label className="block">
          <span className="field-label">{t('stock.date')}</span>
          <input
            type="date"
            value={date}
            onChange={(event) => setDate(event.target.value)}
            className="field-input"
            required
          />
        </label>
        <label className="block">
          <span className="field-label">{t('stock.warehouse')}</span>
          <SearchSelect
            value={warehouseId}
            onChange={setWarehouseId}
            label={t('stock.warehouse')}
            placeholder={t('stock.choose')}
            options={warehouses.map((warehouse) => ({
              value: warehouse.id,
              label: warehouse.name,
            }))}
          />
        </label>
        <label className="block">
          <span className="field-label">{t('stock.narration')}</span>
          <input
            value={narration}
            onChange={(event) => setNarration(event.target.value)}
            className="field-input"
          />
        </label>
      </div>

      <RunLines
        title={t('production.made')}
        lines={made}
        options={options}
        batched={batched}
        warehouseId={warehouseId}
        outgoing={false}
        onChange={setMade}
      />

      <div className="flex flex-wrap items-center gap-3">
        <button
          type="button"
          disabled={!hasBom}
          onClick={() => void fillFromBoms(made)}
          className="btn-secondary btn-sm"
        >
          {t('production.fillFromBom')}
        </button>
        {filledFrom && (
          <span className="text-xs text-ink-muted">
            {t('production.filledFrom', { products: filledFrom })}
          </span>
        )}
      </div>

      <RunLines
        title={t('production.used')}
        lines={used}
        options={options}
        batched={batched}
        warehouseId={warehouseId}
        outgoing
        onHand={onHand}
        onChange={setUsed}
      />

      {problem && <Alert tone="error">{problem}</Alert>}

      <div className="flex flex-wrap items-center gap-3 border-t border-line/60 pt-3">
        <button type="submit" disabled={busy} className="btn-primary ms-auto">
          {t('production.post')}
        </button>
        <button type="button" onClick={onCancel} className="btn-secondary">
          {t('products.cancel')}
        </button>
      </div>
    </form>
  );
}

function RunLines({
  title,
  lines,
  options,
  batched,
  warehouseId,
  outgoing,
  onHand,
  onChange,
}: {
  readonly title: string;
  readonly lines: readonly RunLine[];
  readonly options: ReturnType<typeof productOption>[];
  readonly batched: (productId: string) => boolean;
  readonly warehouseId: string;
  readonly outgoing: boolean;
  readonly onHand?: ReadonlyMap<string, number>;
  readonly onChange: (lines: readonly RunLine[]) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const anyBatched = lines.some((line) => batched(line.productId));

  const update = (key: string, patch: Partial<RunLine>): void =>
    onChange(lines.map((line) => (line.key === key ? { ...line, ...patch } : line)));

  return (
    <section className="space-y-2">
      <div>
        <h3 className="text-sm font-semibold text-ink">{title}</h3>
      </div>
      <div className="overflow-x-auto sm:rounded-xl sm:border sm:border-line sm:bg-surface sm:shadow-card">
        <table className="line-table">
          <thead className="bg-surface-3 text-xs tracking-wide text-ink-muted uppercase">
            <tr>
              <th className="px-3 py-2 text-start font-semibold">{t('stock.product')}</th>
              <th className="px-3 py-2 text-end font-semibold">{t('stock.quantity')}</th>
              {anyBatched && (
                <th className="px-3 py-2 text-start font-semibold">{t('stock.batch')}</th>
              )}
              <th />
            </tr>
          </thead>
          <tbody>
            {lines.map((line) => {
              const held = onHand?.get(line.productId);
              const short =
                outgoing && held !== undefined && Number(line.quantity) > held;

              return (
                <tr key={line.key} className="border-t border-line">
                  <td data-label={t('stock.product')} className="min-w-64 px-2 py-1">
                    <SearchSelect
                      value={line.productId}
                      onChange={(productId) =>
                        update(line.key, { productId, batchId: '', batchNumber: '' })
                      }
                      size="sm"
                      label={t('stock.product')}
                      placeholder={t('stock.choose')}
                      options={options}
                    />
                  </td>
                  <td data-label={t('stock.quantity')} className="px-2 py-1">
                    <input
                      type="number"
                      step="any"
                      min="0"
                      value={line.quantity}
                      onChange={(event) =>
                        update(line.key, { quantity: event.target.value })
                      }
                      className={clsx(
                        'field-input-sm text-end font-mono tabular-nums',
                        short && 'border-amber-500',
                      )}
                    />
                  </td>
                  {anyBatched && (
                    <td data-label={t('stock.batch')} className="px-2 py-1">
                      {batched(line.productId) ? (
                        outgoing ? (
                          <BatchPick
                            productId={line.productId}
                            warehouseId={warehouseId}
                            value={line.batchId}
                            onChange={(batchId) => update(line.key, { batchId })}
                          />
                        ) : (
                          <input
                            value={line.batchNumber}
                            onChange={(event) =>
                              update(line.key, { batchNumber: event.target.value })
                            }
                            placeholder={t('production.batchNumber')}
                            className="field-input-sm"
                          />
                        )
                      ) : (
                        <span className="text-xs text-ink-subtle">
                          {t('stock.batchNone')}
                        </span>
                      )}
                    </td>
                  )}
                  <td className="px-2 py-1 text-end">
                    <button
                      type="button"
                      disabled={lines.length === 1}
                      onClick={() =>
                        onChange(lines.filter((candidate) => candidate.key !== line.key))
                      }
                      className="rounded-md border border-line px-2 py-0.5 text-xs text-ink-muted disabled:opacity-40"
                    >
                      {t('stock.removeLine')}
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      <button
        type="button"
        onClick={() => onChange([...lines, blankRunLine()])}
        className="btn-secondary btn-sm"
      >
        {t('stock.addLine')}
      </button>
    </section>
  );
}
