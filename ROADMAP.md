# VereinMember — three-phase completion roadmap

Review date: **2026-11-16**. Status: **proposed for Yves/axuno approval**, not authorization to implement or deploy. This roadmap supersedes the phase/status claims in `PLAN.md`, `docs/OKR.md` and older feature briefs where they conflict with the evidence below; those historical documents have not been rewritten.

## 1. Recommendation and definition of finished

**Keep the existing Razor Class Library and complete it; do not rewrite it.** The core is substantial, but passing service tests are not evidence of a safe production member-management system.

Proposed v1.0 scope:

- ClubSite consumes a versioned VereinMember package; VereinMail remains the mail abstraction.
- Admin/Editor can create, find, update and deactivate members; Admin manages restricted/destructive actions.
- Adult, Youth and Family registration, invitation, activation, login/logout, password recovery and own-profile updates work end to end under an agreed guardian/family/account model.
- Least-privilege role access, safe bank-data handling, reliable auditing and a verified data-rights/retention process.
- Repeatable package build, real SQL Server and HTTP tests, operational backup/restore, staged acceptance and a documented release/rollback procedure.

Not necessary for v1.0: SEPA payment files, BLSV/DOSB reporting, PDF/vCard/bulk exports, general import, photos, independent service extraction or a generic plugin framework. Retain as a separately prioritized post-v1 backlog. Do not let optional features delay security or reliable core workflows.

Priority definitions:

- **P0 — release blocker:** exposure of sensitive data, account compromise/data-loss risk, unsupported production foundations or inability to trust the release checks. Must be closed before real-data release.
- **P1 — v1 essential:** required agreed user journey, integration, data integrity or operating capability. Must be closed before v1.0.
- **P2 — later enhancement:** not a release prerequisite unless Yves explicitly brings it into scope.

Phases are **gated milestones**, not calendar commitments. Implementation estimates should follow the scope decisions in section 7 and the supported-runtime compatibility check.

## 2. Verified baseline

### Repository and checks

- Repo: `/home/admin/projects/VereinMember`; local `main` and GitHub `main` both at `99e29b0` (2026-11-04).
- Pre-existing uncommitted work: `src/VereinMember/Services/MemberService.cs`, `tests/VereinMember.Tests/Services/MemberServiceTests.cs`; untracked `PRUEFBERICHT.md`. Preserve and review separately; do not discard or silently fold into unrelated changes.
- Local SDK: `/home/admin/.dotnet/dotnet`, **8.0.422**. It is not on the current shell's PATH by default.
- Fresh direct library build: **0 warnings, 0 errors**.
- Fresh test run: **63 passed, 0 failed, 0 skipped**.
- The tests use EF Core **InMemory**, including the file called `DbContextIntegrationTests`. They do not establish SQL constraints, migrations, encryption round trips, rowversion behavior or browser/HTTP behavior.
- `dotnet build VereinMember.sln --no-restore` returns success, but the normal-verbosity trace builds **no child projects**. The solution lacks `ProjectConfigurationPlatforms` mappings. This green result must not be used as a release gate.
- Latest queried GitHub build and publish runs on main succeeded on **2026-11-04**. `.github/workflows/build.yml` builds the library only: **it does not run the test project**. CI success therefore does not cover the local fixes or 63-test suite.

Commands used (with `PATH="$HOME/.dotnet:$PATH"`):

```sh
dotnet build src/VereinMember/VereinMember.csproj --no-restore --nologo
dotnet test tests/VereinMember.Tests/VereinMember.Tests.csproj --no-restore --nologo
dotnet build VereinMember.sln --no-restore -v:normal
```

### Already implemented, but not all production-verified

Member/fee/department/family entities; Membership-schema Identity context and migrations; Admin member CRUD pages; member and family API controllers; search/filter/pagination; public registration and activation pages; own-profile page; invitation tokens and expiry; AES-GCM value converters; scheduled purge and manual erasure; JSON data-export endpoint; mail transport selection and package publishing.

Important corrections to older plans:

