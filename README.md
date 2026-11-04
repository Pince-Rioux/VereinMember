# VereinMember

An add-on member management system for the ClubSite sports club website.

## Project Status

**Phase 1 — Foundation:** Core entities, DbContext, CRUD pages (active development)

## Structure

```
Membership/
├── docs/                   # OKR, use cases, architecture, security
│   ├── OKR.md
│   ├── USE_CASES.md
│   ├── ARCHITECTURE.md
│   └── DSGVO_SECURITY.md
├── src/
│   └── VereinMember/   # Razor Class Library
│       ├── Areas/Membership/  # Controllers, Pages, ViewModels
│       ├── Data/              # DbContext, configurations, migrations
│       ├── Models/            # Domain entities
│       ├── Services/          # Business logic interfaces + implementations
│       └── Extensions/        # DI registration helpers
└── tests/
    └── VereinMember.Tests/
```

## Development Lifecycle

1. Development in `Membership/` as standalone RCL
2. ClubSite adds project reference → integration tested in same solution
3. Stable → PR into ClubSite main branch
4. Module can later be extracted as independent service if needed

## Reference Data (Departments and Fees)

`Department` and `MembershipFee` are **reference data without admin UI**. There are no CRUD pages for them by design: they change rarely, and deleting a fee that members already reference would corrupt those records.

Maintain them with SQL against the membership database:

- Seed script: `shared-config/seed/seed-membership.sql` (departments + fee groups)
- Tables: `Membership.Department`, `Membership.MembershipFee`

### Rules to follow

1. **Add a fee, do not rename its meaning.** Members store `MembershipFeeId`; changing the row keeps history consistent, inserting a new row for a new year keeps old members on the old amount.
2. **Never delete a fee that is referenced.** Check first:
   ```sql
   SELECT COUNT(*) FROM Membership.Member WHERE MembershipFeeId = <id>;
   ```
   Retire a fee by leaving it in place; the registration form lists every row.
3. **Registration requires at least one fee per category** (`Adult`, `Youth`, `Family`). A missing category breaks self-registration with "Bitte wählen Sie eine Beitragsgruppe".
4. **Departments are optional** on a member (`DepartmentId` is nullable). Removing a department referenced by members leaves a dangling id, so deactivate by renaming instead of deleting.

After changing reference data, no application restart is required — the forms read the tables on each request.

## Tech Stack

- .NET 8 + ASP.NET Core
- Entity Framework Core 9 + SQL Server
- Razor Pages + REST API
- Piranha Identity (shared auth)
- xUnit + WebApplicationFactory (testing)

## License

MIT — same as ClubSite