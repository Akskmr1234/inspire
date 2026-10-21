import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { SearchSelect } from '@/components/SearchSelect';
import type { ApiError } from '@/lib/api';
import { fetchProductBatches, type BatchStockRow } from '@/lib/stock';

function trim(value: number): string {
  return value.toLocaleString(undefined, { maximumFractionDigits: 6 });
}

/** The lots of a product on the shelf, for a line to take one of. */
export function BatchPick({
  productId,
  warehouseId,
  value,
  onChange,
}: {
  readonly productId: string;
  readonly warehouseId: string;
  readonly value: string;
  readonly onChange: (batchId: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const batches = useQuery<readonly BatchStockRow[], ApiError>({
    queryKey: ['product-batches', productId, warehouseId],
    queryFn: () => fetchProductBatches(productId, warehouseId),
    enabled: productId !== '' && warehouseId !== '',
  });

  return (
    <SearchSelect
      value={value}
      onChange={onChange}
      size="sm"
      label={t('stock.batch')}
      placeholder={t('stock.choose')}
      options={(batches.data ?? []).map((batch) => ({
        value: batch.batchId,
        label: batch.batchNumber,
        detail: `${trim(batch.quantity)}${batch.expiresOn ? ` · ${batch.expiresOn}` : ''}`,
      }))}
    />
  );
}
