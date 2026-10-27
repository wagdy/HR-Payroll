// The tabs of HR & Payroll, and Custody & Assets beside them, as the API names them (صلاحيات
// الموظفين): what a manager gives an employee, who works them in the portal, or an accountant, in
// the system. A manager has them all.
export type HrTab = 'Now' | 'Employees' | 'Departments' | 'Attendance' | 'Corrections' | 'Payroll' | 'Advances' | 'Holidays' | 'Devices' | 'Custody';

export interface HrTabInfo {
  tab: HrTab;
  // Its address under /hr.
  path: string;
  en: string;
  ar: string;
  // Its Material icon, beside its name in the sections.
  icon: string;
}

// HR & Payroll's own tabs, in the order they are shown.
export const hrTabs: readonly HrTabInfo[] = [
  { tab: 'Now', path: 'now', en: 'Now', ar: 'الآن', icon: 'how_to_reg' },
  { tab: 'Employees', path: 'employees', en: 'Employees', ar: 'الموظفين', icon: 'groups' },
  { tab: 'Departments', path: 'departments', en: 'Departments', ar: 'الأقسام', icon: 'account_tree' },
  { tab: 'Attendance', path: 'attendance', en: 'Attendance', ar: 'الحضور والانصراف', icon: 'event_available' },
  { tab: 'Corrections', path: 'corrections', en: 'Corrections', ar: 'مراجعة وجزاءات', icon: 'rule' },
  { tab: 'Payroll', path: 'payroll', en: 'Payroll', ar: 'مسير الرواتب', icon: 'payments' },
  { tab: 'Advances', path: 'advances', en: 'Advances', ar: 'السلفيات', icon: 'request_quote' },
  // The leaves and the public holidays (the tab the API calls Holidays).
  { tab: 'Holidays', path: 'leaves', en: 'Leaves & Holidays', ar: 'الإجازات والعطلات', icon: 'beach_access' },
  { tab: 'Devices', path: 'devices', en: 'Attendance devices', ar: 'أجهزة البصمة', icon: 'fingerprint' },
];

// Custody & Assets, a section of its own (/custody), given as a tab is.
export const custodyTab: HrTabInfo = { tab: 'Custody', path: '/custody', en: 'Custody & Assets', ar: 'العهدة والأصول', icon: 'inventory_2' };

// Every tab a manager gives, in the order the Permissions tab shows them.
export const givenTabs: readonly HrTabInfo[] = [...hrTabs, custodyTab];

export const allHrTabs: readonly HrTab[] = givenTabs.map((t) => t.tab);

// The tabs a list names, in the order shown; anything that is not a tab left out.
export function hrTabsOf(names: readonly unknown[] | null | undefined): HrTab[] {
  const named = new Set(names ?? []);
  return allHrTabs.filter((t) => named.has(t));
}
