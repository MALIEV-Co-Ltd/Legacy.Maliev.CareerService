# Career #30 lifecycle repair / #27 remaining acceptance

## Ownership and current scope

Owned workspace `B:/maliev-legacy/.worktrees/career-lifecycle-acceptance-20261001`, branch `codex/career-lifecycle-acceptance-20261001`, exact base `bf8780d2af38cd17f2eba7e10e92691aa04c451b`. Canonical Career, older Career candidates, Employee and File outputs are read-only/root-owned. After the observed TEST/DESIGN RED and root gate, the bounded #30 runtime repair is present: two DELETE live-check attributes, four repository concurrency boundaries, Application-owned typed exception and four typed controller catches. Two new test files and this document accompany it. Existing tests, shared Defaults, DI configuration, schema and public DTOs remain unchanged. Scoped Program whitespace normalization has no semantic/content diff after Git line-ending normalization. No commit/push/GitHub/provider/deployment actions. Database/cache writes use uniquely owned disposable PostgreSQL 18/Redis 8 containers with synthetic fixtures.

Base includes root's #28 source mutation-whitelist repair; unchanged. Source-data migration, provider activation, deployment/IAM grants and whole-source-owner closure are excluded. Parent #27's raw API 80% floor remains required without exclusions. Current final validation: Release 0 warnings/errors, focus 26/26, full 89/89 passed, zero skips; full format, package audit, gitleaks and diff checks pass. Raw API 21.52% is below the 80% gate (62/288 lines; handwritten 62/62, generated 0/226). Application 98.34%, Data 97.23%, Domain 94.44%. #30 bounded repair awaits root independent acceptance; #27 quality stays open. Candidate and outputs are frozen/released to root, no live process/handle.

## Architecture and real evidence

