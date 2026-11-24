# VereinMember

An add-on member management system for the ClubSite sports club website.

## Project Status

**Phase 3 — Operational Readiness & v1.0 Release (active):**
- **Phase 1 (Foundation & Security Hardening):** completed and merged on `main` (fail-closed AES-256-GCM encryption, atomic invitation tokens, audit snapshot redaction on erasure/purge, no REST API surface).
- **Phase 2 & 2.5 (Core Workflows & Refactoring R1–R6):** completed and merged (PRs `#20`–`#27`), **125 / 125** automated tests passing in CI.
- **Phase 3 (in progress):** documentation reconciliation, operations/backup runbook (`docs/OPERATIONS.md`), staging browser acceptance, and production release readiness.

## Structure

```
VereinMember/
├── VereinMember.slnx       # Solution with module and test projects
├── Directory.Build.props   # Shared .NET project configuration
├── .editorconfig          # Shared C# formatting rules
├── .gitattributes         # Line-ending and diff configuration
├── docs/                   # OKR, use cases, architecture, security, operations
│   ├── OKR.md
│   ├── USE_CASES.md
│   ├── ARCHITECTURE.md
│   ├── DSGVO_SECURITY.md
│   ├── INVITE_FLOW.md
│   └── OPERATIONS.md
├── src/
│   └── VereinMember/   # Razor Class Library
│       ├── Areas/Membership/  # Pages, ViewModels (InputModels)
│       ├── Data/              # DbContext, configurations, migrations
│       ├── Models/            # Domain entities
│       ├── Services/          # Business logic interfaces + implementations
│       └── Extensions/        # DI registration helpers
└── tests/
    ├── VereinMember.Tests/    # 125 unit, service, SQL projection & HTTP tests
    └── VereinMember.E2ETests/ # Playwright tests with an independent minimal host
```

## Building

The projects target **.NET 8**. Use **.NET SDK 10 or newer** for the standard
`.slnx` solution build.

```bash
dotnet restore VereinMember.slnx
dotnet build VereinMember.slnx --no-restore --configuration Release

# Install the browser once before running the complete test suite
pwsh tests/VereinMember.E2ETests/bin/Release/net8.0/playwright.ps1 install chromium
dotnet test VereinMember.slnx --no-build --configuration Release --verbosity minimal
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

## ClubSite integration

VereinMember owns `/Membership`, including the **Anmelden**, **Abmelden**,
**Registrieren**, **Mein Profil**, and **Administration** navigation. The landing
page resolves the current user through the membership store, so a CMS login that
shares the authentication cookie is not treated as a member login.

The host site remains responsible for its layout and menu. No membership-specific
menu code or configuration is required when the CMS page is placed in a submenu.

An existing CMS page may keep the old `/mitglieder-bereich` URL and its sitemap
position, but must use a **permanent redirect** to `/Membership`. This avoids two
owners rendering the same public page. Configure that redirect in Piranha Manager
(or through the CMS API); membership links and authorization do not belong in
ClubSite's generic `Page.cshtml`.

## Registration declarations

Self-registration requires a valid IBAN and separate confirmation of the
membership/data declaration and the SEPA direct-debit mandate. The main member's
record stores both consent timestamps and text versions; family members use the
main applicant's payment mandate.

The displayed wording in `Areas/Membership/Pages/Register.cshtml` and the version
constants in `Register.cshtml.cs` must change together. Do not rewrite the meaning
of an existing version silently: add a new version when the legal text changes.
The migration `AddRegistrationConsents` adds the corresponding storage.

## Tech Stack

- .NET 8 + ASP.NET Core
- Entity Framework Core 9 + SQL Server
- Razor Pages (Areas/Membership)
- Piranha Identity (shared auth)
- xUnit + WebApplicationFactory (testing)
- Playwright for .NET (browser integration tests)

The [browser integration tests](tests/VereinMember.E2ETests/README.md) run against
the module's real Razor Pages without a ClubSite or Piranha dependency. They use
an isolated SQLite database and local `.eml` delivery.

## License

MIT — same as ClubSite