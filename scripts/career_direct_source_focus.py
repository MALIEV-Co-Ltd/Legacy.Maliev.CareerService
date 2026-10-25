"""Exact direct-source focus rosters; original40 archive validation stays separate."""
import json
from pathlib import Path
import re
import sys
from career_ci_candidate import verify_focused

# Retained24 are from run39962170415 artifact12505656469, focused TRX SHA256
# 1ba30be931628e8118cb38be5bf09847e00874e25bd23c665f43064e482dfae7 (five separate detail-source rows excluded).
# New2+4 entries are authored expectations and require actual hosted execution.
RETAINED_ROSTER = frozenset({
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.AbsentContainer_DoesNotSettleUnknownVolumesOrNativeCustody',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.BoundedObservation_DoesNotSettleStillOwnedStartupOrRelease(duringRelease: False)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.BoundedObservation_DoesNotSettleStillOwnedStartupOrRelease(duringRelease: True)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.CancellationProxy_DoesNotDetachActualLateWork',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.ClosedCreation_RejectsNewExternalResources',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.ContextOperationAndDisposalFailure_PreservesExactOperationPrimary',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.ContextOperationSuccess_DisposalFailureIsPrimary',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.ContextPrimaryAndEvidenceFailure_StillAttemptsDisposalAndPreservesPrimary',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.CooperativeTimeout_DoesNotClaimNoncooperativeWorkEnded',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.CreationIntent_PrecedesActualResourceCreation',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.Disposal_IsIdempotentAndReleasesEachRegistrationOnce',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.ExplicitContextRelease_IsNotRepeatedByFixtureCleanup',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.FirstReleaseFailure_DoesNotSkipOtherTiers',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.IndependentSameTierReleases_StartBeforeFirstJoin',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.Journal_SerializesFailureTypeWithoutCredentialOrMessage',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.LateCreation_IsJoinedAndReleasedDuringDispose',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.PrimaryFailure_IsSameObjectWhenCleanupFails',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.PrimaryFailure_IsSameObjectWhenEvidenceFails',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests.ResetOperations_AreSerializedAndActuallyJoined',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerLevelOffersSourceHttpTests.LevelGraph_DirectReadKeepsUnloadedOffersEmptyWithPersistedSiblings(detail: False)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerLevelOffersSourceHttpTests.LevelGraph_DirectReadKeepsUnloadedOffersEmptyWithPersistedSiblings(detail: True)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerLevelOffersSourceHttpTests.LevelGraph_PaginatedSiblingsOmitNestedOffersAndPreserveWholeRows(index: 1)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerLevelOffersSourceHttpTests.LevelGraph_PaginatedSiblingsOmitNestedOffersAndPreserveWholeRows(index: 2)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteHttpTests.Update_ChangesExistingLevelAndRefreshesPublicProjectionWithoutChangingEitherPrincipalOrSurvivor',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerSelectedDeletionIsolationHttpTests.SelectedOfferDeletion_PreservesSharedLevelsAndSurvivorsAndRecomputesGlobalOpenStatus(otherOpen: False)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerSelectedDeletionIsolationHttpTests.SelectedOfferDeletion_PreservesSharedLevelsAndSurvivorsAndRecomputesGlobalOpenStatus(otherOpen: True)',
})
WIRE_ROSTER = frozenset({
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalPaginationWireHttpTests.AnonymousFilter_ReturnsAllTwentyOriginalMatchesFromExactMixedCohortWithoutChangingRows(field: "description", distractors: 60)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalPaginationWireHttpTests.AnonymousFilter_ReturnsAllTwentyOriginalMatchesFromExactMixedCohortWithoutChangingRows(field: "title", distractors: 81)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureNativeResourceTests.ActualAlpineProvidersAndPg18Tmpfs_StartMigrateAndDisappearAfterDisposal',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureNativeResourceTests.ActualSecondProviderAcquiredThenStartupFails_BothExactIdsAreAbsent',
})
BOUNDARY_ROSTER = frozenset({
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalBoundaryRetentionHttpTests.Delete_OriginalEmptyDatabaseIntMaxReturns404AfterFreshLiveDecision(kind: "level")',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalBoundaryRetentionHttpTests.Delete_OriginalEmptyDatabaseIntMaxReturns404AfterFreshLiveDecision(kind: "offer")',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalBoundaryRetentionHttpTests.EmptyUpdateObject_MissingIntMaxReturns404WithoutPersistence(kind: "level")',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalBoundaryRetentionHttpTests.EmptyUpdateObject_MissingIntMaxReturns404WithoutPersistence(kind: "offer")',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalBoundaryRetentionHttpTests.OriginalFourOfferFilledCohort_PreservesPublicBoolean(hasOpenPositions: False)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalBoundaryRetentionHttpTests.OriginalFourOfferFilledCohort_PreservesPublicBoolean(hasOpenPositions: True)',
})
FILTERS = ('FullyQualifiedName~Update_ChangesExistingLevelAndRefreshesPublicProjectionWithoutChangingEitherPrincipalOrSurvivor|FullyQualifiedName~SelectedOfferDeletion_PreservesSharedLevelsAndSurvivorsAndRecomputesGlobalOpenStatus|FullyQualifiedName~LevelGraph_|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerFixtureLifetimeTests', 'FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalBoundaryRetentionHttpTests', 'FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalPaginationWireHttpTests|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureNativeResourceTests', 'FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOfferDetailSourceHttpTests')

def verify_workflow(path):
    expressions=re.findall(r"--filter '([^']+)'", Path(path).read_text(encoding="utf-8"))
    if tuple(expressions)!=FILTERS:
        raise ValueError("Exact current26, original6, pagination/native4 and offer-detail5 filters required")
    return expressions

def main():
    if len(sys.argv)!=3 or sys.argv[1] not in ("retained", "wire", "boundary"):
        raise ValueError("Closed direct-source focus invocation required")
    roster={"retained": RETAINED_ROSTER, "wire": WIRE_ROSTER, "boundary": BOUNDARY_ROSTER}[sys.argv[1]]
    print(json.dumps(verify_focused(sys.argv[2], roster), sort_keys=True))

if __name__=="__main__": main()
