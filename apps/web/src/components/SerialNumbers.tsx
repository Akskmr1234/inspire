import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { Modal, ModalButton } from '@/components/Modal';
import type { ApiError } from '@/lib/api';
import { fetchProductSerials, SerialStatus, type SerialNumberView } from '@/lib/stock';
import { useSettings } from '@/stores/settings';

/**
 * Whether serial numbers are offered at all.
 *
 * The firm's own setting, off unless switched on: a business that does not track
 * handsets by IMEI was shown a "Requires Serial Number / IMEI" box on every line of
 * every invoice. Switched off, no screen asks for a serial number and the server takes
 * documents without them; switched on, they are offered where a product is tracked by
 * serial number and never demanded.
 */
export function useSerialNumbersEnabled(): boolean {
  return useSettings((state) => state.enableSerialNumbers);
}

/**
 * Which way the goods are moving, which decides where the numbers come from.
 *
 * `out` - a sale, a return to a supplier, an issue: the units are on the shelf, so they
 * are picked from what the warehouse holds. `back` - a customer's return: the units are
 * ones the firm sold, so they are picked from those. `in` - a purchase or a receipt: the
 * units are new, so their numbers are typed or scanned.
 */
export type SerialDirection = 'out' | 'back' | 'in';

/**
 * The serial numbers of one line: a summary, and the button that opens the picker.
 *
 * In place of the block of tags and buttons that used to sit under every serialised
 * line: one row saying how many are named and how many could be, because the numbers
 * are optional - a line may name all, some or none of its units.
 */
