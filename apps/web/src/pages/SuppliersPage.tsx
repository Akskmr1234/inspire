import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { MasterField, MasterFrame, RowAction } from '@/components/MasterFrame';
import type { GridColumn } from '@/components/DataGrid';
import {
  createSupplier,
  listSuppliers,
  setSupplierActive,
  type SupplierSummary,
} from '@/lib/suppliers';

/**
 * The supplier master. A peer of the customer master of §12.1, which is what it is
 * modelled on: both are sub-ledgers, and both are reached from the work rather than
 * from the chart of accounts.
 *
 * A supplier is a sub-ledger rather than a record beside one, which is why this screen
 * lives under purchase and not under the chart of accounts: a purchase is billed by a
 * ledger, a payment settles against one, and the creditors report sums them.
 *
 * Nothing here deletes. A supplier with history is what every past purchase points at, so
 * withdrawing one stops new documents naming them and leaves the trail whole.
 */
export function SuppliersPage(): React.JSX.Element {
  const { t } = useTranslation();

  const columns = (
    run: (action: () => Promise<void>) => void,
    busy: boolean,
  ): readonly GridColumn<SupplierSummary>[] => [
    { key: 'code', header: t('masters.code'), value: (row) => row.code },
    { key: 'name', header: t('masters.name'), value: (row) => row.name },
    {
      key: 'mobile',
      header: t('customers.mobile'),
      value: (row) => row.contact.mobileNumber ?? '',
    },
    {
      key: 'address',
      header: t('customers.address'),
      value: (row) => row.contact.addressLine1 ?? '',
      hiddenByDefault: true,
    },
    {
      key: 'registration',
      header: t('suppliers.registration'),
      // What an input tax reclaim is made against, so it is shown rather than buried
      // behind the column picker.
      value: (row) => row.taxDetails.registrationNumber ?? '',
    },
    {
      key: 'creditDays',
      header: t('customers.creditDays'),
      value: (row) => row.terms.creditDays ?? '',
      numeric: true,
    },
    {
      key: 'creditLimit',
      header: t('customers.creditLimit'),
      value: (row) => row.terms.creditLimit ?? '',
      numeric: true,
      hiddenByDefault: true,
    },
    {
      key: 'state',
      header: t('customers.stateCode'),
      value: (row) => row.taxDetails.stateCode ?? '',
    },
    {
      key: 'status',
      header: t('masters.status'),
      value: (row) => (row.isActive ? t('masters.active') : t('masters.withdrawn')),
    },
    {
      key: 'actions',
      header: '',
      value: () => '',
      render: (row) => (
        <RowAction
          label={row.isActive ? t('masters.withdraw') : t('masters.restore')}
          disabled={busy}
          onClick={() =>
            run(async () => {
              await setSupplierActive(row.supplierId, !row.isActive);
            })
          }
        />
      ),
    },
  ];

  return (
    <MasterFrame<SupplierSummary>
      title={t('nav.suppliers')}
      queryKey="suppliers"
      fetchRows={(includeInactive) => listSuppliers('', !includeInactive)}
      columns={columns}
      rowKey={(row) => row.supplierId}
      addForm={(run, busy) => <AddSupplier run={run} busy={busy} />}
    />
  );
}

/** The fields a supplier is created with. */
function AddSupplier({
  run,
  busy,
}: {
  readonly run: (action: () => Promise<void>) => void;
  readonly busy: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();

  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [mobile, setMobile] = useState('');
  const [address, setAddress] = useState('');
  const [registration, setRegistration] = useState('');
  const [creditDays, setCreditDays] = useState('30');
  const [stateCode, setStateCode] = useState('');

  const add = (): void =>
    run(async () => {
      await createSupplier({
        code: code.trim(),
        name: name.trim(),
        contact: {
          mobileNumber: mobile.trim() || null,
          addressLine1: address.trim() || null,
        },
        terms: {
          creditDays: creditDays.trim() === '' ? null : Number(creditDays),
          isBillWise: true,
        },
        taxDetails: {
          registrationNumber: registration.trim() || null,
          stateCode: stateCode.trim() || null,
        },
      });

      setCode('');
      setName('');
      setMobile('');
      setAddress('');
      setRegistration('');
      setStateCode('');
    });

  return (
    <>
      <MasterField label={t('masters.code')} value={code} onChange={setCode} />
      <MasterField
        label={t('masters.name')}
        value={name}
        onChange={setName}
        width="w-56"
      />
      <MasterField label={t('customers.mobile')} value={mobile} onChange={setMobile} />
      <MasterField
        label={t('customers.address')}
        value={address}
        onChange={setAddress}
        width="w-56"
      />
      <MasterField
        label={t('suppliers.registration')}
        value={registration}
        onChange={setRegistration}
        width="w-36"
      />
      <MasterField
        label={t('customers.creditDays')}
        value={creditDays}
        onChange={setCreditDays}
        width="w-20"
      />
      <MasterField
        label={t('customers.stateCode')}
        value={stateCode}
        onChange={setStateCode}
        width="w-20"
      />

      <button
        type="button"
        disabled={busy || code.trim() === '' || name.trim() === ''}
        onClick={add}
        className="btn-primary btn-sm self-end py-1.5"
      >
        {t('masters.add')}
      </button>
    </>
  );
}
