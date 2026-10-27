import { Me } from '../app/core/api/models';

// What GET /api/auth/me returns for each role: the API's RolePermissions table.
export const managerMe: Me = {
  id: 'user-manager',
  fullName: 'Omar',
  email: 'omar@example.com',
  role: 'Manager',
  permissions: ['approval.decide', 'hr.manage', 'hr.view', 'users.manage'],
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