export function SerialLineSummary({
  chosen,
  quantity,
  onOpen,
}: {
  readonly chosen: readonly string[];
  readonly quantity: number;
  readonly onOpen: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const most = Number.isFinite(quantity) && quantity > 0 ? Math.floor(quantity) : 0;

  return (
    <div className="flex flex-wrap items-center gap-2 text-xs">
      <span className="font-medium text-ink-muted">
        {t('serials.summary', { chosen: chosen.length, most })}
      </span>

      {chosen.slice(0, 4).map((number) => (
        <span
          key={number}
          className="rounded bg-surface-3 px-1.5 py-0.5 font-mono text-[11px] text-ink"
        >
          {number}
        </span>
      ))}
      {chosen.length > 4 && <span className="text-ink-muted">+{chosen.length - 4}</span>}

      <button type="button" onClick={onOpen} className="btn-secondary btn-xs">
        {chosen.length > 0 ? t('serials.change') : t('serials.choose')}
      </button>
    </div>
  );
}

/**
 * Picks the serial numbers for one line.
 *
 * For goods going out it lists the units the warehouse holds and lets somebody tick the
 * ones in their hand, or scan one to tick it; for goods coming in it takes numbers as
 * they are typed or scanned. Either way the count is free from none up to the line's
 * quantity, and choosing more than the quantity raises the quantity to match, because
 * somebody who has scanned three handsets is selling three.
 */
export function SerialPickerDialog({
  productId,
  productLabel,
  warehouseId,
  direction,
  chosen,
  quantity,
  onDone,
  onClose,
}: {
  readonly productId: string;
  readonly productLabel: string;
  readonly warehouseId: string;
  readonly direction: SerialDirection;
  readonly chosen: readonly string[];
  readonly quantity: number;
  readonly onDone: (serials: readonly string[], quantity: number) => void;
  readonly onClose: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [picked, setPicked] = useState<readonly string[]>(chosen);
  const [typed, setTyped] = useState('');
  const [filter, setFilter] = useState('');
  const [notice, setNotice] = useState<string | null>(null);

  const units = useQuery<readonly SerialNumberView[], ApiError>({
    queryKey: [
      'serial-picker',
      productId,
      direction === 'back' ? '' : warehouseId,
      direction,
    ],
    queryFn: () =>
      direction === 'back'
        ? fetchProductSerials(productId, undefined, true)
        : fetchProductSerials(productId, warehouseId),
    enabled: direction !== 'in' && productId !== '',
  });

  // What may be picked: on the shelf for goods going out, sold for a customer return.
  const offered = useMemo(
    () =>
      (units.data ?? []).filter((unit) =>
        direction === 'back'
          ? unit.status === SerialStatus.issued
          : unit.status === SerialStatus.inStock ||
            unit.status === SerialStatus.returnedFromCustomer,
      ),
    [units.data, direction],
  );

  const shown = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    return needle
      ? offered.filter((unit) => unit.number.toLowerCase().includes(needle))
      : offered;
  }, [offered, filter]);

  const toggle = (number: string): void =>
    setPicked((current) =>
      current.includes(number)
        ? current.filter((n) => n !== number)
        : [...current, number],
    );

  // A scan or a typed number. Going out, it has to be a unit the shelf holds - a
  // number that is not there is a mistyped number, and saying so now is kinder than
  // the server saying it on save. Coming in, any new number is taken.
  const enter = (): void => {
    const number = typed.trim();

    if (number === '') {
      return;
    }

    setTyped('');
    setNotice(null);

    if (picked.some((n) => n.toLowerCase() === number.toLowerCase())) {
      setNotice(t('serials.alreadyChosen', { number }));
      return;
    }

    if (direction === 'in') {
      setPicked((current) => [...current, number]);
      return;
    }

    const unit = offered.find(
      (candidate) => candidate.number.toLowerCase() === number.toLowerCase(),
    );

    if (unit) {
      setPicked((current) => [...current, unit.number]);
    } else {
      setNotice(
        t(direction === 'back' ? 'serials.notSold' : 'serials.notOnShelf', { number }),
      );
    }
  };

  const done = (): void => {
    const wanted = Number.isFinite(quantity) ? quantity : 0;
    onDone(picked, Math.max(wanted, picked.length));
  };

  return (
    <Modal
      title={t('serials.title', { product: productLabel })}
      size="form"
      onClose={onClose}
    >
      <div className="flex items-center gap-2">
        <input
          autoFocus
          value={typed}
          onChange={(event) => setTyped(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault();
              enter();
            }
          }}
          placeholder={t('serials.scanPlaceholder')}
          aria-label={t('serials.scanPlaceholder')}
          className="field-input font-mono"
        />
        <button type="button" onClick={enter} className="btn-secondary btn-sm shrink-0">
          {t('serials.add')}
        </button>
      </div>

      {notice && <p className="alert-warn text-xs">{notice}</p>}

      {picked.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {picked.map((number) => (
            <span
              key={number}
              className="inline-flex items-center gap-1 rounded-md border border-brand-500/30 bg-brand-50 px-2 py-0.5 font-mono text-xs text-brand-800 dark:bg-brand-500/15 dark:text-brand-200"
            >
              {number}
              <button
                type="button"
                onClick={() => toggle(number)}
                aria-label={t('serials.remove', { number })}
                className="font-bold hover:text-red-600"
              >
                ×
              </button>
            </span>
          ))}
        </div>
      )}

      {direction !== 'in' && (
        <div className="space-y-2">
          <div className="flex items-center justify-between gap-2">
            <h4 className="text-xs font-semibold text-ink">
              {direction === 'back' ? t('serials.soldUnits') : t('serials.onShelf')}{' '}
              <span className="font-normal text-ink-muted">({offered.length})</span>
            </h4>
            {offered.length > 8 && (
              <input
                value={filter}
                onChange={(event) => setFilter(event.target.value)}
                placeholder={t('serials.filter')}
                aria-label={t('serials.filter')}
                className="field-input-sm w-40"
              />
            )}
          </div>

          {units.isPending && (
            <p className="text-xs text-ink-muted">{t('common.loading')}</p>
          )}

          {!units.isPending && offered.length === 0 && (
            <p className="text-xs text-ink-muted">
              {direction === 'back' ? t('serials.noneSold') : t('serials.noneOnShelf')}
            </p>
          )}

          <ul className="max-h-64 divide-y divide-line overflow-y-auto rounded-lg border border-line">
            {shown.map((unit) => {
              const on = picked.includes(unit.number);

              return (
                <li key={unit.serialNumberId}>
                  <label
                    className={clsx(
                      'flex cursor-pointer items-center gap-3 px-3 py-1.5 text-sm',
                      on && 'bg-brand-50/60 dark:bg-brand-500/10',
                    )}
                  >
                    <input
                      type="checkbox"
                      checked={on}
                      onChange={() => toggle(unit.number)}
                    />
                    <span className="font-mono">{unit.number}</span>
                    <span className="ms-auto text-xs text-ink-muted">
                      {[
                        unit.batchNumber,
                        unit.warehouseName,
                        unit.warrantyUntil &&
                          t('serials.warrantyTo', { date: unit.warrantyUntil }),
                      ]
                        .filter(Boolean)
                        .join(' · ')}
                    </span>
                  </label>
                </li>
              );
            })}
          </ul>
        </div>
      )}

      <div className="flex items-center justify-between gap-2 border-t border-line pt-3">
        <span className="text-xs text-ink-muted">
          {t('serials.pickedOf', {
            chosen: picked.length,
            quantity: Math.max(quantity || 0, picked.length),
          })}
        </span>
        <div className="flex gap-2">
          <ModalButton onClick={onClose}>{t('common.cancel')}</ModalButton>
          <ModalButton primary onClick={done}>
            {t('serials.done')}
          </ModalButton>
        </div>
      </div>
    </Modal>
  );
}
