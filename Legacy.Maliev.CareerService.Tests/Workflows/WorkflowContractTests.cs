using System.Text.RegularExpressions;
using System.Xml.Linq;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Legacy.Maliev.CareerService.Tests.Workflows;

/// <summary>Checks the existing build, dependency, security, and evidence workflow boundaries.</summary>
public sealed class WorkflowContractTests
{
    private static readonly string Workflow = File.ReadAllText(FindRepositoryFile(".github", "workflows", "_build-and-test.yml"));
    private static readonly string ApiProject = File.ReadAllText(
        FindRepositoryFile("Legacy.Maliev.CareerService.Api", "Legacy.Maliev.CareerService.Api.csproj"));
    private static readonly string DataProject = File.ReadAllText(
        FindRepositoryFile("Legacy.Maliev.CareerService.Data", "Legacy.Maliev.CareerService.Data.csproj"));

    /// <summary>Checks the maintained build-and-test structural contract.</summary>
    [Fact]
    public void BuildAndTest_SatisfiesStructuralContract()
    {
        WorkflowContractValidator.Validate(Workflow);
        foreach (var (original, replacement) in new[]
        {
            ("--warnaserror", ""),
            ("python3 -B scripts/career_required_ci_preflight.py", "python3 -B scripts/unreviewed_preflight.py"),
            ("FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalPaginationWireHttpTests", "FullyQualifiedName~NoSuchPaginationTests"),
            ("Total:[[:space:]]+4,", "Total:[[:space:]]+0,"),
            ("Total:[[:space:]]+6,", "Total:[[:space:]]+0,"),
            ("--no-build --no-restore --filter", "--no-restore --filter"),
            ("FullyQualifiedName~Update_ChangesExistingLevelAndRefreshesPublicProjectionWithoutChangingEitherPrincipalOrSurvivor", "FullyQualifiedName~NoSuchTests"),
            ("|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests", ""),
            ("Total:[[:space:]]+26,", "Total:[[:space:]]+0,"),
            ("FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOfferDetailSourceHttpTests", "FullyQualifiedName~NoSuchDetailTests"),
            ("Total:[[:space:]]+5,", "Total:[[:space:]]+0,"),
            ("python3 -B scripts/career_offer_detail_focus.py", "python3 -B scripts/unreviewed_detail_focus.py"),
            ("timeout-minutes: 20", "timeout-minutes: 21"),
            ("actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68", "actions/setup-dotnet@main"),
            ("Build Release and run changed-behavior regressions before full suite", "Unreviewed focused regression"),
        })
        {
            AssertMutationRejected(original, replacement);
        }
    }

    /// <summary>Checks rejection of an unreviewed artifact-upload action pin.</summary>
    /// <param name="replacement">The unreviewed actions/upload-artifact reference substituted for the approved immutable action pin.</param>
    [Theory]
    [InlineData("actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a")]
    [InlineData("actions/upload-artifact@v7.0.1")]
    [InlineData("actions/upload-artifact@main # 043fb46d1a93c77aae656e7c1c64a875d1fc6a0a")]
    [InlineData("actions/upload-artifact@0000000000000000000000000000000000000000")]
    [InlineData("actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02")]
    public void BuildAndTest_RejectsUnreviewedArtifactUploadPin(string replacement)
    {
        AssertMutationRejected("actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9", replacement);
    }

    /// <summary>Checks rejection of missing or disabled artifact ZIP archiving.</summary>
    /// <param name="original">The enabled archive YAML line located in the existing evidence-upload step.</param>
    /// <param name="replacement">The disabled archive line or empty text substituted to verify rejection of non-ZIP evidence uploads.</param>
    [Theory]
    [InlineData("          archive: true", "          archive: false")]
    [InlineData("          archive: true", "")]
    public void BuildAndTest_RejectsMissingOrDisabledZipArchive(string original, string replacement)
    {
        AssertMutationRejected(original, replacement);
    }

