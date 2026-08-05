import i18next from 'i18next';
import { initReactI18next } from 'react-i18next';

/**
 * English and Arabic, per section 7 of the specification.
 *
 * Resources are inline while the surface is small. They move to separate
 * per-namespace files once there is enough of the UI to warrant lazy loading -
 * splitting a hundred keys across files buys nothing but indirection.
 */
const en = {
  app: { name: 'Inspire ERP' },
  signIn: {
    title: 'Sign in',
    subtitle: 'Enter your credentials to continue',
    company: 'Company code',
    companyHint: 'Leave blank if this installation has a single company',
    userName: 'User name or email',
    password: 'Password',
    submit: 'Sign in',
    working: 'Signing in…',
  },
  nav: {
    dashboard: 'Dashboard',
    accounting: 'Accounting',
    voucherEntry: 'Voucher entry',
    trialBalance: 'Trial balance',
    profitAndLoss: 'Profit and loss',
    balanceSheet: 'Balance sheet',
    signOut: 'Sign out',
  },
  reports: {
    from: 'From',
    to: 'To',
    asAt: 'As at',
    run: 'Run',
    running: 'Running…',
    ledger: 'Ledger',
    group: 'Group',
    openingDebit: 'Opening Dr',
    openingCredit: 'Opening Cr',
    periodDebit: 'Period Dr',
    periodCredit: 'Period Cr',
    closingDebit: 'Closing Dr',
    closingCredit: 'Closing Cr',
    totals: 'Totals',
    balanced: 'Debits equal credits',
    notBalanced: 'OUT OF BALANCE',
    noData: 'No postings in this period.',
    income: 'Income',
    expenses: 'Expenses',
    totalIncome: 'Total income',
    totalExpenses: 'Total expenses',
    netProfit: 'Net profit',
    netLoss: 'Net loss',
    assets: 'Assets',
    liabilities: 'Liabilities',
    equity: 'Equity',
    retainedEarnings: 'Retained earnings',
    totalAssets: 'Total assets',
    totalLiabilities: 'Total liabilities',
    totalEquity: 'Total equity',
    totalLiabilitiesAndEquity: 'Total liabilities and equity',
  },
  common: {
    retry: 'Try again',
    loading: 'Loading…',
    theme: 'Theme',
    language: 'Language',
    mustChangePassword: 'Your password must be changed before you continue.',
  },
} as const;

const ar = {
  app: { name: 'إنسباير إي آر بي' },
  signIn: {
    title: 'تسجيل الدخول',
    subtitle: 'أدخل بياناتك للمتابعة',
    company: 'رمز الشركة',
    companyHint: 'اتركه فارغاً إذا كان النظام يحتوي على شركة واحدة',
    userName: 'اسم المستخدم أو البريد الإلكتروني',
    password: 'كلمة المرور',
    submit: 'تسجيل الدخول',
    working: 'جارٍ تسجيل الدخول…',
  },
  nav: {
    dashboard: 'لوحة المعلومات',
    accounting: 'المحاسبة',
    voucherEntry: 'إدخال قيد',
    trialBalance: 'ميزان المراجعة',
    profitAndLoss: 'الأرباح والخسائر',
    balanceSheet: 'الميزانية العمومية',
    signOut: 'تسجيل الخروج',
  },
  reports: {
    from: 'من',
    to: 'إلى',
    asAt: 'كما في',
    run: 'تشغيل',
    running: 'جارٍ التشغيل…',
    ledger: 'الحساب',
    group: 'المجموعة',
    openingDebit: 'مدين افتتاحي',
    openingCredit: 'دائن افتتاحي',
    periodDebit: 'مدين الفترة',
    periodCredit: 'دائن الفترة',
    closingDebit: 'مدين ختامي',
    closingCredit: 'دائن ختامي',
    totals: 'المجاميع',
    balanced: 'المدين يساوي الدائن',
    notBalanced: 'غير متوازن',
    noData: 'لا توجد قيود في هذه الفترة.',
    income: 'الإيرادات',
    expenses: 'المصروفات',
    totalIncome: 'إجمالي الإيرادات',
    totalExpenses: 'إجمالي المصروفات',
    netProfit: 'صافي الربح',
    netLoss: 'صافي الخسارة',
    assets: 'الأصول',
    liabilities: 'الالتزامات',
    equity: 'حقوق الملكية',
    retainedEarnings: 'الأرباح المحتجزة',
    totalAssets: 'إجمالي الأصول',
    totalLiabilities: 'إجمالي الالتزامات',
    totalEquity: 'إجمالي حقوق الملكية',
    totalLiabilitiesAndEquity: 'إجمالي الالتزامات وحقوق الملكية',
  },
  common: {
    retry: 'أعد المحاولة',
    loading: 'جارٍ التحميل…',
    theme: 'المظهر',
    language: 'اللغة',
    mustChangePassword: 'يجب تغيير كلمة المرور قبل المتابعة.',
  },
} as const;

await i18next.use(initReactI18next).init({
  resources: {
    en: { translation: en },
    ar: { translation: ar },
  },
  lng: localStorage.getItem('erp.language') === 'ar' ? 'ar' : 'en',
  fallbackLng: 'en',
  interpolation: {
    // React escapes for us; letting i18next escape as well double-encodes any
    // apostrophe in a ledger name.
    escapeValue: false,
  },
});

export default i18next;
