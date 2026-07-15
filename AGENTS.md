# Legacy.Maliev.CareerService

This repository is the public, sanitized legacy compatibility service for career
job offers and levels that used to live in `R:\maliev-web` as `Maliev.JobService`.

## Non-negotiable boundaries

- Keep the original `maliev-web` repository private.
- Do not copy monorepo Git history or legacy configuration files.
- Do not commit connection strings, service-account material, JWT keys, SMTP
  credentials, or generated secret-audit evidence.
- Preserve the legacy `/Jobs`, `/Jobs/job-opening-status`, and `/jobs/Levels`
  route contracts until consumers are explicitly migrated.
- The database source of truth remains legacy until the PostgreSQL parity and
  cutover gates in `maliev-web` pass.

## Validation

Run from this repository root:

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet format Legacy.Maliev.CareerService.slnx --verify-no-changes --no-restore
dotnet list package --vulnerable --include-transitive
gitleaks git . --redact=100 --exit-code 0 --no-banner --no-color
```

## Service conventions

- Runtime: .NET 10.
- OpenAPI UI: Scalar through `Maliev.Aspire.ServiceDefaults`; no Swashbuckle.
- Logging: built-in `ILogger<T>` only; do not reintroduce `Maliev.LoggerService`.
- Cache: Redis keys must use the `legacy:career:` prefix.
- Auth: protected mutation endpoints use granular `legacy-career.*`
  permissions; public job listings and job-opening status stay anonymous.
- Data model: `JobOffer` maps to legacy `Offer`, and `JobLevel` maps to legacy
  `Level`; do not alter the source SQL Server schema from this service.