    /// <summary>Checks rejection of changes to the scaffold guard boundary.</summary>
    /// <param name="original">The reviewed scaffold-guard shell, script path, or evidence-output path located in the workflow.</param>
    /// <param name="replacement">The altered shell, script path, or evidence destination that the structural contract must reject.</param>
    [Theory]
    [InlineData("shell: pwsh", "shell: bash")]
    [InlineData("./tooling/Test-CareerScaffoldContract.ps1", "./tooling/UnreviewedScaffold.ps1")]
    [InlineData("./runner-results/career-scaffold-orchestration.json", "./discarded-scaffold-evidence.json")]
    public void BuildAndTest_RejectsAlteredScaffoldGuardBoundary(string original, string replacement)
    {
        AssertMutationRejected(original, replacement);
    }

    /// <summary>Checks that a comment pin cannot authorize a shared action's main branch.</summary>
    [Fact]
    public void BuildAndTest_RejectsSharedActionMainWithPinnedShaComment()
    {
        AssertMutationRejected(
            "MALIEV-Co-Ltd/Legacy.Maliev.Workflows/actions/dotnet-validate@e3a6093324a24968876782153286f52db8b29fd8",
            "MALIEV-Co-Ltd/Legacy.Maliev.Workflows/actions/dotnet-validate@main # e3a6093324a24968876782153286f52db8b29fd8");
    }

    /// <summary>Checks that commented dependency hashes do not satisfy checkout pinning.</summary>
    [Fact]
    public void BuildAndTest_RejectsCommentedDependencySha()
    {
        AssertMutationRejected(
            "ref: c40a7f82cea347b949444dcd7fb730f2b8dc3c0e",
            "ref: main # c40a7f82cea347b949444dcd7fb730f2b8dc3c0e");
    }

    /// <summary>Checks that the API references only the legacy ServiceDefaults project.</summary>
    [Fact]
    public void ApiProject_UsesOnlyLegacyServiceDefaults()
    {
        Assert.Contains("Legacy.Maliev.ServiceDefaults", ApiProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Maliev.Aspire\\Maliev.Aspire.ServiceDefaults", ApiProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Include=\"Maliev.Aspire.ServiceDefaults\"", ApiProject, StringComparison.Ordinal);
    }