Root independent acceptance on October 1: compiled the full Release test-project graph with private exact CI pins (`MalievWorkspaceRoot=.dependencies`, Defaults `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3`, Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`) and warnings as errors: zero warnings/errors. Executed focus 26/26 and full 89/89 with zero skips, followed by whole-solution `dotnet format --verify-no-changes --no-restore`, all five project transitive vulnerability audits, scoped secret checks and `git diff --check`: passed. Independent TRX directories are ignored `TestResults/root-career30-focus` (12:16:33) and `TestResults/root-career30-full` (12:16:37). Earlier unqualified solution build used sibling dependency outputs; it is not the final pinned acceptance graph. Protected PR/exact-main CI remains mandatory before issue completion. Root inspected all runtime boundaries and both complete new test files; no source-owner disposition changes or production acceptance are inferred.

New files: `Tests/Controllers/CareerLifecycleFixture.cs` and `CareerLifecycleAcceptanceTests.cs` (under the actual `Legacy.Maliev.CareerService.Tests` directory). Normal Production host uses actual RS256 JWT middleware, registered Application/Repository, configured Npgsql PostgreSQL and Redis cache registration. Only named `IAMService` primary HTTP transport is controlled through actual `IamServiceClient`. It validates exact POST `/iam/v1/auth/check-permission`, principal, permission, current global resource, bypass flag and live credential header. Standard responses deliberately deny upstream; current signed exact claims exercise fallback. Live responses allow/deny/503/malformed. Unique principals avoid shared positive-cache aliasing. This is component evidence, not deployed grant proof; no fake authentication or repository/cache replacement.

`ScheduledSave` is an EF SaveChanges interceptor pausing before SQL writes after actual Application/Repository loaded a tracked entity. Winner requests use a separate normal host/context. Real PostgreSQL xmin enforces conflicts; winner persistence is independently read before asserting HTTP classification. HTTP cancellation controls cancel after the real save boundary, require OperationCanceledException and server-observed canceled token, then verify unchanged rows. Direct registered-repository canceled-conflict controls use the actual EF dedicated `ThrowingConcurrencyExceptionAsync` hook to cancel on genuine stale-xmin failure. These are not positive HTTP auth proof. Timeouts are never accepted as cancellation proof.

## Current/source action and consumer matrix

Source producers at committed cutoff `bed10c7d15e0698e0b75f1329d0f312937f5d77f`: `Maliev.JobService.Api/Controllers/JobsController.cs`, `LevelsController.cs`, Startup and `Maliev.JobService.Data/Database/JobContext/JobOffersContext.cs`. Only initial commit changes controller action history. Source has eleven actions; no invented route.

| Method/path | Current permission / authority | Preserved wire/behavior |
| --- | --- | --- |
| POST /Jobs | legacy-career.jobs.create | Upsert; 201 GetOffer Location; approved source writable-field whitelist |
| GET /Jobs | anonymous | sort/search/index/size; camelCase paginated offers and level projection; empty 404 |
| GET /Jobs/{offerId} | anonymous | camelCase offer; absent 404 |
| GET /Jobs/job-opening-status | anonymous | literal boolean; only IsFilled false counts open |
| PUT /Jobs/{offerId} | legacy-career.jobs.update | 204/404; preserve imported Introduction/WhatWeOffer/Location and CreatedDate |
| DELETE /Jobs/{offerId} | legacy-career.jobs.delete, live/critical, global | 204/404, deny/fault/no-client 403; typed conflict 409 |
| POST /jobs/Levels | legacy-career.levels.create | name/description; 201 GetLevel Location |
| GET /jobs/Levels | anonymous | camelCase array; empty 404 |
| GET /jobs/Levels/{levelId} | anonymous | camelCase level; absent 404 |
| PUT /jobs/Levels/{levelId} | legacy-career.levels.update | 204/404; original CreatedDate retained |
| DELETE /jobs/Levels/{levelId} | legacy-career.levels.delete, live/critical, global | unlinked 204/404; linked FK refusal preserves both; typed conflict 409 |

Source LevelID is required and relationship uses ClientSetNull with `FK_Offers_Level`; deleting a linked level cannot invent cascading/nulling semantics. Current real PG FK failure is generic HTTP 500 and preserves rows. That control is GREEN, not a proposed source parity defect. Current granular permissions and desired forced-live deletion are accepted security architecture beyond original source's JWT-only mutation authorization. Conflict 409 is current xmin correctness design, not a claim that original SQL Server action returned 409.

Read-only current consumer `Legacy.Maliev.Web.Infrastructure/CareerClient.cs`: detail GET line 42, paginated GET line 73, levels GET line 97; anonymous named careers transport, exact query names, 404 becomes available empty/not-found, transient HTTP/unowned timeout becomes unavailable, caller cancellation propagates. No current Intranet Career route hits found in scoped source search. Do not change these consumer routes or public reads. Source Startup null/reference-loop omission is retained; current actual MVC wire and actual generated documentation both use camelCase. No Employee-like PascalCase documentation defect exists here.

## Phase 1 — retained TEST/DESIGN RED chronology

First focused 16 cases: 12 RED/4 GREEN, zero errors/skips. Expanded focus 18: 14 RED/4 GREEN, zero errors/skips.

- DELETE positive two cases perform actual 204 then 404 and verify selected real rows removed; expected two independent live checks, actual zero. This is not positive forced-live acceptance yet.
- DELETE denial/unavailable/malformed/no-client eight cases return 204 despite exact signed permission and required expectation 403/no effects. Pinned `RequirePermissionAttribute` and handler do not auto-upgrade `.delete`: both actual controller attributes lack RequireLiveCheck/IsCritical.
- Concurrent update two cases return 500 instead of proposed 409 while real xmin preserves the winner.
- Concurrent DELETE two cases return 500 instead of proposed 409 after another context actually removes the row; no resurrection.
- GREEN: two actual caller-abort cases, linked-level FK preservation/error redaction, actual nonProduction HTTP OpenAPI five exact camelCase DTO schemas and Production documentation 404.

## Phase 2 — root-approved narrow runtime and additional controls

Root observed 14 genuine initial REDs and approved only: both DELETE attributes RequireLiveCheck=true/IsCritical=true with unchanged global scope/permission; `Application/Models/CareerConcurrencyException.cs` fixed generic message preserving internal cause; four Update/Delete repository save boundaries catch genuine `DbUpdateConcurrencyException` only with nonempty entries all JobOffer/JobLevel; `cancellationToken.ThrowIfCancellationRequested()` precedes typed translation; four typed controller catches return fixed generic 409. No Add/read catches, EF reference in Application, tracked xmin overwrite, retry-policy change or global exception classifier. Missing rows still 404. Source mutation whitelist untouched.

Additional arbitrary-save controls retain pinned classifier behavior: IOException 500, InvalidOperationException 400, neither becomes 409; response omits fault message and no writes commit. Initial NEW assumption expected InvalidOperation 500; pinned `ExceptionHandlingMiddleware.cs:112` explicitly maps it to 400, so tests were corrected with root confirmation and both exception categories retained. Linked-level real FK error remains generic 500/preserves both rows. No broad arbitrary-error mapping change.

Cancellation fixture correction: initial `SaveChangesFailedAsync` callback does not execute for EF's dedicated xmin conflict path. Four early 'canceled conflict' failures were fixture diagnostics, not valid product RED. Dedicated `ThrowingConcurrencyExceptionAsync` now cancels and the test explicitly verifies token cancellation before asserting OCE. Corrected controls pass. A bounded mutation removed only the four cancellation checks, rebuilt, and all four controls failed with typed conflict despite observed caller cancellation. Restoring the four checks rebuilt 0W0E and final focus/full passed. No mutation or test relaxation remains. This establishes true sensitivity without claiming the invalid fixture diagnostics as product evidence.

## Individual source-owner cohort

Read-only Workflows ledger has 23 Career owner entries. Matrix is classification of this bounded slice, not dispositions or a request to close whole SHA ownership:

| Full SHA | Bounded classification |
| --- | --- |
| 5fac706a7983a6d359b39acbd670e6800afe020e | Eleven business actions introduced; this lifecycle acceptance plus earlier root mutation/read controls |
| 72eb9f1949176392141951d35e6e06f7c30af4c2 | Packages/generated documentation/model refresh; no new controller action |
| 3a393215d883fa35e1461f69c876bf2ead7ce36e | Deployment ingress split, excluded |
| 0822636e5e2d46e4db20a79d27037aab426d85aa | Deployment resources/node selection, excluded |
| 3a104503328cc3c0d57ff9ae2deafba06d1e46d5 | Deployment node selection, excluded |
| 5458b7ddc81a15d72087fa69fb4cfcc27ae75747 | Frontend retirement/deployment, excluded |
| 53f4baf373ef04a3ed5ab5c1ef39bd61404c5258 | Deployment limits, excluded |
| 93f9f99522fbe6c128acb5d049f2b448e07dba95 | Deployment tuning, excluded |
| 90f34b389c298d1ce85abe2ae7ac92877dbbf7af | Frontend deployment scripts, excluded |
| 00ec830615c15b5e4e227046712247b11df0100f | Deployment hardening, excluded |
| 2aab25eb07894fc0267b03b85bad96490219d2fa | Design-time DB externalization, no source-data parity claim |
| 7d6f46f53cbab853ca9c25e385af067cfff6238a | Credentials externalization, no credentials/deployment writes |
| cbac7d7155da2208c77d56103b6a2cb19196fc83 | Already migrated ledger #21/PR22; normal RS256 controls do not redispose |
| eb8ed86672bd9afccc6560b547b734d0fcd7363b | Secret-remediation merge, broad operational exclusion |
| a649db99a27bda65274fe1b18866ae226d3c69cf | Merge cohort/migration history, excluded broad source-data migration |
| 03eaff1194c3ae2a54ceefeae31deffaff90436f | Docker context, excluded deployment |
| 72163e9ae11f39f6579423841a2e20529b986fab | Deployment status, excluded |
| f8921b1b1d5846eeaff999af10b640011655d1d4 | Throwaway deployment rendering, excluded |
| 143f53ba0a1c81c78d252864ca131d42ed79dc1b | Production secret readiness, excluded live operational proof |
| f0640fe0719b2eb6becda378bff08153d955be07 | Already migrated tracing ledger #25/PR26; retain provider/PII redaction |
| 9e51e6c5da29de8e617b65b59d46882cde6d3b64 | Native logging transition; no broad logging/runtime rewrite in this slice |
| 03dc9a1271c16e6535934445e9dd6e3f30e8fffe | Generated XML isolation; unexcluded coverage retained |
| 5ac7d045c51194edd9e64d8564f1b726b001be34 | Already migrated application-local native logging #25/PR26; no redisposition |

## Commands and evidence

Own detached CI pins: Defaults `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3`, Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`, CI validator `73dd7304ffe85ec504389fd7664cc39070b9f148`. No shared outputs. All dotnet commands use `-p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/career-lifecycle-acceptance-20261001/.dependencies`; format/audit use equivalent environment values.

```powershell
dotnet restore Legacy.Maliev.CareerService.slnx -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/career-lifecycle-acceptance-20261001/.dependencies
dotnet build Legacy.Maliev.CareerService.slnx -c Release --no-restore -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/career-lifecycle-acceptance-20261001/.dependencies
dotnet test Legacy.Maliev.CareerService.Tests/Legacy.Maliev.CareerService.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~CareerLifecycleAcceptanceTests --logger trx --results-directory TestResults/career27-complete-focus-red -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/career-lifecycle-acceptance-20261001/.dependencies
```

Restore passed. Fresh baseline/final test compilation Release 0 warnings/errors. Initial missing test-only Tokens namespace caused compiler errors, corrected before execution; not product RED. Baseline `TestResults/career27-baseline/natth_MALIEV-31USFIV_2026-10-01_11_50_41_net10.0.trx` 63/63 passed, zero skips. Initial focus `career27-lifecycle-red/...11_54_41` 12 failed/4 passed of 16. Expanded focus `TestResults/career27-complete-focus-red/natth_MALIEV-31USFIV_2026-10-01_11_56_11_net10.0.trx` 14 failed/4 passed of 18. All failures are assertion RED, not errors. Existing tests untouched. No final acceptance/commit-ready claim while these REDs and raw quality gap remain.

Final full command is the test command above without filter, with `--collect 'XPlat Code Coverage' --results-directory TestResults/career27-complete-full-red`. It terminated with **67 passed/14 intended failed/0 errors/0 skips, total 81**. Thus all 63 existing tests remain passing. TRX `TestResults/career27-complete-full-red/natth_MALIEV-31USFIV_2026-10-01_11_56_47_net10.0.trx`; coverage `TestResults/career27-complete-full-red/84e93913-4c22-4bee-b175-18c50cf03744/coverage.cobertura.xml`.

Raw unexcluded coverage: API **19.28% (54/280 lines)**, Application **98.31%**, Data **97.16%**, Domain **94.44%**. API handwritten lines 54/54 hit; generated OpenAPI XML-comment helper lines 0/226 hit even through real document HTTP. No reflected helper execution, registration restructuring for coverage, exclusions, threshold edits or waiver. The raw API quality gate remains open; handwritten-only accounting is explanatory and not a replacement denominator.

Statics (all terminal):

- Historical test-only full format **failed** on pre-existing Program.cs comment whitespace lines 12–14; not corrected until root permitted scoped whitespace normalization with runtime gate. Final full format passes. Program has no semantic/content diff after Git line-ending normalization.
- Same format verification scoped with `--include Legacy.Maliev.CareerService.Tests/Controllers/CareerLifecycleFixture.cs Legacy.Maliev.CareerService.Tests/Controllers/CareerLifecycleAcceptanceTests.cs` passes. Prior scoped whitespace formatter touched only these new files.
- `dotnet list Legacy.Maliev.CareerService.slnx package --vulnerable --include-transitive`: all five product/test projects have no vulnerable packages, exit 0.
- `gitleaks dir . --redact --log-level warn --report-format json --report-path TestResults/career27-gitleaks.json`: zero findings, exit 0.
- Historical `git diff --check` passed. See final current evidence below; no commit created.

## Final current evidence / release to root

- Corrected extra control RED/diagnostics: `TestResults/career27-corrected-fault-red/natth_MALIEV-31USFIV_2026-10-01_12_03_26_net10.0.trx` (4 fault controls GREEN, four invalid-cancellation-fixture failures; do not count latter as product RED).
- Corrected cancellation controls: `TestResults/career30-corrected-cancel-control/natth_MALIEV-31USFIV_2026-10-01_12_06_11_net10.0.trx`, 4/4 passed.
- True cancellation mutation RED: `TestResults/career30-cancel-mutation-red/natth_MALIEV-31USFIV_2026-10-01_12_06_50_net10.0.trx`, 4/4 intended failures. Removed four ct checks only, restored before final verification.
- Final Release build: 0 warnings/0 errors using private pins/absolute paths above.
- Final focus: `TestResults/career30-final-focus/natth_MALIEV-31USFIV_2026-10-01_12_07_21_net10.0.trx`, 26/26 passed, zero skips.
- Final full: `TestResults/career30-final-full/natth_MALIEV-31USFIV_2026-10-01_12_08_04_net10.0.trx`, 89/89 passed, zero skips; all 63 old tests unchanged/passing.
- Coverage: `TestResults/career30-final-full/7bde973e-6a96-4e36-84f0-e14d6fbcca17/coverage.cobertura.xml`. Raw API 21.52%, Application 98.34%, Data 97.23%, Domain 94.44%; no exclusions or replacement denominator.
- Final `dotnet format Legacy.Maliev.CareerService.slnx --verify-no-changes --no-restore` passes, exit 0.
- Final `dotnet list Legacy.Maliev.CareerService.slnx package --vulnerable --include-transitive`: all five projects no vulnerable packages, exit 0.
- Final `gitleaks dir . --redact --log-level warn --report-format json --report-path TestResults/career30-gitleaks.json`: zero findings, exit 0.
- Final `git diff --check` passes. All process handles terminal. Source/canonical/other owned outputs never built or modified. No persistent writes, provider calls, deployment or commit/push. Root owns further integration and validation.

Final test commands use the earlier command with `--filter FullyQualifiedName~CareerLifecycleAcceptanceTests --results-directory TestResults/career30-final-focus`; full removes filter and adds `--collect 'XPlat Code Coverage' --results-directory TestResults/career30-final-full`. All carry the unchanged exact private dependency properties. Historical RED denominators are retained separately from final GREEN; no blanket source closure or raw quality acceptance.