- Own Membership Identity, REST endpoints, export, erasure and a purge service already exist. Plan to **repair and verify**, not implement them from scratch.
- AES-GCM is implemented; the old claim that bank encryption is only a base64 stub is outdated. Key lifecycle and audit/API exposure remain unsafe.
- Department/Fee CRUD pages claimed in old OKRs do **not** exist. The newer README explicitly treats these as SQL-maintained reference data without CRUD UI. Do not add those screens automatically.
- The current checked-out ClubSite calendar branch has **no VereinMember reference/registration**. Local remote-tracking integration branches exist (`Pince-Rioux/feature/membership-system`, `feature/plugin-module`, `bump/vereinmember-deps`), with differing project/package approaches. They are not proof of current integration or deployment. No branch was switched or merged.

## 3. Phase 1 — remove release blockers and establish a trustworthy baseline

**Outcome:** safe, repeatable development/staging foundation. Use synthetic data only; this phase does not authorize production use.

| ID | Priority | Work package | Acceptance evidence |
|---|---|---|---|
| 1.1 | P0 | Remove the hardcoded SQL credential fallback in `Data/DesignTimeDbContextFactory.cs:16–20`; require explicit protected configuration and use least-privilege migration/runtime accounts. Assess whether the committed development credential was ever usable elsewhere; rotate it through an approved operational change if applicable. Do not reproduce it in tickets/logs. | Missing config fails clearly; secret scan clean for new changes; credential-history assessment recorded without values. No production DB access required for development. |
| 1.2 | P0 | Make encryption fail closed without a valid persistent key. Remove random startup-key fallback (`Extensions/ServiceCollectionExtensions.cs:82–91`); validate 32-byte key, secure storage, recovery and future rotation/versioning. Review static converter state and multi-host/test isolation. | Invalid/missing key rejected at startup; relational DB ciphertext differs from plaintext; same key decrypts after restart/restore; wrong-key/tamper cases fail safely; recovery procedure documented. |
| 1.3 | P0 | Minimize audit data and repair erasure/purge. Whole-member snapshots include decrypted bank data. Manual erasure and scheduled purge copy the data being erased into audit history. Purge is registered unconditionally and runs before its first 24-hour delay. Make retention configurable, explicitly enabled, dry-runnable and governed by approved rules; cover Identity, invitations, consent metadata, family links, historical audits and backup expiry/restore safeguards. | Synthetic end-to-end erasure proves no prohibited residual PII/token survives in those stores. Purge disabled/dry-run is non-destructive. Approved legal-retention exceptions are documented, not guessed. No erasure performed on real data during development. |
| 1.4 | P0 | Remove activation URLs/tokens from logs (`DefaultMemberAccountService.cs:83–85`). Harden invitation redemption: store token hashes, one-time atomic consumption with password/account changes, expiry/resend/replay protection. Replace remove-password-then-add-password sequence with a safe Identity token/reset workflow; enforce full password policy before destructive changes. | Logs contain no link/token; expired/replayed/concurrently redeemed invitations cannot change a password; a rejected new password leaves the old credential valid; failed-attempt writes are awaited. |
| 1.5 | P0 | Stop returning raw EF Member graphs from API endpoints; use explicit role-scoped response/input DTOs. Viewer reads currently return Member objects whose bank fields are not excluded. Centralize validation, field ownership, antiforgery/API auth rules and server-side feature-disable enforcement. Return safe public errors, not inner exception messages. | HTTP role matrix: anonymous/Member/Viewer/Editor/Admin; forbidden bank/internal fields absent; overposting rejected; disabled module does not process mutations or start purge; CSRF protection demonstrated for cookie-authenticated writes. Host-level controls verified rather than assumed. |
| 1.6 | P0 | Move release baseline to a supported .NET/ASP.NET/EF/Identity combination, coordinated with ClubSite/Piranha and VereinMail. Proposed target **.NET 10 LTS**, subject to compatibility verification. Both .NET 8 and .NET 9 are out of support as of 2026-11-10 per Microsoft; current EF/Identity versions are also mixed. | Supported matrix proven in an isolated host before adoption; SDK/runtime/package versions aligned and pinned; direct build/tests and host smoke test green; dependency vulnerability/license review recorded. No production runtime upgrade bundled silently into library work. |
| 1.7 | P0 | Repair solution mappings and CI; add explicit test-project execution, discovered-test count, restore/build/test/pack checks and suitable dependency/secret scans. Test package restore without a sibling checkout or local cache. Review/preserve the existing local audit regression fix before feature work. | Clean checkout CI really builds both projects and runs at least the current 63 baseline tests (or documents deliberate replacements). Broken test deliberately makes CI fail. Packaged artifact can be restored reproducibly. |
| 1.8 | P0/P1 | Make updates non-destructive: load stored member and copy allowlisted input, preserve status/dates/creator/consent unless explicitly changed, audit the actual actor and changed fields. Current `UpdateAsync` attaches a whole posted entity; omitted fields can be overwritten. Apply posted concurrency token, not a freshly fetched one. Enable login lockout/rate limits and ensure deactivated/erased accounts lose access. | HTTP edit preserves omitted metadata; tampered server fields do not change; concurrent edit returns a recoverable conflict; login attempt limits work; deactivation blocks new login and invalidates existing access according to policy. |