    /// <summary>Checks shared failure middleware without introducing a local exception handler.</summary>
    [Fact]
    public void SourceF064_UsesSharedFailureMiddlewareWithoutLocalExceptionHandler()
    {
        var program = File.ReadAllText(FindRepositoryFile("Legacy.Maliev.CareerService.Api", "Program.cs"));
        var publisher = File.ReadAllText(FindRepositoryFile(".github", "workflows", "publish-image.yml"));

        Assert.Contains("AddStandardMiddleware(", program, StringComparison.Ordinal);
        Assert.Contains("UseStandardMiddleware()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("UseExceptionHandler(", program, StringComparison.Ordinal);
        Assert.Contains("ref: c40a7f82cea347b949444dcd7fb730f2b8dc3c0e", Workflow, StringComparison.Ordinal);
        Assert.Contains("legacy-service-defaults-ref: c40a7f82cea347b949444dcd7fb730f2b8dc3c0e", publisher,
            StringComparison.Ordinal);
    }

    /// <summary>Checks that EF design dependencies belong only to the Data project.</summary>
    [Fact]
    public void EfDesignDependency_IsOwnedByDataProjectOnly()
    {
        var project = XDocument.Parse(ApiProject);
        var enabled = Assert.Single(project.Descendants("EnableCareerScaffoldDesignTime"));
        Assert.Equal("false", enabled.Value);
        Assert.Equal("'$(EnableCareerScaffoldDesignTime)' == ''", enabled.Attribute("Condition")?.Value);
        var reference = Assert.Single(project.Descendants("PackageReference"),
            element => (string?)element.Attribute("Include") == "Microsoft.EntityFrameworkCore.Design");
        Assert.Equal("'$(EnableCareerScaffoldDesignTime)' == 'true'", reference.Attribute("Condition")?.Value);
        Assert.Equal("10.0.14", reference.Attribute("Version")?.Value);
        Assert.Equal("all", reference.Element("PrivateAssets")?.Value);
        Assert.Contains("Microsoft.EntityFrameworkCore.Design", DataProject, StringComparison.Ordinal);
    }

    /// <summary>Checks the API's EF runtime references match the Data project dependency version.</summary>
    [Fact]
    public void EfRuntimeDependencies_AreCoordinatedWithData()
    {
        var api = XDocument.Parse(ApiProject);
        var data = XDocument.Parse(DataProject);
        var design = Assert.Single(data.Descendants("PackageReference"),
            element => (string?)element.Attribute("Include") == "Microsoft.EntityFrameworkCore.Design");
        foreach (var name in new[] { "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Relational" })
        {
            var reference = Assert.Single(api.Descendants("PackageReference"),
                element => (string?)element.Attribute("Include") == name);
            Assert.Equal(design.Attribute("Version")?.Value, reference.Attribute("Version")?.Value);
        }
    }

    /// <summary>Checks rejection of job-level permission escalation.</summary>
    [Fact]
    public void BuildAndTest_RejectsJobPermissionEscalation()
    {
        AssertMutationRejected(
            "  validate:\n    name: validate",
            "  validate:\n    permissions:\n      contents: write\n    name: validate");
    }

    /// <summary>Checks rejection of secret references throughout the workflow.</summary>
    [Fact]
    public void BuildAndTest_RejectsSecretReferenceAnywhere()
    {
        var mutated = $"{Workflow}\n# ${{{{ secrets.X }}}}\n";

        Assert.Throws<InvalidOperationException>(() => WorkflowContractValidator.Validate(mutated));
    }

    /// <summary>Checks rejection of secret expressions in job environment values.</summary>
    /// <param name="expression">The dotted or bracketed GitHub secrets expression injected into the validate job environment to verify rejection.</param>
    [Theory]
    [InlineData("${{secrets.X}}")]
    [InlineData("${{ secrets['X'] }}")]
    public void BuildAndTest_RejectsSecretExpressionInJobEnvironment(string expression)
    {
        AssertMutationRejected(
            "    env:\n      MalievWorkspaceRoot: ${{ github.workspace }}/.dependencies",
            $"    env:\n      MalievWorkspaceRoot: ${{{{ github.workspace }}}}/.dependencies\n      REVIEW_TOKEN: {expression}");
    }

    /// <summary>Checks rejection of restore commands obscured with whitespace.</summary>
    [Fact]
    public void BuildAndTest_RejectsWhitespaceObfuscatedRestoreCommand()
    {
        AssertMutationRejected(
            "          solution: Legacy.Maliev.CareerService.slnx",
            "          solution: Legacy.Maliev.CareerService.slnx\n      - run: dotnet  restore Legacy.Maliev.CareerService.slnx");
    }

    /// <summary>Checks rejection of missing local dependency opt-in.</summary>
    [Fact]
    public void BuildAndTest_RejectsMissingLocalDependencyOptIn()
    {
        AssertMutationRejected(
            "          use-local-maliev-dependencies: 'true'\n",
            string.Empty);
    }

    /// <summary>Checks rejection of reserved GitHub Actions environment overrides.</summary>
    [Fact]
    public void BuildAndTest_RejectsReservedGitHubActionsOverride()
    {
        AssertMutationRejected(
            "          use-local-maliev-dependencies: 'true'\n",
            "          use-local-maliev-dependencies: 'true'\n        env:\n          GITHUB_ACTIONS: 'false'\n");
    }

    private static void AssertMutationRejected(string original, string replacement)
    {
        Assert.Contains(original, Workflow, StringComparison.Ordinal);
        var mutated = Workflow.Replace(original, replacement, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => WorkflowContractValidator.Validate(mutated));
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{Path.Combine(segments)}'.");
    }
}

internal static partial class WorkflowContractValidator
{
    private const string CheckoutAction = "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1";
    private const string SharedValidationAction = "MALIEV-Co-Ltd/Legacy.Maliev.Workflows/actions/dotnet-validate@e3a6093324a24968876782153286f52db8b29fd8";

    private const string FocusSetupAction = "actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68";
    private const string FocusStepName = "Build Release and run changed-behavior regressions before full suite";
    private const string MemoryAdmissionCommand = """
        set -euo pipefail
        python3 -B -c 'from pathlib import Path; [compile((Path("scripts")/name).read_bytes(), name, "exec") for name in ("career_required_ci_preflight.py", "test_career_required_ci_preflight.py")]'
        (cd scripts && timeout --signal=TERM --kill-after=5s 30s python3 -B -m unittest test_career_required_ci_preflight test_career_ci_candidate test_career_offer_detail_focus -v)
        python3 -B scripts/career_required_ci_preflight.py | tee "$RUNNER_TEMP/career-sdk-memory-preflight.json"
        """;
    private const string FocusCommand = """
        set -euo pipefail
        export GITHUB_ACTIONS=false
        mkdir -p runner-results/focus24
        python3 -B scripts/career_required_ci_preflight.py | tee "$RUNNER_TEMP/career-restore-memory.json"
        timeout --signal=TERM --kill-after=5s 300s dotnet restore Legacy.Maliev.CareerService.slnx 2>&1 | tee runner-results/focus24/restore.log
        python3 -B scripts/career_required_ci_preflight.py | tee "$RUNNER_TEMP/career-build-memory.json"
        timeout --signal=TERM --kill-after=5s 300s dotnet build Legacy.Maliev.CareerService.slnx --configuration Release --no-restore --warnaserror 2>&1 | tee runner-results/focus24/build.log
        grep -Eq '^[[:space:]]*0 Warning\(s\)[[:space:]]*$' runner-results/focus24/build.log
        grep -Eq '^[[:space:]]*0 Error\(s\)[[:space:]]*$' runner-results/focus24/build.log
        python3 -B scripts/career_required_ci_preflight.py | tee runner-results/focus24/memory.json
        VSTestCollect='' timeout --signal=TERM --kill-after=5s 300s dotnet test Legacy.Maliev.CareerService.Tests/Legacy.Maliev.CareerService.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~Update_ChangesExistingLevelAndRefreshesPublicProjectionWithoutChangingEitherPrincipalOrSurvivor|FullyQualifiedName~SelectedOfferDeletion_PreservesSharedLevelsAndSurvivorsAndRecomputesGlobalOpenStatus|FullyQualifiedName~LevelGraph_|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests' --logger 'trx;LogFileName=career-focus24.trx' --results-directory runner-results/focus24 2>&1 | tee runner-results/focus24/test.log
        grep -Eq 'Failed:[[:space:]]+0, Passed:[[:space:]]+26, Skipped:[[:space:]]+0, Total:[[:space:]]+26,' runner-results/focus24/test.log
        python3 -B scripts/career_direct_source_focus.py retained runner-results/focus24 | tee runner-results/focus24/identity-gate.json
        mkdir -p runner-results/original-boundary
        python3 -B scripts/career_required_ci_preflight.py | tee runner-results/original-boundary/memory.json
        VSTestCollect='' timeout --signal=TERM --kill-after=5s 300s dotnet test Legacy.Maliev.CareerService.Tests/Legacy.Maliev.CareerService.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalBoundaryRetentionHttpTests' --logger 'trx;LogFileName=career-original-boundary.trx' --results-directory runner-results/original-boundary 2>&1 | tee runner-results/original-boundary/test.log
        grep -Eq 'Failed:[[:space:]]+0, Passed:[[:space:]]+6, Skipped:[[:space:]]+0, Total:[[:space:]]+6,' runner-results/original-boundary/test.log
        python3 -B scripts/career_direct_source_focus.py boundary runner-results/original-boundary | tee runner-results/original-boundary/identity-gate.json
        mkdir -p runner-results/pagination-wire
        python3 -B scripts/career_required_ci_preflight.py | tee runner-results/pagination-wire/memory.json
        VSTestCollect='' timeout --signal=TERM --kill-after=5s 300s dotnet test Legacy.Maliev.CareerService.Tests/Legacy.Maliev.CareerService.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalPaginationWireHttpTests|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureNativeResourceTests' --logger 'trx;LogFileName=career-pagination-wire.trx' --results-directory runner-results/pagination-wire 2>&1 | tee runner-results/pagination-wire/test.log
        grep -Eq 'Failed:[[:space:]]+0, Passed:[[:space:]]+4, Skipped:[[:space:]]+0, Total:[[:space:]]+4,' runner-results/pagination-wire/test.log
        python3 -B scripts/career_direct_source_focus.py wire runner-results/pagination-wire | tee runner-results/pagination-wire/identity-gate.json
        mkdir -p runner-results/offer-detail
        python3 -B scripts/career_required_ci_preflight.py | tee runner-results/offer-detail/memory.json
        VSTestCollect='' timeout --signal=TERM --kill-after=5s 300s dotnet test Legacy.Maliev.CareerService.Tests/Legacy.Maliev.CareerService.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOfferDetailSourceHttpTests' --logger 'trx;LogFileName=career-offer-detail.trx' --results-directory runner-results/offer-detail 2>&1 | tee runner-results/offer-detail/test.log
        grep -Eq 'Failed:[[:space:]]+0, Passed:[[:space:]]+5, Skipped:[[:space:]]+0, Total:[[:space:]]+5,' runner-results/offer-detail/test.log
        python3 -B scripts/career_offer_detail_focus.py runner-results/offer-detail | tee runner-results/offer-detail/identity-gate.json
        """;

    private const string AuditCommand = """
        set -euo pipefail
        export GITHUB_ACTIONS=false
        python3 -B scripts/career_required_ci_preflight.py | tee "$RUNNER_TEMP/career-audit-memory.json"
        timeout --signal=TERM --kill-after=5s 10s dotnet --version | python3 -c 'import json,sys; json.dump({"sdkVersion":sys.stdin.read().strip()},sys.stdout)' > "$RUNNER_TEMP/career-candidate-package-audit-sdk.json"
        timeout --signal=TERM --kill-after=5s 120s dotnet list Legacy.Maliev.CareerService.slnx package --vulnerable --include-transitive --no-restore --format json > "$RUNNER_TEMP/career-candidate-package-audit.json"
        python3 -B scripts/career_ci_candidate.py audit "$RUNNER_TEMP/career-candidate-package-audit.json" | tee "$RUNNER_TEMP/career-candidate-package-audit-gate.json"
        """;

    public static void Validate(string workflow)
    {
        if (SecretExpression().IsMatch(workflow))
        {
            throw new InvalidOperationException("Workflow must not reference secrets.");
        }

        var yaml = new YamlStream();
        try
        {
            yaml.Load(new StringReader(workflow));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Workflow must be valid YAML.", exception);
        }

        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidOperationException("Workflow must contain exactly one mapping document.");
        }

        var workflowPermissions = RequireExactReadOnlyPermissions(RequireMapping(root, "permissions"), "workflow");
        var jobs = RequireMapping(root, "jobs");
        if (jobs.Children.Count != 1)
        {
            throw new InvalidOperationException("Workflow must define only the validate job.");
        }

        var validateJob = RequireMapping(jobs, "validate");
        var jobPermissionsNode = GetOptional(validateJob, "permissions");
        var effectiveJobPermissions = jobPermissionsNode is null
            ? workflowPermissions
            : RequireExactReadOnlyPermissions(RequireMapping(jobPermissionsNode, "jobs.validate.permissions"), "validate job");
        if (!effectiveJobPermissions.SequenceEqual(workflowPermissions, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Validate job permissions must not differ from workflow permissions.");
        }

        RequireScalarValue(validateJob, "name", "validate");
        RequireScalarValue(validateJob, "timeout-minutes", "30");
        var steps = RequireSequence(validateJob, "steps");
        if (steps.Children.Count != 12)
        {
            throw new InvalidOperationException("Validate job must preserve the seven original steps, two reviewed build-first focus steps, and the retained physical-memory admission and complete JSON audit steps.");
        }

        var admission = RequireMapping(steps.Children[3], "physical-memory admission");
        if (!admission.Children.Keys.Select(RequireScalar).ToHashSet(StringComparer.Ordinal).SetEquals(["name", "shell", "run"]))
        {
            throw new InvalidOperationException("Memory admission must preserve its exact reviewed command.");
        }
        RequireScalarValue(admission, "name", "Check fresh physical memory before SDK setup");
        RequireScalarValue(admission, "shell", "bash");
        RequireScalarValue(admission, "run", MemoryAdmissionCommand.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");

        var focusSetup = RequireMapping(steps.Children[4], "focused SDK setup");
        var setupKeys = focusSetup.Children.Keys.Select(RequireScalar).ToHashSet(StringComparer.Ordinal);
        if (!setupKeys.SetEquals(["name", "uses", "timeout-minutes", "with"]))
        {
            throw new InvalidOperationException("Focused SDK setup must contain only its reviewed inputs and timeout.");
        }
        RequireScalarValue(focusSetup, "name", "Set up .NET for build-first focused regression");
        RequireScalarValue(focusSetup, "uses", FocusSetupAction);
        RequireScalarValue(focusSetup, "timeout-minutes", "5");
        var setupInputs = RequireMapping(focusSetup, "with");
        if (setupInputs.Children.Count != 1)
        {
            throw new InvalidOperationException("Focused SDK setup must contain only dotnet-version.");
        }
        RequireScalarValue(setupInputs, "dotnet-version", "10.0.x");
        var focus = RequireMapping(steps.Children[5], "build-first focused regression");
        var focusKeys = focus.Children.Keys.Select(RequireScalar).ToHashSet(StringComparer.Ordinal);
        if (!focusKeys.SetEquals(["name", "shell", "timeout-minutes", "run"]))
        {
            throw new InvalidOperationException("Focused regression must contain only the reviewed command and timeout.");
        }
        RequireScalarValue(focus, "name", FocusStepName);
        RequireScalarValue(focus, "shell", "bash");
        RequireScalarValue(focus, "timeout-minutes", "20");
        var focusRun = RequireScalar(GetRequired(focus, "run"));
        if (!string.Equals(focusRun.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n'),
            FocusCommand.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Focused regression must preserve exact Release zero-warning/error and original twenty-four controls plus two bounded-observation and four pagination/native plus six original-boundary controls.");
        }
        var sharedAdmission = RequireMapping(steps.Children[6], "shared-phase admission");
        if (sharedAdmission.Children.Count != 3) throw new InvalidOperationException("Exact shared admission required.");
        RequireScalarValue(sharedAdmission, "name", "Check fresh memory before shared validation");
        RequireScalarValue(sharedAdmission, "shell", "bash");
        RequireScalarValue(sharedAdmission, "run", "python3 -B scripts/career_required_ci_preflight.py | tee \"$RUNNER_TEMP/career-shared-memory.json\"");
        var audit = RequireMapping(steps.Children[8], "complete package audit");
        if (audit.Children.Count != 4) throw new InvalidOperationException("Exact bounded JSON audit required.");
        RequireScalarValue(audit, "name", "Verify complete JSON package audit");
        RequireScalarValue(audit, "shell", "bash");
        RequireScalarValue(audit, "timeout-minutes", "3");
        RequireScalarValue(audit, "run", AuditCommand.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
        RejectDuplicatedValidationActionsAndCommands(jobs, focusSetup, focus, audit);

        var environment = RequireMapping(validateJob, "env");
        if (environment.Children.Count != 5)
        {
            throw new InvalidOperationException("Validate environment must contain only dependency root, evidence properties, and explicit fixture ownership.");
        }

        RequireScalarValue(environment, "MalievWorkspaceRoot", "${{ github.workspace }}/.dependencies");
        RequireScalarValue(environment, "VSTestCollect", "XPlat Code Coverage");
        RequireScalarValue(environment, "VSTestLogger", "trx");
        RequireScalarValue(environment, "MALIEV_TEST_RESOURCE_OWNER", "career-${{ github.run_id }}-${{ github.run_attempt }}");
        RequireScalarValue(environment, "VSTestResultsDirectory", "${{ github.workspace }}/runner-results");

        var scaffold = RequireMapping(steps.Children[9], "scaffold guard");
        if (scaffold.Children.Count != 3)
        {
            throw new InvalidOperationException("Scaffold guard must contain only name, shell, and run.");
        }

        RequireScalarValue(scaffold, "name", "Validate safe scaffold orchestration");
        RequireScalarValue(scaffold, "shell", "pwsh");
        RequireScalarValue(scaffold, "run", "./tooling/Test-CareerScaffoldContract.ps1 -EvidencePath ./runner-results/career-scaffold-orchestration.json");
        var gate = RequireMapping(steps.Children[10], "coverage gate");
        if (gate.Children.Count != 2)
        {
            throw new InvalidOperationException("Coverage gate must contain only name and run.");
        }

        RequireScalarValue(gate, "name", "Gate owned production coverage");
        RequireScalarValue(gate, "run", "python3 scripts/verify-runner-coverage.py runner-results");
        var evidence = RequireMapping(steps.Children[11], "evidence upload");
        if (evidence.Children.Count != 4)
        {
            throw new InvalidOperationException("Evidence upload must contain exactly name, if, uses and with.");
        }

        RequireScalarValue(evidence, "name", "Preserve validation evidence");
        RequireScalarValue(evidence, "if", "always()");
        RequireScalarValue(evidence, "uses", "actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9");
        var evidenceInputs = RequireMapping(evidence, "with");
        if (evidenceInputs.Children.Count != 5)
        {
            throw new InvalidOperationException("Evidence upload must have exactly five bounded inputs.");
        }

        RequireScalarValue(evidenceInputs, "name", "career-validation-${{ github.sha }}");
        RequireScalarValue(evidenceInputs, "path", "runner-results\n${{ runner.temp }}/career-sdk-memory-preflight.json\n${{ runner.temp }}/career-*-memory.json\n${{ runner.temp }}/career-candidate-package-audit*.json\n");
        RequireScalarValue(evidenceInputs, "if-no-files-found", "warn");
        RequireScalarValue(evidenceInputs, "retention-days", "7");
        RequireScalarValue(evidenceInputs, "archive", "true");

        ValidateStep(
            steps.Children[0],
            CheckoutAction,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["persist-credentials"] = "false",
            });
        ValidateStep(
            steps.Children[1],
            CheckoutAction,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["repository"] = "MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults",
                ["ref"] = "c40a7f82cea347b949444dcd7fb730f2b8dc3c0e",
                ["path"] = ".dependencies/Legacy.Maliev.ServiceDefaults",
                ["persist-credentials"] = "false",
            });
        ValidateStep(
            steps.Children[2],
            CheckoutAction,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["repository"] = "MALIEV-Co-Ltd/Legacy.Maliev.CompatibilityContracts",
                ["ref"] = "78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7",
                ["path"] = ".dependencies/Legacy.Maliev.CompatibilityContracts",
                ["persist-credentials"] = "false",
            });
        ValidateStep(
            steps.Children[7],
            SharedValidationAction,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["solution"] = "Legacy.Maliev.CareerService.slnx",
                ["use-local-maliev-dependencies"] = "true",
            });
    }

    private static IReadOnlyList<string> RequireExactReadOnlyPermissions(YamlMappingNode permissions, string scope)
    {
        if (permissions.Children.Count != 1)
        {
            throw new InvalidOperationException($"{scope} permissions must contain only contents: read.");
        }

        RequireScalarValue(permissions, "contents", "read");
        return ["contents:read"];
    }

    private static void ValidateStep(
        YamlNode node,
        string expectedAction,
        IReadOnlyDictionary<string, string> expectedInputs)
    {
        var step = RequireMapping(node, "workflow step");
        var allowedKeys = new HashSet<string>(["name", "uses", "with"], StringComparer.Ordinal);

        if (expectedAction == SharedValidationAction)
        {
            allowedKeys.Add("timeout-minutes");
            RequireScalarValue(step, "timeout-minutes", "10");
        }
        var actualKeys = step.Children.Keys.Select(RequireScalar).ToHashSet(StringComparer.Ordinal);
        if (!actualKeys.SetEquals(allowedKeys))
        {
            throw new InvalidOperationException("Each workflow step must contain exactly name, uses, and with.");
        }

        var name = RequireScalar(GetRequired(step, "name"));
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Workflow step name must not be empty.");
        }

        RequireScalarValue(step, "uses", expectedAction);
        var inputs = RequireMapping(step, "with");
        if (inputs.Children.Count != expectedInputs.Count)
        {
            throw new InvalidOperationException($"Action {expectedAction} has an unexpected input count.");
        }

        foreach (var expectedInput in expectedInputs)
        {
            RequireScalarValue(inputs, expectedInput.Key, expectedInput.Value);
        }

        var environment = GetOptional(step, "env");
        if (environment is not null)
        {
            throw new InvalidOperationException("Workflow action steps must not override reserved environment variables.");
        }

        if (expectedInputs.ContainsKey("use-local-maliev-dependencies"))
        {
            var useLocalDependencies = GetRequired(inputs, "use-local-maliev-dependencies") as YamlScalarNode;
            if (useLocalDependencies?.Style != ScalarStyle.SingleQuoted)
            {
                throw new InvalidOperationException("use-local-maliev-dependencies must use the single-quoted string value 'true'.");
            }
        }
    }

