# Career publisher validation adoption - issue 32

Frozen candidate for independent root acceptance; no publication or deployment
was executed or enabled. This is a thin caller adoption, not whole-service,
source-owner, production or Aspire acceptance.

## Immutable boundary

- Career base: `34ae5b1cf72ef37ae5776cacbf89a284d5876f7e` (root reports
  exact-main CI `36819426091` successful).
- Accepted reusable publisher: Workflows
  `503e8846390a597c267d2889b33a9c26863389b3` (root reports exact-main
  `36823157482` successful).
- Private dependency clones: Defaults
  `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3`, Contracts
  `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`; both clean.

Only the publisher pin and its job-local `actions: read` permission changed.
`contents: read` and `id-token: write` remain job-local. The
`LEGACY_DEPLOY_ENABLED` opt-in and complementary planned-only gate, triggers,
concurrency, image, Dockerfile, context, environment, dependency pins and WIF /
service-account inputs are unchanged. The reusable digest interface is unchanged.
The caller now grants the read ceiling required by the reusable workflow's
exact-SHA main-validation checks; it does not activate cloud publication.

The existing YAML contract preserves every previous assertion and additionally
checks the exact three-entry caller permission map. The independent existing
OIDC-scoping contract is untouched. No duplicated producer implementation tests
were introduced.

## Executed evidence

Commands run from this worktree; no other worktree outputs were consumed.

```powershell
dotnet build Legacy.Maliev.CareerService.Tests/Legacy.Maliev.CareerService.Tests.csproj -c Release -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/career-publisher-validation-20261001/TestResults/.private --no-restore --nologo
dotnet test Legacy.Maliev.CareerService.Tests/Legacy.Maliev.CareerService.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~PublicationDependencyTests|FullyQualifiedName~PublishWorkflowPermissionContractTests'
dotnet test Legacy.Maliev.CareerService.Tests/Legacy.Maliev.CareerService.Tests.csproj -c Release --no-build --no-restore
```

- Initial and final Release builds: zero warnings / zero errors, including private
  dependencies in Release.
- Test-first `TestResults/publisher-red/publisher-red.trx`: one genuine old-pin
  assertion failure, one unchanged OIDC contract passing, zero skips/errors.
- `TestResults/publisher-focus/publisher-focus.trx`: 2 passed, zero failures/skips.
- `TestResults/publisher-full/publisher-full.trx`: all 89 passed, zero failures/skips.
- Whole `dotnet format ... --verify-no-changes --no-restore`: initially three
  WHITESPACE diagnostics in unchanged API `Program.cs` lines 12-14. Root approved
  scoped formatting of that file. Only working-file CRLF normalization resulted;
  normalized Git blob remains identical to HEAD:
  `2b87c66efb85e4cee84a2b229210d4d75e67dc54`. Whole format rerun exit 0.
  No semantic/source diff; no additional build repetition was required by root.
- `actionlint .github/workflows/publish-image.yml`: exit 0.
- `git diff --check`: pass.
- `dotnet list ... package --vulnerable --include-transitive`: no vulnerable
  packages in all five service projects under the current package feed.
- Scoped gitleaks workflow and workflow-test scans: no leaks. History gitleaks:
  29 commits, no leaks. JWT signing-resource scan: pass.
- Whole current-tree credential scanner reports one unchanged synthetic test
  connection-string-password finding in `Startup/CareerStartupLoggingTests.cs`
  line 176. Values were redacted; fixture was not edited. This is a disclosed
  baseline scanner finding, not a claimed clean scan or permission to suppress it.

## Five scoped audit dispositions

1. GitHub Actions: exact immutable reusable pin, least required job-local read
   ceiling, unchanged OIDC scoping/default-off gate; parsed caller contracts and
   actionlint pass.
2. API: no controller, route, DTO or permission changes (diff inspection).
3. Messaging: no producer/consumer or contract changes (diff inspection).
4. Migration: no migration, model or schema changes (diff inspection).
5. Performance: no query, application or repository changes (diff inspection).

The latter four are unaffected-boundary reviews, not executed integration or
production claims. No runtime, package, other workflow, shared contract, ledger,
GitHub state or infrastructure changes. Root owns issue 32 and integration;
no commit or push was created by this writer.

## Independent root acceptance

Root reran the Release graph with the same clean private dependencies: zero
warnings/errors; focused caller and OIDC contracts 2 passed; unfiltered suite
89 passed, zero failures/skips. Fresh TRX files are under
`TestResults/root-publisher-focus` and `TestResults/root-publisher-full`
(full run `2026-10-01_13_20_00_net10.0.trx`). Whole formatting, all five
transitive package audits, actionlint and diff checks passed. Root inspected
the actual YAML permission/pin diff and unchanged runtime/Docker inputs.
`Program.cs` normalizes to the same committed blob and has no content diff;
it is excluded from the coherent change. The unchanged synthetic current-tree
scanner finding above remains disclosed, not suppressed or counted as a pass.
Generated outputs/private clones remain ignored and outside staging.
Protected-head and exact post-merge hosted validation are separate gates.
