import { Me } from '../app/core/api/models';
import { CompanySettings } from '../app/core/settings/company';

// What GET /api/auth/me returns for each role: the API's RolePermissions table.
export const managerMe: Me = {
  id: 'user-manager',
  fullName: 'Omar',
  email: 'omar@example.com',
  role: 'Manager',
  permissions: ['approval.decide', 'hr.manage', 'hr.view', 'settings.manage', 'users.manage'],
  hrTabs: ['Now', 'Employees', 'Departments', 'Attendance', 'Corrections', 'Payroll', 'Advances', 'Holidays', 'Devices', 'Custody'],
};

export const accountantMe: Me = {
  id: 'user-accountant',
  fullName: 'Mona',
  email: 'mona@example.com',
  role: 'Accountant',
  permissions: ['hr.manage', 'hr.view'],
  hrTabs: [],
};

// What GET /api/settings returns: a company in Egypt, its money written "L.E 1,400.00".
export function aCompany(overrides: Partial<CompanySettings> = {}): CompanySettings {
  return {
    name: 'Acme', currencySymbol: 'L.E', currencyPosition: 'Before', country: 'Egypt', timeZoneId: 'Africa/Cairo', workdayStartsAtHour: 4,
    weekends: ['Friday'], hasLogo: false, logoVersion: 0, ...overrides,
  };
}
