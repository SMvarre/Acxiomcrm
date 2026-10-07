# AcxiomCRM

A compact role-based CRM built with **ASP.NET Core 8 MVC**, **EF Core 8 (Code First)** on **SQL Server LocalDB**, **ASP.NET Core Identity**, **Bootstrap 5**, **jQuery Validation (Unobtrusive)** and **Chart.js**.

## Roles

| Role | Access |
|---|---|
| Admin | Everything: all data, Users management, Audit Log |
| Manager | All team CRM data (no Users / Audit pages) |
| SalesExecutive | Only records assigned to / created by them |

## Setup

```bash
# 1. Restore & build
dotnet restore
dotnet build

# 2. Create the database (LocalDB) - migrations are applied on first startup
#    (optional explicit step)
dotnet tool install --global dotnet-ef        # once per machine
dotnet ef database update

# 3. Run
dotnet run
# open https://localhost:7198
```

Roles, demo users and sample data (customers, leads, opportunities, follow-ups)
are seeded automatically on first startup.

## Seeded logins (demo only)

| Email | Password | Role |
|---|---|---|
| admin@acxiom.com | Admin@123 | Admin |
| manager@acxiom.com | Manager@123 | Manager |
| sales1@acxiom.com | Sales@123 | SalesExecutive |
| sales2@acxiom.com | Sales@123 | SalesExecutive |

Self-registration is open and always creates a **SalesExecutive**.

## REST API (cookie-authenticated, same ownership rules)

| Method | Route | Notes |
|---|---|---|
| GET / POST | `/api/customers` | 201 + Location on create; 409 on duplicate email/phone |
| GET / PUT / DELETE | `/api/customers/{id}` | 204 / 400 / 403 / 404 / 409 |
| GET / POST | `/api/leads` | tampered `status` -> 400 |
| GET / PUT / DELETE | `/api/leads/{id}` | 403 when the lead belongs to another executive |
| GET / POST | `/api/opportunities` | amount/probability/date rules enforced -> 400 |
| GET / PUT / DELETE | `/api/opportunities/{id}` | role-scoped |

## Demo checklist

- [ ] Anonymous access blocked - visiting `/` redirects to `/Account/Login`
- [ ] Register a new account -> lands on Dashboard as SalesExecutive
- [ ] Login with `admin@acxiom.com` / `Admin@123` -> Dashboard
- [ ] Invalid email format / non-10-digit phone blocked **client-side** (jQuery Validation)
- [ ] Crafted POST (e.g. Phone = "123") via curl/devtools -> rejected **server-side** (400)
- [ ] Opportunity with Amount 0 / Probability 150 / past close date -> rejected with the exact messages
- [ ] Follow-up with yesterday's date -> "Follow-up date cannot be earlier than today."
- [ ] Login as `sales1@acxiom.com` -> only their customers/leads/opportunities visible;
      opening sales2's record by ID -> 404/403
- [ ] Login as `manager@acxiom.com` -> sees team data, no Users/Audit menu
- [ ] Admin -> Users page (change role, deactivate) and Audit Log page with filters
- [ ] Any create/update/delete/conversion adds a row to Audit Log
- [ ] `curl -k https://localhost:7198/api/customers` (after cookie login) returns JSON
- [ ] Dashboard shows stat cards + Lead Status doughnut + Opportunity Pipeline bar chart
- [ ] 5 wrong passwords -> account locked for 15 minutes
