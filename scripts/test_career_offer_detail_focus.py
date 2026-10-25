"""Portable guards only; synthetic TRX fixtures are never native execution proof."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import career_ci_candidate as candidate
import career_direct_source_focus as direct
import test_career_ci_candidate as existing

HERE = Path(__file__).resolve().parent
DETAIL_FILTER = 'FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOfferDetailSourceHttpTests'
EXPECTED = frozenset({
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOfferDetailSourceHttpTests.SourceOfferDetail_LinkedStoredRowOmitsUnloadedLevelAndPreservesWholeGraph(nullableFields: False)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOfferDetailSourceHttpTests.SourceOfferDetail_LinkedStoredRowOmitsUnloadedLevelAndPreservesWholeGraph(nullableFields: True)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOfferDetailSourceHttpTests.SourceOfferDetail_MissingIdIs404WithoutMutationOrAuthorityCall(id: 0)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOfferDetailSourceHttpTests.SourceOfferDetail_MissingIdIs404WithoutMutationOrAuthorityCall(id: -1)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOfferDetailSourceHttpTests.SourceOfferDetail_MissingIdIs404WithoutMutationOrAuthorityCall(id: 2147483647)',
})


class OfferDetailFocusControls(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)

    def helper(self):
        path = HERE / 'career_offer_detail_focus.py'
        self.assertTrue(path.is_file(), 'The separate five-case detail gate is missing')
        spec = importlib.util.spec_from_file_location('offer_detail_focus', path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module

    def test_detail_cli_qualifies_only_the_five_authored_native_identities(self):
        helper = self.helper()
        self.assertEqual(helper.ROSTER, EXPECTED)
        result = subprocess.run([sys.executable, '-B', str(HERE / 'career_offer_detail_focus.py'),
                                 str(existing.CandidateControls.trx(self, roster=EXPECTED))],
                                capture_output=True, text=True, timeout=10)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)['actualFocusedPassed'], 5)

    def test_detail_gate_rejects_missing_failed_foreign_and_changed_argument_rows(self):
        helper = self.helper()
        foreign = frozenset(name.replace('id: -1', 'id: -2') for name in EXPECTED)
        for options in ({'missing': True}, {'failed': True}, {'wrong_class': True}, {'roster': foreign}):
            with self.subTest(options=options), self.assertRaises(ValueError):
                values = {'roster': EXPECTED, **options}
                candidate.verify_focused(existing.CandidateControls.trx(self, **values), helper.ROSTER)

    def test_fourth_filter_is_required_without_changing_the_first_three(self):
        filters = (
            'FullyQualifiedName~Update_ChangesExistingLevelAndRefreshesPublicProjectionWithoutChangingEitherPrincipalOrSurvivor|FullyQualifiedName~SelectedOfferDeletion_PreservesSharedLevelsAndSurvivorsAndRecomputesGlobalOpenStatus|FullyQualifiedName~LevelGraph_|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests',
            'FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalBoundaryRetentionHttpTests',
            'FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalPaginationWireHttpTests|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureNativeResourceTests',
            DETAIL_FILTER,
        )
        path = self.root / 'workflow.yml'
        path.write_text('\n'.join("dotnet test --filter '" + value + "'" for value in filters))
        try:
            actual = tuple(direct.verify_workflow(path))
        except ValueError as failure:
            self.fail('The authored fourth detail filter is not accepted: ' + str(failure))
        self.assertEqual(actual, filters)

    def test_removed_or_mutated_detail_filter_is_refused(self):
        text = '\n'.join("dotnet test --filter '" + value + "'" for value in (*direct.FILTERS[:3], DETAIL_FILTER))
        for bad in (text.rsplit('\n', 1)[0], text.replace(DETAIL_FILTER, DETAIL_FILTER + '|FullyQualifiedName~ForeignTests')):
            path = self.root / 'bad.yml'
            path.write_text(bad)
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                direct.verify_workflow(path)


if __name__ == '__main__':
    unittest.main()
