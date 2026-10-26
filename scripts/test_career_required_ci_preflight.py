"""Pure CI wiring controls; these are not C# lifecycle execution or runtime proof."""
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch
import career_required_ci_preflight as gate

ROOT = Path(__file__).resolve().parents[1]


class RequiredCIControls(unittest.TestCase):
    def environment(self):
        return {'GITHUB_REPOSITORY':'MALIEV-Co-Ltd/Legacy.Maliev.CareerService',
                'GITHUB_RUN_ID':'123','GITHUB_RUN_ATTEMPT':'1','GITHUB_JOB':'validate','GITHUB_SHA':'a'*40}

    def test_established_four_gib_physical_floor(self):
        self.assertEqual(gate.PHYSICAL_FLOOR_KIB,4194304)
        self.assertTrue(gate.observation('MemFree: 4194304 kB\n',self.environment())['physicalFloorSatisfied'])

    def test_one_kib_below_floor_refuses(self):
        self.assertFalse(gate.observation('MemFree: 4194303 kB\n',self.environment())['physicalFloorSatisfied'])

    def test_reclaimable_available_is_not_physical_free(self):
        result=gate.observation('MemAvailable: 99999999 kB\nMemFree: 100 kB\n',self.environment())
        self.assertFalse(result['physicalFloorSatisfied'])

    def test_missing_duplicate_negative_or_wrong_units_refused(self):
        for text in ('MemAvailable: 4194304 kB\n','MemFree: 4194304 kB\nMemFree: 4194304 kB\n',
                     'MemFree: -1 kB\n','MemFree: 4194304 MB\n'):
            with self.assertRaises(ValueError):gate.observation(text,self.environment())

    def test_actual_job_and_attempt_identity_required(self):
        for key in self.environment():
            env=self.environment();env.pop(key)
            with self.assertRaises(ValueError):gate.observation('MemFree: 4194304 kB\n',env)
        for value in ('0','-1','not-a-run'):
            env=self.environment();env['GITHUB_RUN_ATTEMPT']=value
            with self.assertRaises(ValueError):gate.observation('MemFree: 4194304 kB\n',env)

    def test_report_retains_identity_and_no_native_result_claim(self):
        result=gate.observation('MemFree: 4194304 kB\n',self.environment())
        self.assertEqual((result['runId'],result['attempt'],result['job']),('123','1','validate'))
        self.assertEqual(result['jobTimeoutMinutes'],30)
        self.assertFalse(result['nativeResultsProven'])

    def test_cli_returns_nonzero_below_floor_and_records_actual_read(self):
        output=io.StringIO()
        with patch.object(Path,'read_text',return_value='MemFree: 100 kB\n') as read,patch.dict('os.environ',self.environment()),patch('sys.stdout',output):
            self.assertEqual(gate.main(),1)
        read.assert_called_once_with(encoding='ascii')
        self.assertEqual(json.loads(output.getvalue())['freePhysicalKiB'],100)

    def test_existing_job_timeout_preflight_before_sdk_and_always_evidence(self):
        text=(ROOT/'.github/workflows/_build-and-test.yml').read_text(encoding='utf-8')
        self.assertIn('timeout-minutes: 30',text)
        self.assertLess(text.index('Check fresh physical memory before SDK setup'),text.index('name: Set up .NET for build-first focused regression'))
        self.assertIn('python3 -B scripts/career_required_ci_preflight.py',text)
        self.assertIn('set -euo pipefail',text)
        self.assertIn('if: always()',text)
        self.assertIn('${{ runner.temp }}/career-sdk-memory-preflight.json',text)
        self.assertNotIn('GOTOOLCHAIN:',text)  # Preserve current main: scanner/toolchain remain owned by its exact pinned action.
        self.assertIn('dotnet-validate@e3a6093324a24968876782153286f52db8b29fd8',text)

    def test_fixture_explicit_memory_swap_cpu_pid_loopback_and_finite_provider_commands(self):
        text=(ROOT/'Legacy.Maliev.CareerService.Tests/Controllers/CareerFixtureContainerLimits.cs').read_text(encoding='utf-8')
        for exact in ('Memory = 1024L * 1024 * 1024','MemorySwap = 1024L * 1024 * 1024',
                      'Memory = 256L * 1024 * 1024','MemorySwap = 256L * 1024 * 1024',
                      'NanoCPUs = 750000000','NanoCPUs = 500000000','PidsLimit = 128','PidsLimit = 64',
                      'binding.HostIP = "127.0.0.1"','parameters.Labels["maliev.test.fixture"] = fixtureId',
                      'exec timeout 1200 docker-entrypoint.sh postgres','exec timeout 1200 redis-server',
                      'physicalKiB < 4194304','/var/lib/postgresql','/data'):
            self.assertIn(exact,text)
        self.assertNotIn('WithDockerEndpoint',text)
        for name in ('CareerRouteHttpTests.cs','CareerLifecycleFixture.cs'):
            fixture=(ROOT/'Legacy.Maliev.CareerService.Tests/Controllers'/name).read_text(encoding='utf-8')
            self.assertIn('CareerFixtureContainerLimits.Postgres(parameters, lifetime.FixtureId)',fixture)
            self.assertIn('CareerFixtureContainerLimits.Redis(parameters, lifetime.FixtureId)',fixture)
            self.assertEqual(fixture.count('CareerFixtureContainerLimits.RequireFreshAdmission();'),2)
            self.assertEqual(fixture.count('TimeSpan.FromSeconds(120), cancellation)'),2)
            self.assertNotIn('WithDockerEndpoint',fixture)


    def test_lifecycle_finally_cleanup_reverse_pair_timeout_and_primary_error_retention(self):
        text=(ROOT/'Legacy.Maliev.CareerService.Tests/Controllers/CareerFixtureLifetime.cs').read_text(encoding='utf-8')
        tests=(ROOT/'Legacy.Maliev.CareerService.Tests/Controllers/CareerFixtureLifetimeTests.cs').read_text(encoding='utf-8')
        self.assertIn('linked.CancelAfter(timeout)',text)
        self.assertIn('finally',text)
        self.assertIn('ExceptionDispatchInfo.Capture(primary).Throw()',text)
        self.assertIn('cleanupFailures.Add(error)',text)
        self.assertIn('group.Select(ReleaseOnce).ToArray()',text)
        self.assertIn('releases.GroupBy(item => item.Order).OrderBy(item => item.Key)',text)
        # Both independent providers are started before joining; a blocked Redis release cannot prevent PostgreSQL release.
        self.assertIn('IndependentSameTierReleases_StartBeforeFirstJoin',tests)
        self.assertIn('FirstReleaseFailure_DoesNotSkipOtherTiers',tests)
        self.assertIn('PrimaryFailure_IsSameObjectWhenCleanupFails',tests)
        self.assertIn('BoundedObservation_DoesNotSettleStillOwnedStartupOrRelease',tests)
        self.assertIn('cleanup.WaitAsync(TimeSpan.FromMilliseconds(30))',tests)

    def test_startup_task_settlement_precedes_disposal_and_unsettled_refuses_claim(self):
        text=(ROOT/'Legacy.Maliev.CareerService.Tests/Controllers/CareerFixtureLifetime.cs').read_text(encoding='utf-8')
        tests=(ROOT/'Legacy.Maliev.CareerService.Tests/Controllers/CareerFixtureLifetimeTests.cs').read_text(encoding='utf-8')
        self.assertIn('entry.Actual = ExecuteEntry',text)
        self.assertIn('CancellationTokenSource.CreateLinkedTokenSource(stop.Token, caller)',text)
        self.assertLess(text.index('await entry.Actual;'),text.index('var tasks = group.Select(ReleaseOnce)'))
        self.assertIn('pending.Where(item => !joined.Contains(item.Id))',text)
        self.assertIn('LateCreation_IsJoinedAndReleasedDuringDispose',tests)
        self.assertIn('CancellationProxy_DoesNotDetachActualLateWork',tests)
        self.assertIn('Assert.Same(cleanup, lifetime.DisposeAsync())',tests)
        self.assertIn('Assert.False(cleanup.IsCompleted)',tests)
        self.assertIn('Assert.DoesNotContain(events, item => item.Stage == "managed-terminal")',tests)
        self.assertIn('await Task.WhenAll(actual, cleanup).WaitAsync(Bound)',tests)

    def test_real_host_proof_uses_actual_inspection_and_exact_not_found(self):
        text=(ROOT/'Legacy.Maliev.CareerService.Tests/Controllers/CareerRouteFixtureNativeResourceTests.cs').read_text(encoding='utf-8')
        self.assertIn('InspectContainerAsync(id, cancellation)',text)
        self.assertIn('HttpStatusCode.NotFound, actual.StatusCode',text)
        self.assertIn('db.Database.CanConnectAsync',text)
        self.assertIn('fixture = new CareerRouteFixture(async cancellation =>',text)
        self.assertIn('redisId = fixture.RedisResource.Id',text)
        fixture=(ROOT/'Legacy.Maliev.CareerService.Tests/Controllers/CareerRouteHttpTests.cs').read_text(encoding='utf-8')
        self.assertLess(fixture.index('lifetime.ObserveOwned(redis)'),fixture.index('await afterProvidersStarted(cancellation)'))
        self.assertIn('fixture.DisposeAsync().WaitAsync(TimeSpan.FromSeconds(30))',text)
        self.assertIn('Assert.DoesNotContain(actual.Mounts, mount => mount.Type != "tmpfs")',text)
        self.assertIn('throw primary',text)
        self.assertNotIn('actual.Config.Env',text)


if __name__=='__main__':unittest.main()
