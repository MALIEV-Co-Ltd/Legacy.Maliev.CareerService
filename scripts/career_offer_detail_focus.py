"""Qualify the separate, source-authored five-case offer-detail native receipt."""
import json
import sys

from career_ci_candidate import verify_focused

ROSTER = frozenset({
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOfferDetailSourceHttpTests.SourceOfferDetail_LinkedStoredRowOmitsUnloadedLevelAndPreservesWholeGraph(nullableFields: False)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOfferDetailSourceHttpTests.SourceOfferDetail_LinkedStoredRowOmitsUnloadedLevelAndPreservesWholeGraph(nullableFields: True)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOfferDetailSourceHttpTests.SourceOfferDetail_MissingIdIs404WithoutMutationOrAuthorityCall(id: 0)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOfferDetailSourceHttpTests.SourceOfferDetail_MissingIdIs404WithoutMutationOrAuthorityCall(id: -1)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOfferDetailSourceHttpTests.SourceOfferDetail_MissingIdIs404WithoutMutationOrAuthorityCall(id: 2147483647)',
})


if __name__ == '__main__':
    if len(sys.argv) != 2:
        raise ValueError('Exactly one offer-detail results directory is required')
    print(json.dumps(verify_focused(sys.argv[1], ROSTER), sort_keys=True))
