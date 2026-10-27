# HR & Payroll

HR, attendance, payroll, and custody of company assets for one company, with an employee
self-service portal. Bilingual (English and Arabic) throughout.

- **Employees**: profiles with their ID card, departments and job roles, monthly or daily pay,
  a nine-hour work schedule or flexible hours, and an Excel import and export.
- **Attendance**: punches pushed by a ZKTeco device (ADMS) or read from it over TCP 4370, worked
  out into days: late, leaving early, away, overtime, absences, forgotten punch-outs, public
  holidays and administrative penalties. Who is in now, live.
- **Payroll**: each month's payslip from base salary to net payable, incentives and allowances,
  advances deducted (an employee's own, or their share of a lump sum), finalized by a manager
  and paid by salary payments. A day's labour cost and each department's.
- **Leaves**: leave types, yearly balances, and requests a manager approves.
- **Performance & disciplinary**: KPIs, evaluations, warnings and penalty deductions.
- **Custody & Assets**: assets by category, check-in and check-out vouchers, and what is in
  whose custody. What breaks or is lost only changes the asset's status.
- **Company Settings**: the company's name and logo, the currency (on every screen, sheet and
  message), the country (its national IDs and public holidays), the time zone and workday, and
  the weekends new employees start with.
- **Roles and approvals**: users sign in with an email and a password, as a Manager or an
  Accountant. An accountant works the tabs a manager gives them, and their changes wait for a
  manager's approval. An employee signs in to the portal with their mobile number and a PIN, and
  can be given HR tabs to work there.

The design, rule by rule, is in [docs/](docs/).

## Stack

- **Backend**: .NET 10, ASP.NET Core Web API, Clean Architecture (`HrPayroll.Domain`,
  `HrPayroll.Application` with MediatR, `HrPayroll.Infrastructure` with EF Core on PostgreSQL,
  `HrPayroll.Api`), SignalR for live attendance.
- **Frontend**: Angular 21 (standalone components, signals, zoneless), Angular Material,
  Vitest.
- **Database**: PostgreSQL 16, schemas `hr` and `identity`.

## Running it locally

You need the .NET 10 SDK, Node 22 with npm, and Docker.

1. Start the development database (PostgreSQL on `127.0.0.1:5435`):

   ```bash
   docker compose -f dev/docker-compose.yml up -d
   ```

2. Start the API on <http://localhost:5090>. It applies the migrations and makes the first
   manager from `Auth:FirstManager` in `appsettings.Development.json`:

   ```bash
   dotnet run --project backend/src/HrPayroll.Api --launch-profile http
   ```

3. Start the app on <http://localhost:4220>, which forwards `/api` and `/hubs` to the API:

   ```bash
   cd frontend && npm ci && npm start
   ```

Sign in with the first manager's email and password, then add the other users on Users.

## Configuration

| Setting | What it is |
| --- | --- |
| `ConnectionStrings:HrPayroll` | The PostgreSQL database |
| `Auth:SigningKey` | The key tokens are signed with, 32 bytes at least: `openssl rand -base64 48` |
| `Auth:TokenLifetimeHours` | How long a sign-in lasts (12 hours) |
| `Auth:FirstManager` | `Email`, `Name` and `Password` of the manager made when there is none |
| `Company:*` | The company's settings for the first start only: `Name`, `TimeZoneId` (`Africa/Cairo`), `WorkdayStartsAtHour` (4: a night past midnight belongs to the day before), `Country` (`Egypt` or `SaudiArabia`), `CurrencySymbol` and `CurrencyPosition` (`Before` or `After`), `Weekends`. After that a manager changes them on Settings |
| `Portal:Port` | A port that answers only the employee portal, for the office Wi-Fi (unset: none) |
| `Attendance:DevicePort` | A port that answers only the attendance device (unset: the device uses the API's port) |
| `Cors:AllowedOrigins` | Browser origins allowed to call the API from elsewhere |

## Tests

```bash
dotnet test --project backend/tests/HrPayroll.Domain.Tests
dotnet test --project backend/tests/HrPayroll.Application.Tests
```

The Infrastructure and Api tests run against PostgreSQL, each test in a database of its own.
Without `HRPAYROLL_TEST_DB` they are skipped, so check `skipped: 0`:

```bash
HRPAYROLL_TEST_DB="Host=localhost;Port=5435;Username=hr;Password=hr-dev" dotnet test --project backend/tests/HrPayroll.Infrastructure.Tests
```

```bash
HRPAYROLL_TEST_DB="Host=localhost;Port=5435;Username=hr;Password=hr-dev" dotnet test --project backend/tests/HrPayroll.Api.Tests
```

```bash
cd frontend && npx ng test --watch=false
```

## Building for production

`npm run build` in `frontend/` writes the app into `backend/src/HrPayroll.Api/wwwroot`, and the
API serves it beside `/api`. Set `Auth:SigningKey`, the connection string and the first manager
from the environment, never in a committed file.
