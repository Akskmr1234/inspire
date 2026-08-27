import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { MasterField, MasterFrame, RowAction } from '@/components/MasterFrame';
import type { GridColumn } from '@/components/DataGrid';
import {
  createCustomer,
  listCustomers,
  setCustomerActive,
  type CustomerSummary,
} from '@/lib/customers';

/**
 * The customer master of section 12.1.
 *
 * A customer is a sub-ledger rather than a record beside one, which is why this screen
 * lives under sales and not under the chart of accounts: an invoice is billed to a
 * ledger, a receipt settles against one, and the debtors report sums them.
 *
 * Nothing here deletes. A customer with history is what every past invoice points at, so
 * withdrawing one stops new documents naming them and leaves the trail whole.
 */
export function CustomersPage(): React.JSX.Element {
  const { t } = useTranslation();

  const columns = (
    run: (action: () => Promise<void>) => void,
    busy: boolean,
  ): readonly GridColumn<CustomerSummary>[] => [
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
    },
    {
      key: 'state',
      header: t('customers.stateCode'),
      // The field that decides IGST against CGST plus SGST on a GST firm, so it is
      // shown rather than buried behind the column picker.
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
              await setCustomerActive(row.customerId, !row.isActive);
            })
          }
        />
      ),
    },
  ];

  return (
    <MasterFrame<CustomerSummary>
      title={t('nav.customers')}
      addTitle={t('masters.newCustomer')}
      queryKey="customers"
      fetchRows={(includeInactive) => listCustomers('', !includeInactive)}
      columns={columns}
      rowKey={(row) => row.customerId}
      addForm={(run, busy) => <AddCustomer run={run} busy={busy} />}
    />
  );
}

/** The fields a customer is created with. */
function AddCustomer({
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
  const [creditDays, setCreditDays] = useState('30');
  const [creditLimit, setCreditLimit] = useState('');
  const [stateCode, setStateCode] = useState('');

  const add = (): void =>
    run(async () => {
      await createCustomer({
        code: code.trim(),
        name: name.trim(),
        contact: {
          mobileNumber: mobile.trim() || null,
          addressLine1: address.trim() || null,
        },
        terms: {
          creditDays: creditDays.trim() === '' ? null : Number(creditDays),
          creditLimit: creditLimit.trim() === '' ? null : Number(creditLimit),
          isBillWise: true,
        },
        taxDetails: { stateCode: stateCode.trim() || null },
      });
    });

  return (
    <form
      className="space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        add();
      }}
    >
      <div className="form-grid">
        <MasterField label={t('masters.code')} value={code} onChange={setCode} />
        <MasterField label={t('masters.name')} value={name} onChange={setName} />
        <MasterField label={t('customers.mobile')} value={mobile} onChange={setMobile} />
        <MasterField
          label={t('customers.address')}
          value={address}
          onChange={setAddress}
        />
        <MasterField
          label={t('customers.creditDays')}
          value={creditDays}
          onChange={setCreditDays}
        />
        <MasterField
          label={t('customers.creditLimit')}
          value={creditLimit}
          onChange={setCreditLimit}
          placeholder={t('customers.noLimit')}
        />
        <MasterField
          label={t('customers.stateCode')}
          value={stateCode}
          onChange={setStateCode}
        />
      </div>

      <div className="form-actions">
        <button
          type="submit"
          disabled={busy || code.trim() === '' || name.trim() === ''}
          className="btn-primary"
        >
          {t('masters.add')}
        </button>
      </div>
    </form>
  );
}