Evidence locations beyond those above: `Services/MemberService.cs:202–324,389–427,521–529`; `Services/PurgeService.cs:27–43,68–95`; `Controllers/MembersController.cs:26–85,126–179` under `Areas/Membership`; `Pages/Register.cshtml.cs:163–168,268–273`; `Pages/Login.cshtml.cs:69`; `.github/workflows/build.yml`; `VereinMember.sln`.

**Phase-1 exit gate:** blockers above have tested fixes or disabled functionality with an explicitly limited staging scope; direct and CI checks are trustworthy; no sensitive production migration or cleanup has been run. Full v1 release still requires Phases 2 and 3.

## 4. Phase 2 — complete core workflows and prove ClubSite integration

**Outcome:** feature-complete v1 release candidate in isolated staging.

| ID | Priority | Work package | Acceptance evidence |
|---|---|---|---|
| 2.1 | P1 | Adult registration → invitation → activation → login → profile → logout; password-forgot/reset and safe resend/recovery. Define admission/activation semantics and avoid treating a sent email as delivered. | HTTP tests for valid/invalid/duplicate input, expiry, password policy and mail failures; browser acceptance; no manual DB repair required. |
| 2.2 | P1 | Complete Youth/guardian flow. Public guardian-name/email fields are declared but not converted to a valid GuardianMemberId; service requires one. Resolve non-member guardians and guardian responsibility independently of fee discount. Never expose a public directory of existing adults. | Adult/member and non-member guardian scenarios, denied forged links, reassignment, departure and age transition tested with synthetic records. |
| 2.3 | P1 | Complete Family model and creation/edit/departure journeys. Decide minors' contact/accounts and shared-email support; currently unique member email and invitations per person constrain families. Persist the agreed relationship/join dates (currently collected values are not fully mapped, service overwrites JoinedAt). Validate dates and cross-family duplicates before writes. | Main + children/partner scenarios work; all-or-nothing family writes; failure leaves no orphan group/partial members; fee/guardian/minimum-size invariants survive move/deactivate/erase; business-approved departure path exists for a two-person family. |
| 2.4 | P1 | Repair own-profile POST with a dedicated editable-field model. Form disables/omits email, fee and other fields, while service compares them with default posted values. Clearly distinguish internal admin notes from member-editable content. Stabilize account/member link rather than relying only on mutable email. | Real form changes phone/address successfully; protected fields remain unchanged; member A cannot edit B; validation redisplays entered values; email-change and stale-session cases covered. |
| 2.5 | P1 | Finish Admin usability and data integrity: create/edit/search/filter/pagination, actionable guardian/family errors, correct concurrency feedback, status/date handling and reference-data seeding/retirement instructions. Keep fee/department admin UI out unless scope is changed. | Full role-based CRUD journey, empty/missing seed cases, invalid pagination and concurrent updates tested; German messages; supported reference-data procedure does not corrupt existing memberships. |
| 2.6 | P1 | Recover and consolidate the intended ClubSite package integration onto an up-to-date branch. Prefer the existing compile-time PackageReference pattern over a custom plugin loader. Unify `Modules:VereinMember:Enabled` vs `ClubMembership:Enabled`; verify layout/routing/menu and isolated CMS/member auth cookies, schemes and role/admin provisioning. Do not blindly merge legacy branches or execute the old plan's DROP-table steps. | Module on/off and direct endpoint tests; CMS and member login/logout do not cross-contaminate; install/package upgrade from a clean host; host regression tests for calendar, contact and registration. |
| 2.7 | P1 | Update actual VereinMail dependency from `1.0.0-dev` to the reviewed compatible TLS-hardened release; validate artifact contents and version immutability. Add reliable mail-state/retry handling, preferably using a transactional outbox in the existing SQL store if needed. Bind activation links to a configured trusted HTTPS origin. | `.eml` captured in development; local fake-SMTP integration proves success/failure/TLS-required behavior; retry/resend does not recreate members or leak tokens; production configuration fails clearly rather than silently selecting log-only transport. |
| 2.8 | P1 | Add real SQL Server and HTTP test host suites. Use disposable synthetic database, not the existing membership DB. Verify migrations from empty and supported old schema, unique/FK constraints, encrypted-column sizes/roundtrip, rowversions, transactions, serialization and auth. Add focused coverage reporting. | Tests discover and report real counts; SQL and HTTP jobs green; new regression tests cover every repaired blocker. Suggested coverage target: ≥80% Services, plus all critical security/activation/erasure paths regardless of percentage. |
| 2.9 | P1 | Complete role-correct data-subject export and controlled erasure workflow. Existing JSON export is a partial operational view, not proof of a complete rights response. Check Identity/invitation/consent/audit data and third-party family/guardian data; log export metadata, not payloads. | Authorized request fulfilled with defined contents, appropriate identity checks and third-party redaction; erasure/retention exceptions documented and tested; audit does not retain erased values. |

