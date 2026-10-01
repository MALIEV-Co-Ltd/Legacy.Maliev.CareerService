# Career offer mutation parity, 2026-10-01

## Scope and source lineage

Bounded child #28 of route acceptance #27. Original source is inspected only as
committed Git objects, cutoff `bed10c7d15e0698e0b75f1329d0f312937f5d77f`.
`Maliev.JobService.Api/Controllers/JobsController.cs` has one behavior commit:
`5fac706a7983a6d359b39acbd670e6800afe020e`. Its offer POST and PUT assign
Description, IsFilled, LevelId, Title and Prerequisites. Introduction,
WhatWeOffer and Location remain readable/imported data; POST ignores them and
PUT preserves them. The migrated application had added these three assignments
to both actions. No documented source feature authorized that broader write.

The fix removes only those six assignments. Input DTOs retain the fields for
wire compatibility; output and read/search projections are unchanged. Original
creation/modified timestamp behavior and existing granular permissions remain.
No schema, credentials, SQL Server runtime provider or browser code is changed.
Original Web Career Index/View use anonymous listing/detail/levels; current Web
`CareerClient` reads those same paths and handles downstream404 as an empty
listing/absent detail. No mutation consumer or new rich-field write is introduced.

## Executed evidence

Private exact dependencies: ServiceDefaults
`8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3`, CompatibilityContracts
`78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. Both actually build in Release under
ignored `TestResults/.career-dependencies`; no shared canonical outputs.

Build first: `dotnet build Legacy.Maliev.CareerService.Tests/Legacy.Maliev.CareerService.Tests.csproj -c Release -p:TreatWarningsAsErrors=true`
with `UseLocalMalievDependencies=true` and absolute `MalievWorkspaceRoot`.
Each baseline/regression/final build reports zero warnings/errors.
Baseline complete suite: **36 passed, zero skipped**, TRX
`TestResults/career-route-baseline-full/natth_MALIEV-31USFIV_2026-10-01_11_29_42_net10.0.trx`.

Actual normal Production Program, RS256 JWT handler and permission middleware,
registered application/repository, disposable PostgreSQL18 and Redis7.4:
initial new HTTP gate **2 genuine failures / 13 passing controls**, zero skips.
TRX `TestResults/career-route-http-red/natth_MALIEV-31USFIV_2026-10-01_11_33_55_net10.0.trx`.
The failures occur after successful201/204 and independent persisted reads:
POST stores supplied introduction; PUT replaces imported introduction. They are
not mock expectations or fixture setup errors. After six-line repair, focus15
and full51 pass. Expanded final focus **27 passed**, complete suite **63 passed**,
zero failures/skips, including all original36.
TRXs: `career-route-expanded-focus/...11_38_03_net10.0.trx` and
`career-route-expanded-full/...11_38_15_net10.0.trx`.
After formatting, a fresh Release build again reports zero warnings/errors;
focus27 and full63 pass with zero skips. Final TRXs:
`career-route-formatted-focus/natth_MALIEV-31USFIV_2026-10-01_11_42_18_net10.0.trx`
and `career-route-formatted-full/natth_MALIEV-31USFIV_2026-10-01_11_42_25_net10.0.trx`.
Scoped test and documentation gitleaks scans report no leaks.

Additional normal HTTP controls prove anonymous default pagination and Thai
level projection, null omission/camelCase wire, 404 absent/empty/out-of-range,
all four original sorts, numeric ID OR text semantics, literal percent,
underscore/backslash and Thai searches, level create/update/persisted timestamps,
missing updates, null-body400, invalid-FK500 with zero rows and no provider text,
anonymous/expired/wrong-signature401 and missing-permission403 without writes.
The fixture does not replace authentication, permission, repository or cache.
No live IAM grant or external notification is exercised.

Raw unexcluded final coverage is API52/280=18.57%, Application105/119=88.24%,
Data473/493=95.94%, Domain17/18=94.44%. The API denominator includes generated
documentation code. This does **not** meet a raw API80% acceptance target;
broader #27 stays open. No exclusions, threshold changes or denominator edits.

Whole-solution format initially found new-test whitespace and three baseline
Program comment indentation diagnostics. Scoped whitespace formatting fixed
them; full verification then passes. The Program normalization has no Git
semantic diff. Five project transitive vulnerability audits and signing-resource
scan pass. General current-tree scanner finds one pre-existing never-connect
synthetic redaction-test literal at Startup/CareerStartupLoggingTests.cs:176;
canonical baseline reports the same finding, independently inspected. No real
credential or scanner suppression was added. Ignored outputs are not staged.

## Deliberately remaining acceptance

This repair closes #28 only after protected PR/main CI. #27 remains open for the
full route/provider/Aspire matrix, raw API coverage, positive forced-live DELETE
and linked-level relational retention/cancellation/concurrency characterization.
No audited or related data deletion policy is inferred from these tests.

Other individual JobService history cohorts remain separately tracked:
`5ac7d045c51194edd9e64d8564f1b726b001be34` and
`9e51e6c5da29de8e617b65b59d46882cde6d3b64` logging architecture;
`f0640fe0719b2eb6becda378bff08153d955be07` failure tracing;
`03dc9a1271c16e6535934445e9dd6e3f30e8fffe` XML isolation;
`cbac7d7155da2208c77d56103b6a2cb19196fc83`,
`7d6f46f53cbab853ca9c25e385af067cfff6238a`,
`2aab25eb07894fc0267b03b85bad96490219d2fa` externalized configuration;
all remaining manifest/resource/deployment commits require their own reviewed
owner dispositions. This document does not close any whole source ledger entry.
No application deployment, traffic change, persistent migration/data write,
production-derived parity or complete Aspire acceptance is claimed.
