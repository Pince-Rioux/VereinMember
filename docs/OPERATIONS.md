# VereinMember — Operations & Release Runbook

Phase 3 operational reference for hosting `VereinMember` inside `ClubSite`. Keep all real secrets, connection strings, and environment-specific URLs out of git.

---

## 1. Supported Stack & Ownership

- **Target framework:** `net8.0` (aligned with `axuno/ClubSite`; `.NET 10 LTS` upgrade tracked as `ROADMAP.md` item `1.6` once `ClubSite`/Piranha upgrades).
- **Database:** SQL Server (`Membership` schema inside the membership database; managed via EF Core migrations in `src/VereinMember/Data/Migrations/`).
- **Host integration:** `ClubSite` references `VereinMember`, configures `services.AddClubSiteMembership(configuration)`, and maps `/mitglieder-bereich` as a permanent redirect to `/Membership` in Piranha Manager.

---

## 2. Configuration & Secrets

| Key / Env Var | Owner | Purpose & Fail-Closed Behavior |
|---|---|---|
| `FeatureManagement:EnableMembership` | Host | Enables/disables the module. `[RequireMembershipEnabled]` blocks requests server-side when `false`. |
| `ConnectionStrings:MembershipDb` | Host (secret store) | Runtime SQL Server connection string for `MembershipDbContext`. |
| `MEMBERSHIP_CONNECTION_STRING` | Operator (CLI env) | Required by `DesignTimeDbContextFactory` for `dotnet ef` migrations. Fails if unset. |
| `MembershipEncryption:Key` / `MEMBERSHIP_ENCRYPTION_KEY` | Host (secret store) | Base64-encoded 32-byte (256-bit) AES-256-GCM key for IBAN/BIC/notes encryption. Startup **fails closed** if missing or invalid. |
| `MembershipMail` | Host | Mail transport (`Mode`, `Smtp:Server`, etc.). Development captures `.eml` files; production requires valid SMTP settings (fail-closed if `Mode=smtp` without `Smtp:Server`). |
| `MembershipPurge` | Host | Retention/purge settings (`Enabled`, `DryRun`, retention window). Keep `Enabled=false` or `DryRun=true` until club retention sign-off (`ROADMAP.md` 3.2). |
| `ClubMembership:PublicBaseUrl` | Host | Trusted HTTPS base URL used for invitation/activation links. |

**Rule:** Never commit keys, connection strings, or `.eml` / database dumps to git.

---

## 3. Roles & Access Provisioning

- `VereinMember` uses its own ASP.NET Core Identity (`MembershipUser`) in the `Membership` schema, separate from Piranha CMS admin logins.
- Role provisioning (`Admin`, `Editor`, `Viewer`, `Member`) is managed in the Membership Identity tables; staff accounts must follow least privilege (`Admin` for erasure/role-sensitive actions, `Editor`/`Viewer` for daily administration).
- Deactivated or erased members lose sign-in access.

---

## 4. Reference Data (`Department` & `MembershipFee`)

- No admin CRUD UI exists by design (`README.md`).
- Maintain via SQL (`Membership.Department`, `Membership.MembershipFee`).
- **Never delete** a fee or department referenced by `Membership.Member`; add new fee rows or rename departments to retire them without breaking historical records.
- Ensure at least one `MembershipFee` exists per category (`Adult`, `Youth`, `Family`) so self-registration works.

---

## 5. Migrations & Deployment Checklist

1. **Before deploying:**
   - Take a verified SQL backup of the membership database.
   - Verify `MembershipEncryption:Key` and ASP.NET Core DataProtection keys are backed up in the host secret store.
2. **Apply EF Core migrations** (staging first, then production with explicit approval):
   ```bash
   export MEMBERSHIP_CONNECTION_STRING="<connection-string>"
   dotnet ef database update --project src/VereinMember/VereinMember.csproj
   ```
3. **Post-deploy smoke check:**
   - Confirm `GET /Membership` returns `200`.
   - Confirm `/mitglieder-bereich` redirects to `/Membership`.
   - Confirm mail delivery / SMTP connectivity without logging activation tokens.

---

## 6. Backup, Recovery & Rollback

- **What must be backed up together:**
  1. SQL Server membership database (`Membership` schema).
  2. `MembershipEncryption:Key` (without this key, encrypted `BankIban`, `BankBic`, and `Notes` fields cannot be decrypted).
  3. ASP.NET Core DataProtection key ring (cookie/antiforgery continuity).
- **Restore rehearsal (Phase 3.3 gate):**
  - Restore a synthetic backup onto an isolated SQL instance with the backed-up `MembershipEncryption:Key`.
  - Verify member counts, `AuditLog` entries, and decryption of synthetic IBAN/BIC fields.
- **Emergency rollback:**
  - Set `FeatureManagement:EnableMembership=false` to immediately disable `/Membership` endpoints server-side, or roll back the `ClubSite` release artifact and restore the pre-migration DB snapshot if a schema change must be reverted.
