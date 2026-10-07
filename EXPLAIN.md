# EXPLAIN.md - AcxiomCRM Architecture Walkthrough

## 1. Architecture

```
Browser / curl
   |
   v
[Middleware pipeline]  HTTPS redirect -> static files -> session
   |                    -> Authentication -> Authorization
   v
[Controllers]  MVC: Account, Home(Dashboard), Customers, Leads,
   |                 Opportunities, FollowUps, AuditLogs(Admin), Users(Admin)
   |           API: /api/customers, /api/leads, /api/opportunities
   v
[Services]     CustomerService, LeadService (conversion), OpportunityService,
   |            FollowUpService, DashboardService, UserService, AuditService
   v
[AppDbContext] EF Core 8 (Identity + CRM entities) -> SQL Server LocalDB
```

- **ViewModels** shape what the browser submits/sees; **DTOs** shape the JSON API.
- **Services** own ALL business rules and ownership checks - both MVC and API
  controllers call the same services, so rules cannot be bypassed via the API.
- **AutoValidateAntiforgeryTokenAttribute** is registered globally in
  `Program.cs`, so every POST (MVC and API form posts) requires an
  antiforgery token.

## 2. Request flow (example: edit another executive's customer)

1. `POST /Customers/Edit/7` arrives with the auth cookie.
2. Authentication middleware restores the `ClaimsPrincipal` (user id + roles).
3. `[Authorize]` passes (user is logged in).
4. Controller validates ModelState -> calls `CustomerService.UpdateAsync`.
5. Service loads customer #7, sees `CreatedBy != currentUserId` and the user
   is not Admin/Manager -> throws `ForbiddenException`.
6. `CrmBaseController.HandleError` maps it to **HTTP 403**.
7. `AuditService` wrote the attempt context earlier only for state-changing
   successes; unauthorized attempts never reach the DB at all.

For a record that does not exist (or belongs to someone else on a GET) the
service returns null / `NotFoundException` -> **404**, which avoids leaking
which IDs exist.

## 3. Ownership model

- Every entity carries `CreatedBy` (customers) or `AssignedTo`
  (leads/opportunities/follow-ups) = the owner's Identity user id.
- `CurrentUser` (scoped service) exposes `SeesAllData` = Admin || Manager.
- Each service has one `Scoped(user)` query filter used for **reads and
  writes alike** - a SalesExecutive literally cannot query someone else's rows.
- Mutations additionally re-check ownership after loading the entity (the
  query-filtered fetch + explicit check is defense in depth against race edits).

## 4. Validation - why both client and server?

Client-side (jQuery Validation + DataAnnotations) is UX only: it can be
bypassed by turning off JavaScript or posting raw requests with curl/Postman.
Therefore every rule is repeated server-side:

- MVC: `ModelState.IsValid` re-runs the same attributes + `IValidatableObject`.
- Services: re-check Amount > 0, Probability 0-100, close-date not in past,
  follow-up date >= today, lead status inside the enum set - throwing
  `ArgumentException` -> 400 for crafted requests.
- API: `[ApiController]` auto-returns 400 `ValidationProblemDetails` from DTO
  attributes; service-level checks are the second gate.
- Database: unique indexes on Customer.Email/Phone back the duplicate check
  against race conditions (-> 409).

Error responses never include stack traces or DB details - the exception
handler shows a generic error page/logs server-side.

## 5. Viva Q&A

**How does lockout work?**
`PasswordSignInAsync(..., lockoutOnFailure: true)` increments
`AccessFailedCount` on the AspNetUsers row. At 5 failures (`Lockout.MaxFailedAccessAttempts`)
Identity sets `LockoutEnd = now + 15 min` (`DefaultLockoutTimeSpan`). While
`LockoutEnd > now` every sign-in attempt fails immediately - even with the
correct password. A successful login resets the counter. Deactivating a user
(Users page) simply sets `LockoutEnd` to year 2100.

**Why is server-side validation needed?**
Because the server cannot trust the client. Anything coming over HTTP can be
forged: hidden fields, disabled JavaScript, curl. Client validation only saves
a round-trip; the server is the enforcement point (see section 4).

**How is ownership enforced?**
Two layers: (1) query scoping - every list/get/search filters by
`CreatedBy/AssignedTo == current user id` unless the user is Admin/Manager;
(2) explicit post-load checks in every mutating service method that throw
`ForbiddenException` -> 403. `[Authorize(Roles=...)]` only gates pages by role;
record-level ownership must live in the data layer.

**How are passwords hashed?**
`UserManager.CreateAsync` calls `IPasswordHasher<IdentityUser>` (default
PBKDF2, HMAC-SHA256, 100k iterations, 128-bit salt, stored as
`password_hash|salt|version` in AspNetUsers). Verification re-computes the
hash with the stored salt in constant time. Plaintext never touches the
database or logs, and the Login page compares via `PasswordSignInAsync` - we
never read hashes ourselves.

**Why a transaction in lead conversion?**
Conversion writes THREE things: new Customer, new Opportunity, and the lead's
status change to `Converted`. Without a transaction a crash between steps
would leave inconsistent data (e.g. a "Converted" lead with no customer).
`Database.BeginTransactionAsync()` wraps all three; any failure rolls
everything back - all-or-nothing atomicity.

**Why enum properties instead of strings for statuses?**
Model binding parses the posted value into the enum; anything outside the
valid set fails -> ModelState invalid -> rejected. The DB column is an int,
so even direct SQL cannot store "Hacked".

**Why soft delete for customers?**
Opportunities and follow-ups reference customers by FK; deactivating
(`Status = Inactive`) preserves history and referential integrity.