**Phase-2 exit gate:** all agreed Adult/Youth/Family and staff workflows pass SQL + HTTP tests and staging browser checks; no open P0 or v1 P1; ClubSite integration and package upgrades proven. No live deployment yet.

## 5. Phase 3 — operational readiness, acceptance and controlled v1.0 release

**Outcome:** accepted, supportable production release, not merely a published NuGet package.

| ID | Priority | Work package | Acceptance evidence |
|---|---|---|---|
| 3.1 | P1 | Reconcile README, PLAN, OKRs, architecture and security documentation with actual code. Document setup, supported versions, config ownership, role provisioning, mail, reference data, migration and incident response. Remove hardcoded environment URLs/branding where configuration is appropriate. | One maintained setup/release path succeeds on a clean staging host; old claims marked historical; no credential values in docs. |
| 3.2 | P1 | Agree lawful basis, purposes, privacy notice/consent wording and per-category retention with the responsible club/privacy adviser. Do not assert blanket DSGVO compliance or a universal three-year period. Record any legal hold, erasure and backup-restoration handling. | Club signs off the documented process; retention config matches it; audit minimization and access rules independently reviewed. |
| 3.3 | P1 | Prove backups and recovery including bank-encryption keys and ASP.NET DataProtection keys; stage migration/rollback rehearsal. Review least-privilege SQL login, DB/network transport, HTTPS/cookies, log redaction/permissions and dependency patch process. | Restore exercise recovers synthetic members and decrypts bank data; migration rehearsal and data counts reconciled; operational owner and rollback triggers assigned. |
| 3.4 | P1 | Independent release security/QA review, basic load test with ≥1,000 synthetic members, and Desktop/iPhone browser acceptance. Check keyboard/accessibility, German dates/currency, error states and no host-design regressions. | Signed acceptance checklist with evidence, no unresolved blocker; meaningful latency target measured in a named environment rather than inherited untested 100/200ms claims. |
| 3.5 | P1 | Publish an immutable release candidate, run a controlled staff pilot only after security/privacy gates, then explicit go/no-go with Yves/axuno and the club. Deploy behind a server-enforced feature flag; monitor mail failures, auth abuse, job failures and DB health without PII. | Approved tagged v1.0 package, green host CI, accepted UAT and operational checklist; post-deploy smoke test and demonstrated rollback procedure. Production actions require separate approval. |
| 3.6 | P2 | After stable v1: rank SEPA, BLSV statistics, bulk CSV/PDF/vCard, imports and member-driven family extension against actual club demand. | New brief and acceptance criteria before implementation; maintained libraries/formats/license suitability checked first. |