    private static void RejectDuplicatedValidationActionsAndCommands(
        YamlMappingNode jobs, YamlMappingNode reviewedSetup, YamlMappingNode reviewedFocus, YamlMappingNode reviewedAudit)
    {
        foreach (var jobNode in jobs.Children.Values.OfType<YamlMappingNode>())
        {
            var stepsNode = GetOptional(jobNode, "steps");
            if (stepsNode is not YamlSequenceNode steps)
            {
                continue;
            }

            foreach (var stepNode in steps.Children.OfType<YamlMappingNode>())
            {
                // Only the two exact reviewed nodes may perform focused setup and validation.
                if (ReferenceEquals(stepNode, reviewedSetup) || ReferenceEquals(stepNode, reviewedFocus) || ReferenceEquals(stepNode, reviewedAudit))
                {
                    continue;
                }
                if (GetOptional(stepNode, "uses") is YamlScalarNode usesNode)
                {
                    var action = usesNode.Value ?? string.Empty;
                    if (action.StartsWith("actions/setup-dotnet@", StringComparison.OrdinalIgnoreCase)
                        || action.StartsWith("actions/cache@", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Caller duplicates shared action {action}.");
                    }
                }

                if (GetOptional(stepNode, "run") is YamlScalarNode runNode)
                {
                    RejectDuplicatedDotNetCommand(runNode.Value ?? string.Empty);
                }
            }
        }
    }

    private static void RejectDuplicatedDotNetCommand(string command)
    {
        var tokens = command
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Trim('"', '\'', ';', '&', '|').ToLowerInvariant())
            .Where(token => token.Length > 0)
            .ToArray();

        for (var index = 0; index < tokens.Length - 1; index++)
        {
            if (!string.Equals(tokens[index], "dotnet", StringComparison.Ordinal))
            {
                continue;
            }

            var verb = tokens[index + 1];
            if (verb is "restore" or "build" or "test" or "format" or "list"
                || tokens[(index + 1)..].Any(token => token is "audit" or "--vulnerable"))
            {
                throw new InvalidOperationException($"Caller duplicates shared dotnet validation command: {verb}.");
            }
        }
    }

