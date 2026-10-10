# Original empty level Offers response parity

Issue #27; baseline producer main `3b18b5ef124c35c2f4aa6a3ccc5e89ebf904e8b2`.

At original checkpoint `135e526d0dab85c415b3afdcefd7b70fe2c82e2f`,
`Level` initializes its public `Offers` collection. `LevelsController` returns
entities directly for list/detail and constructs a fresh entity for create.
MVC NewtonsoftJson uses camelCase; null/reference-loop suppression does not
suppress this non-null empty collection. The source-derived isolated level
response therefore includes `"offers":[]`. Original SQL-host execution is not run.

Three independent normal HTTP regressions cover anonymous list and detail plus
authorized create with existing Production auth, PostgreSQL and Redis fixtures.
The input is a synthetic unlinked level with name/description only. Reads verify
stored values and concurrency version remain unchanged; create checks persisted
values, Location and normal permission checks. Controlled remote IAM transport
does not prove deployed IAM readiness.

This tests-only baseline preserves every existing test, production file, route,
permission and dependency/workflow pin. Current selected-deletion tests still
assert Offers absence; they are retained until actual baseline failure review.
Linked collections and nested offer-level graphs require separate analysis.
Do not change the producer DTO before reviewing actual runtime failure after a
zero-warning/zero-error Release build. Compiler/setup failures are not behavior RED.

Local validation is NOT RUN: no local native allocation; another owner's finite
allocation remains exclusive. A fresh memory snapshot does not grant SDK custody.
The owner-approved migration exception permits this reviewed draft
to obtain actual validation through the existing hosted PR workflow. Required
focused/full original-roster, raw coverage, format, audit/security and protected
head/main checks remain unchanged. No source SHA is closed by this baseline.

Original source associations (full identities; all remain OPEN):
- `5fac706a7983a6d359b39acbd670e6800afe020e`
- `72eb9f1949176392141951d35e6e06f7c30af4c2`
- `9e51e6c5da29de8e617b65b59d46882cde6d3b64`
- `a59193ae2ac030d0e1d373ce4ec50c1b35437391`
- `a649db99a27bda65274fe1b18866ae226d3c69cf`
- `eb8ed86672bd9afccc6560b547b734d0fcd7363b`