**Final definition of done:** approved v1 scope works in the integrated host; all P0/P1 acceptance evidence attached; no unexpected local changes; reproducible released package; restore and operational ownership proven; Yves/axuno/club acceptance complete. No outstanding feature may be silently relabeled as done merely because unit tests pass.

## 6. Execution order and working rules

Recommended sequence:

1. Preserve/review the local regression diff; add a safe development branch when implementation is authorized.
2. **1.1–1.3 first:** credential handling, key lifecycle and destructive-job/audit behavior.
3. Establish **1.7** CI immediately in parallel with the runtime compatibility investigation (**1.6**); finish auth/data-boundary/update blockers **1.4–1.5/1.8** before real-data tests.
4. Resolve section 7 decisions before implementing Youth/Family/Identity behavior; then Phase 2 in small reviewed changes with SQL/HTTP tests.
5. Phase 3 rehearsal and acceptance; deployment only after explicit approval.

Use ASP.NET Core Identity/token providers, FluentValidation, EF Core, existing Bootstrap/ClubSite UI, VereinMail/MailKit and WebApplicationFactory before considering new custom infrastructure. For SQL tests evaluate an existing test-container fixture or isolated CI SQL service. Prefer a small SQL outbox over adding a separate broker, only if reliability requirements need it. No new paid service assumed.

Implementation workflow: one bounded feature/fix → independent code/security review as appropriate → QA with real discovered-test counts → PR. Preserve unrelated working changes; no production data in tests; never put credentials in logs/reports. No automatic commits, pushes, package publication, migrations or deployment are authorized by this planning request.

## 7. Scope decisions (Yves feedback 2026-11-16)

1. **Finish line — Approved:** v1 core scope approved; SEPA, BLSV/DOSB, general imports/exports stay post-v1.
2. **Registration/admission — Backlog (post-v1):** Trainer approval as an application workflow goes into the post-v1 backlog; v1 continues with the existing registration/invitation flow.
3. **Guardians & Youth/Family concept — Decided:** "Familie" and "Jugend" follow the same family/guardian concept; "Jugend" is for a single child whose parents are **not** active members (if parents are active, Family membership is cheaper). Still to confirm in implementation: whether each minor needs a distinct email/login or can share a parent's contact.
4. **Families — Post-v1:** member-driven family extension and advanced family changes belong in post-v1; v1 uses admin-managed family changes.
5. **Roles & field-level visibility — Post-v1 concept:** fine-grained visibility restrictions per role (Admin, Accounting, Board, Trainer, Member) need a dedicated post-v1 concept. For v1, keep strict least-privilege defaults (e.g., never expose bank/IBAN data to non-privileged roles or raw API responses).
6. **Reference data — Approved:** retain SQL-maintained fees and departments with a safe seed/retirement procedure (no new CRUD UI).
7. **Host/runtime (aligned on `net8.0` for now):** `axuno/ClubSite` `origin/main` targets `net8.0` and builds with `dotnet-version: 8.0.x` in CI (though it references two `10.0.14` packages: `Microsoft.EntityFrameworkCore.Tools` and `Microsoft.Extensions.Logging.Abstractions`). `VereinMember` stays on `net8.0` to match `ClubSite` until `ClubSite`/Piranha upgrades to `.NET 10 LTS`.
8. **Privacy/retention & go-live (pre-release gate, in backlog):** before going live with real member data, the club's board / data-protection contact must approve the exact retention rules (e.g., 3 years vs. statutory tax/accounting periods) and sign off on UAT/go-live.

## 8. Review limits and sources

Fresh checks: local source/working diff, direct .NET build/tests and solution trace, current remote VereinMember main SHA, recent Actions history, current checked-out ClubSite and local remote-tracking integration branch code. The latter tracking branches may be stale; reconcile with GitHub before implementation. No production DB, member records, SMTP connection, browser acceptance or deployment was inspected or changed. No vulnerability scan or coverage run was performed. Security conclusions are static findings unless a test above explicitly states otherwise.

Reference: Microsoft support policy, checked 2026-11-16: <https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core> (.NET 8/9 support ended 2026-11-10; .NET 10 LTS is supported). Latest queried successful build: <https://github.com/Pince-Rioux/VereinMember/actions/runs/41890255633>; latest queried successful publish: <https://github.com/Pince-Rioux/VereinMember/actions/runs/41891399849>.