    private static YamlMappingNode RequireMapping(YamlMappingNode parent, string key) =>
        RequireMapping(GetRequired(parent, key), key);

    private static YamlMappingNode RequireMapping(YamlNode node, string description)
    {
        return node as YamlMappingNode
            ?? throw new InvalidOperationException($"{description} must be a mapping.");
    }

    private static YamlSequenceNode RequireSequence(YamlMappingNode parent, string key)
    {
        return GetRequired(parent, key) as YamlSequenceNode
            ?? throw new InvalidOperationException($"{key} must be a sequence.");
    }

    private static void RequireScalarValue(YamlMappingNode parent, string key, string expected)
    {
        var actual = RequireScalar(GetRequired(parent, key));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{key} must equal '{expected}', but was '{actual}'.");
        }
    }

    private static string RequireScalar(YamlNode node)
    {
        return (node as YamlScalarNode)?.Value
            ?? throw new InvalidOperationException("Expected a scalar YAML value.");
    }

    private static YamlNode GetRequired(YamlMappingNode parent, string key)
    {
        return GetOptional(parent, key)
            ?? throw new InvalidOperationException($"Missing required YAML key '{key}'.");
    }

    private static YamlNode? GetOptional(YamlMappingNode parent, string key)
    {
        foreach (var child in parent.Children)
        {
            if (child.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.Ordinal))
            {
                return child.Value;
            }
        }

        return null;
    }

    [GeneratedRegex(@"\$\{\{\s*secrets\s*(?:\.|\[)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretExpression();
}
