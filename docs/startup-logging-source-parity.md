# Career startup and request-failure logging proof

Owner issue: [CareerService #25](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CareerService/issues/25), Project 2.
Source ownership: committed `Maliev.JobService.Api/Program.cs`, `Startup.cs`, API project and both Dockerfiles.
Source SHAs: `5ac7d045c51194edd9e64d8564f1b726b001be34` and
`f0640fe0719b2eb6becda378bff08153d955be07`.
Cross-owner trackers: Legacy.Maliev.Workflows #143 and #162.

The approved target architecture remains shared ServiceDefaults JSON console and
exception middleware, rather than clearing providers and copying app-local source
exception logging. It retains Cloud severity, OpenTelemetry, scopes, W3C trace and
span IDs, UTC timestamps, type-only failures, and route-template redaction.
No source configuration or private history is copied.

## Gap and narrow repair

An actual Docker dependency failure on the previous startup emitted EF Query
event 10100 with provider exception text and stack inside its formatted `Message`.
Type-only serialization of the `Exception` field could not redact that message.
Career now disables only EF's duplicate exception-rendering diagnostics:
`QueryIterationFailed`, `SaveChangesFailed`, and `ExecutionStrategyRetrying`.
The middleware still emits the actionable `UnhandledRequestFailure` critical
event with service, method, sanitized route, status, exception type, incident ID,
and occurrence time; other categories/providers and warnings are unchanged.
Existing shared database-command severity filtering is retained.

Five acceptance cases execute actual Production startup: controller failure,
anonymous/protected route boundaries plus runtime dependency graph, started
response preservation, and real PostgreSQL read/save failure using Testcontainers.
Test-only controllers are loaded only by the factory, not production startup.
The PostgreSQL fixture intentionally lacks tables; attempted writes fail and do
not persist data. No deployed or persistent database is contacted.

Read-only consumer inspection included committed Web Career Index/View calls to
`/jobs`, `/jobs/Levels` and `/jobs/{offerId}`, source Jobs controller routes,
and target controllers/DTOs. Business route and payload contracts are unchanged.
The existing shared safe error JSON (`error`, `statusCode`, `details`, `traceId`)
is asserted at the actual HTTP boundary.

## Validation, 2026-09-30

Use `MalievWorkspaceRoot` pointing at isolated `.dependencies`, containing the
exact workflow pins: ServiceDefaults `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3`
and CompatibilityContracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`.

- `dotnet build Legacy.Maliev.CareerService.slnx -c Release`: 0 warnings/errors.
- `dotnet test Legacy.Maliev.CareerService.Tests -c Release --no-build --filter FullyQualifiedName~CareerStartupLoggingTests`: 5 passed, 0 failed/skipped.
- `dotnet test Legacy.Maliev.CareerService.Tests -c Release --no-build --collect:'XPlat Code Coverage'`: 36 passed, 0 failed/skipped.
- `dotnet format Legacy.Maliev.CareerService.slnx --verify-no-changes --no-restore`: exit 0.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages in all five projects.
- `gitleaks dir . --redact=100 --exit-code 1 --no-banner --no-color`: no leaks.
- `Invoke-JwtSigningResourceScan.ps1 -RepositoryPath .`: exit 0.
- `git diff --check`: passed.
- `docker build -f Legacy.Maliev.CareerService.Api/Dockerfile -t legacy-career-startup-acceptance:20260930 .`: passed.
- Local non-root container (UID 1654), generated public RSA test key, Redis disabled,
  deliberately unreachable database, Information logging: liveness 200, business
  read 500, generic JSON with null details, one matching CRITICAL incident event,
  no provider exception messages/stacks, no NativeLogging/NLog/LoggerService
  assemblies. Container removed after proof; no image publication/deployment.

Residual scope: this is not a fleet-wide formatter audit or live OTLP-export proof.
Coverage was collected but the existing repository is below the skill's 80% goal
(API 12.5%, Application 48%, Data 94.32%, Domain 94.44%); this slice does not claim
that goal passed or broaden into unrelated coverage work. Protected-main merge
and exact-main CI remain root-owned after the PR's validation.
