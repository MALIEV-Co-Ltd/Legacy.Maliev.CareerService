# Legacy.Maliev.CareerService

[![PR validation](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CareerService/actions/workflows/pr-validation.yml/badge.svg)](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CareerService/actions/workflows/pr-validation.yml)
[![Main CI](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CareerService/actions/workflows/ci-main.yml/badge.svg)](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CareerService/actions/workflows/ci-main.yml)

Temporary .NET 10 compatibility service extracted from `maliev-web`. It preserves
the legacy integer-key `Offer` and `Level` schemas plus `/Jobs` and `/jobs/Levels`
JSON contracts while the
new `Maliev.CareerService` is developed independently.

## Architecture

The service uses clean dependency direction: `Api` calls `Application`, domain rules live in
`Domain`, and PostgreSQL/Redis adapters live in `Data`. It depends only on the public
`Legacy.Maliev.ServiceDefaults` and `Legacy.Maliev.CompatibilityContracts` source repositories
during CI and image builds, so the legacy runtime no longer consumes new-platform shared-library
source or private package credentials.

## API endpoints

| Purpose | Method | Route | Access |
| --- | --- | --- | --- |
| Legacy job offer list | `GET` | `/Jobs` | Anonymous |
| Legacy job-opening status | `GET` | `/Jobs/job-opening-status` | Anonymous |
| Legacy job offer lookup | `GET` | `/Jobs/{offerId}` | Anonymous |
| Legacy job offer create | `POST` | `/Jobs` | `legacy-career.jobs.create` |
| Legacy job offer update | `PUT` | `/Jobs/{offerId}` | `legacy-career.jobs.update` |
| Legacy job offer delete | `DELETE` | `/Jobs/{offerId}` | `legacy-career.jobs.delete` |
| Legacy level list | `GET` | `/jobs/Levels` | Anonymous |
| Legacy level lookup | `GET` | `/jobs/Levels/{levelId}` | Anonymous |
| Legacy level create | `POST` | `/jobs/Levels` | `legacy-career.levels.create` |
| Legacy level update | `PUT` | `/jobs/Levels/{levelId}` | `legacy-career.levels.update` |
| Legacy level delete | `DELETE` | `/jobs/Levels/{levelId}` | `legacy-career.levels.delete` |
| Scalar UI | `GET` | `/Jobs/scalar` | Anonymous |

## Runtime boundaries

- Legacy route: `/Jobs`
- Scalar: `/Jobs/scalar`
- PostgreSQL database: `JobOffers` on `legacy-postgres-main`
- Redis key prefix: `legacy:career:`
- Public job offer listing remains anonymous; protected operations require granular
  `legacy-career.*` permissions.

This service does not modify the SQL Server source. PostgreSQL promotion requires
the artifact-backed parity and cutover gates tracked in `MALIEV-Co-Ltd/maliev-web`.

Deployment is intentionally validation-only until a dedicated
`legacy-maliev-career` Workload Identity Federation provider and
`maliev-gitops/3-apps/_legacy-career-service` manifest path exist. The existing
`maliev-career-service` GitOps path belongs to the new implementation and must
not be overwritten by this legacy compatibility service.

## Validate

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet format Legacy.Maliev.CareerService.slnx --verify-no-changes --no-restore
dotnet list package --vulnerable --include-transitive
```
