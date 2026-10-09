"""Unpack the eighteen reviewed C# files in this disposable CI checkout only."""
import hashlib
import json
import os
import re
import sys
from pathlib import Path
import stat
import zipfile

POLICY_SHA256 = '94a3903a275ca9f68b26339f9b8edd36721d896cec8ba8a772f2f6aff343c66a'
# No qualified successor has been accepted or bound. Fail before SDK/scanners.
QUALIFIED_SCANNER_BINDING = None
ROOT = Path(__file__).resolve().parents[1]


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


ALLOWED_PATHS = {
    'Legacy.Maliev.CareerService.Application/Models/CareerModels.cs',
    'Legacy.Maliev.CareerService.Application/Services/CareerApplicationService.cs',
    'Legacy.Maliev.CareerService.Tests/Controllers/CareerLevelCollectionWireParityHttpTests.cs',
    'Legacy.Maliev.CareerService.Tests/Controllers/CareerLifecycleAcceptanceTests.cs',
    'Legacy.Maliev.CareerService.Tests/Controllers/CareerOfferLevelReassignmentHttpTests.cs',
    'Legacy.Maliev.CareerService.Tests/Controllers/CareerOriginalFilterAndNullUpdateHttpTests.cs',
    'Legacy.Maliev.CareerService.Tests/Controllers/CareerOriginalLevelReadHttpTests.cs',
    'Legacy.Maliev.CareerService.Tests/Controllers/CareerOriginalMutationParityHttpTests.cs',
    'Legacy.Maliev.CareerService.Tests/Controllers/CareerOriginalOfferReadHttpTests.cs',
    'Legacy.Maliev.CareerService.Tests/Controllers/CareerOriginalOpenPositionParityHttpTests.cs',
    'Legacy.Maliev.CareerService.Tests/Controllers/CareerOriginalPaginationParityHttpTests.cs',
    'Legacy.Maliev.CareerService.Tests/Controllers/CareerRouteFixtureLifetime.cs',
    'Legacy.Maliev.CareerService.Tests/Controllers/CareerRouteFixtureLifetimeTests.cs',
    'Legacy.Maliev.CareerService.Tests/Controllers/CareerRouteFixtureNativeResourceTests.cs',
    'Legacy.Maliev.CareerService.Tests/Controllers/CareerRouteHttpTests.cs',
    'Legacy.Maliev.CareerService.Tests/Controllers/CareerSelectedDeletionIsolationHttpTests.cs',
    'Legacy.Maliev.CareerService.Tests/Controllers/CareerSharedLevelHttpTests.cs',
    'Legacy.Maliev.CareerService.Tests/Controllers/CareerSourcePaginationCardinalityHttpTests.cs',
}


def _is_link(path):
    return path.is_symlink() or getattr(path, 'is_junction', lambda: False)()


def _safe_target(root, name):
    if _is_link(root) or not root.is_dir():
        raise ValueError('invalid isolated checkout')
    current=root
    for part in Path(name).parts[:-1]:
        current=current/part
        if _is_link(current) or not current.is_dir():
            raise ValueError('unsafe checkout ancestor')
    target=current/Path(name).name
    if _is_link(target) or (target.exists() and not target.is_file()):
        raise ValueError('unsafe checkout target')
    if not target.parent.resolve().is_relative_to(root.resolve()):
        raise ValueError('checkout target escapes isolated root')
    return target


def _replace_bytes(target, data):
    # The temporary is in the verified target directory, so replacement is atomic.
    # Its descriptor and exact path are retained and released on every outcome.
    import tempfile
    fd, name=tempfile.mkstemp(prefix='.career-candidate-',dir=target.parent)
    temporary=Path(name)
    try:
        stream=os.fdopen(fd,'wb');fd=-1
        with stream:
            if stream.write(data)!=len(data):
                raise OSError('incomplete candidate temporary write')
        os.chmod(temporary,stat.S_IMODE(target.stat().st_mode) if target.exists() else 0o644)
        os.replace(temporary,target)
    finally:
        if fd>=0:os.close(fd)
        if temporary.exists():temporary.unlink()


def hydrate(root, archive, policy):
    if _is_link(root) or not root.is_dir():
        raise ValueError('invalid isolated checkout')
    import io
    with archive.open('rb') as stream:raw=stream.read(131073)
    if len(raw)!=policy['archiveBytes'] or len(raw)>131072 or digest(raw)!=policy['archiveSha256']:
        raise ValueError('capsule pin mismatch')
    rows=policy['files']
    expected={r['path']:r for r in rows}
    if len(rows)!=18 or len(expected)!=18 or set(expected)!=ALLOWED_PATHS:
        raise ValueError('candidate scope mismatch')
    staged=[]
    with zipfile.ZipFile(io.BytesIO(raw)) as z:
        infos=z.infolist()
        if len(infos)!=18 or {i.filename for i in infos}!=set(expected):
            raise ValueError('capsule inventory mismatch')
        for info in infos:
            row=expected[info.filename]
            if (info.is_dir() or type(row['bytes']) is not int or row['bytes']<1 or
                info.file_size!=row['bytes'] or info.file_size>32768 or
                info.compress_type!=zipfile.ZIP_DEFLATED or info.flag_bits&1 or
                stat.S_ISLNK(info.external_attr>>16)):
                raise ValueError('unsafe capsule entry')
            # Each decoded entry stays under the unchanged 32KiB limit; the
            # complete reviewed policy pins its exact byte length and raw SHA.
            with z.open(info) as stream:data=stream.read(32769)
            if len(data)!=row['bytes'] or digest(data)!=row['sha256']:
                raise ValueError('source pin mismatch')
            target=_safe_target(root,info.filename)
            before=target.read_bytes() if target.exists() else None
            old=row['preimageSha256']
            if (old is None and before is not None) or (old is not None and
                (before is None or digest(before)!=old)):
                raise ValueError('checkout preimage mismatch')
            staged.append((info.filename,target,data,before))
    # Stage the entire coherent source before writing any file. Recheck each
    # destination immediately before replacing it and retain every preimage.
    attempted=[]
    try:
        for name,target,data,before in staged:
            _safe_target(root,name)
            current=target.read_bytes() if target.exists() else None
            if current!=before:raise ValueError('checkout changed during projection')
            attempted.append((name,target,data,before))
            _replace_bytes(target,data)
        for name,target,data,before in staged:
            _safe_target(root,name)
            if target.read_bytes()!=data:raise ValueError('candidate readback mismatch')
    except BaseException as primary:
        failures=[]
        for name,target,data,before in reversed(attempted):
            try:
                _safe_target(root,name)
                current=target.read_bytes() if target.exists() else None
                if current==before:continue
                if current!=data:raise ValueError('foreign change during rollback')
                if before is None:target.unlink()
                else:_replace_bytes(target,before)
            except BaseException:
                failures.append(name)
        for name,target,data,before in staged:
            try:
                _safe_target(root,name)
                if (target.read_bytes() if target.exists() else None)!=before:
                    failures.append(name)
            except BaseException:
                failures.append(name)
        if failures:
            raise ValueError('candidate projection failed; rollback incomplete: '+','.join(sorted(set(failures)))) from primary
        raise ValueError('candidate projection failed; rollback verified') from primary
    return {'verifiedCandidateFiles':expected,'archiveSha256':policy['archiveSha256'],
            'baseCommit':policy['baseCommit'],'nativeResults':0}



def require_qualified_scanners():
    binding=QUALIFIED_SCANNER_BINDING
    if (type(binding) is not dict or set(binding)!= {'revision','qualificationSha256','toolchain'} or
        any(type(v) is not str for v in binding.values()) or
        not re.fullmatch(r'[0-9a-f]{40}',binding.get('revision','')) or
        not re.fullmatch(r'[0-9a-f]{64}',binding.get('qualificationSha256','')) or
        binding.get('toolchain')!='go1.26.9'):
        raise ValueError('Qualified Go9 shared scanner successor is not bound; no SDK/scanner admission')
    return binding


def audit_no_vulnerabilities(raw):
    # Reuse the finite Career lane JSON audit contract: projects/frameworks
    # must be available, and both direct and transitive vulnerabilities fail.
    audit=json.loads(raw)
    if not isinstance(audit,dict) or not isinstance(audit.get('projects'),list) or not audit['projects']:
        raise ValueError('Vulnerability audit project data unavailable')
    expected={'Legacy.Maliev.CareerService.'+name+'.csproj'
              for name in ('Domain','Application','Api','Data','Tests')}
    actual=[]
    for project in audit['projects']:
        if not isinstance(project,dict) or not isinstance(project.get('path'),str):
            raise ValueError('Vulnerability audit project identity unavailable')
        actual.append(Path(project['path']).name)
        if not isinstance(project.get('frameworks'),list) or not project['frameworks']:
            raise ValueError('Vulnerability audit framework data unavailable')
        for framework in project['frameworks']:
            if (not isinstance(framework,dict) or
                type(framework.get('framework')) is not str or framework['framework']!='net10.0'):
                raise ValueError('Exact actual net10.0 audit framework identity required')
            for key in ('topLevelPackages','transitivePackages'):
                packages=framework.get(key,[])
                if not isinstance(packages,list) or any(not isinstance(p,dict) for p in packages):
                    raise ValueError('Invalid audit package data')
                for package in packages:
                    if 'vulnerabilities' in package:
                        vulnerabilities=package['vulnerabilities']
                        if (type(vulnerabilities) is not list or
                            any(type(v) is not dict or
                                type(v.get('severity')) is not str or not v['severity'] or
                                type(v.get('advisoryurl')) is not str or not v['advisoryurl']
                                for v in vulnerabilities)):
                            raise ValueError('Invalid audit vulnerability data')
                if any(package.get('vulnerabilities') for package in packages):
                    raise ValueError('Vulnerability audit found an affected dependency')
    if len(actual)!=5 or set(actual)!=expected:
        raise ValueError('Exact five-project Career audit inventory required')
    return {'auditedProjects':sorted(actual),'vulnerabilities':0,'nativeResultsProven':False}


FOCUSED_NAMESPACE = 'Legacy.Maliev.CareerService.Tests.Controllers.'
FOCUSED_ASSEMBLY = 'Legacy.Maliev.CareerService.Tests.dll'
UNIT_TEST_TYPE = '13cdc9d9-ddb5-4fa4-a97d-d965ccfc6d4b'
# Exact reviewed V4 authored methods and InlineData, not discovery forecasts.
FOCUSED_ROSTER = {
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerLevelCollectionWireParityHttpTests.CreateLevel_EmitsEmptyOffersAndLocationReadRetainsCollectionWithoutPersistingClientGraph',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerLevelCollectionWireParityHttpTests.StandaloneLevel_EmitsEmptyOffersEvenWhenLinked_JobProjectionOmitsOffers(linked: False)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerLevelCollectionWireParityHttpTests.StandaloneLevel_EmitsEmptyOffersEvenWhenLinked_JobProjectionOmitsOffers(linked: True)',

    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalOpenPositionParityHttpTests.OriginalFourOfferFilledCohort_PreservesPublicBoolean(hasOpenPositions: False)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalOpenPositionParityHttpTests.OriginalFourOfferFilledCohort_PreservesPublicBoolean(hasOpenPositions: True)',

    'Legacy.Maliev.CareerService.Tests.Controllers.CareerLifecycleAcceptanceTests.Delete_OriginalEmptyDatabaseIntMaxReturns404AfterFreshLiveDecision(kind: "level")',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerLifecycleAcceptanceTests.Delete_OriginalEmptyDatabaseIntMaxReturns404AfterFreshLiveDecision(kind: "offer")',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOfferLevelReassignmentHttpTests.Update_ChangesExistingLevelAndRefreshesPublicProjectionWithoutChangingEitherPrincipalOrSurvivor',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalFilterAndNullUpdateHttpTests.AnonymousFilter_ReturnsAllTwentyOriginalMatchesFromExactMixedCohortWithoutChangingRows(field: "description", distractors: 60)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalFilterAndNullUpdateHttpTests.AnonymousFilter_ReturnsAllTwentyOriginalMatchesFromExactMixedCohortWithoutChangingRows(field: "title", distractors: 81)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalFilterAndNullUpdateHttpTests.NullUpdateBody_IsBadRequestBeforeExistingOrMissingLookupAndPreservesPhysicalGraph(missing: False)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalFilterAndNullUpdateHttpTests.NullUpdateBody_IsBadRequestBeforeExistingOrMissingLookupAndPreservesPhysicalGraph(missing: True)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalLevelReadHttpTests.LevelExist_ShouldReturnRequestedLevelId',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalLevelReadHttpTests.LevelNotExist_ShouldReturnNotFound',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalMutationParityHttpTests.EmptyUpdateObject_MissingIntMaxReturns404WithoutPersistence(route: "Jobs", permission: "legacy-career.jobs.update")',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalMutationParityHttpTests.EmptyUpdateObject_MissingIntMaxReturns404WithoutPersistence(route: "jobs/levels", permission: "legacy-career.levels.update")',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalMutationParityHttpTests.LevelCreate_PersistsBothOriginalFieldsAndOverridesCallerCreatedDate',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalMutationParityHttpTests.LevelNullUpdateBody_IsBadRequestBeforeExistingOrMissingLookup(missing: False)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalMutationParityHttpTests.LevelNullUpdateBody_IsBadRequestBeforeExistingOrMissingLookup(missing: True)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalMutationParityHttpTests.LevelUpdate_PreservesCreatedDateAndPersistsDescriptionNameAndModifiedDate',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalMutationParityHttpTests.OfferCreate_PersistsAllOriginalFieldsAndOverridesCallerCreatedDate',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalOfferReadHttpTests.OfferExist_ShouldReturnRequestedOfferId',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalOfferReadHttpTests.OfferNotExist_ShouldReturnNotFound',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalPaginationParityHttpTests.ThousandAndOneOriginalDates_PreserveYearEndpoints(sort: "JobCreatedDate_Ascending", firstYear: 2000, lastYear: 3000)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalPaginationParityHttpTests.ThousandAndOneOriginalDates_PreserveYearEndpoints(sort: "JobCreatedDate_Descending", firstYear: 3000, lastYear: 2000)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalPaginationParityHttpTests.ThousandOriginalExplicitIds_PreserveIdEndpoints(sort: "JobId_Ascending", firstId: 1, lastId: 1000)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalPaginationParityHttpTests.ThousandOriginalExplicitIds_PreserveIdEndpoints(sort: "JobId_Descending", firstId: 1000, lastId: 1)',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalPaginationParityHttpTests.ThousandOriginalOffers_PreserveDefaultCardinalityFirstLastFlagsAndTenPages',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureLifetimeTests.NoncooperativeFactoryDisposal_RetainsPhaseAndStillAttemptsBothProviders',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureLifetimeTests.NoncooperativeLateStartup_SettlesBeforeProviderDisposal',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureLifetimeTests.NoncooperativeRedisDisposal_RetainsOriginalTaskAndPrimaryWithoutBlindRetry',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureLifetimeTests.NoncooperativeUnsettledStartup_RetainsTaskAndRefusesCleanupClaim',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureLifetimeTests.RedisDisposalFailure_StillDisposesPostgresAndRetainsBothFailures',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureLifetimeTests.RedisStartupTimeout_DisposesBothProviders',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureLifetimeTests.StartupFailure_AlwaysDisposesBothExistingProvidersAndRetainsPrimaryFailure(failureAt: "initialize")',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureLifetimeTests.StartupFailure_AlwaysDisposesBothExistingProvidersAndRetainsPrimaryFailure(failureAt: "postgres")',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureLifetimeTests.StartupFailure_AlwaysDisposesBothExistingProvidersAndRetainsPrimaryFailure(failureAt: "redis")',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureLifetimeTests.SuccessfulInitialization_KeepsProvidersUntilNormalFixtureDisposal',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureNativeResourceTests.ActualAlpineProvidersAndPg18Tmpfs_StartMigrateAndDisappearAfterDisposal',
    'Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureNativeResourceTests.ActualSecondProviderAcquiredThenStartupFails_BothExactIdsAreAbsent',
}
FOCUSED_FILTER = 'FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalOfferReadHttpTests|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalFilterAndNullUpdateHttpTests|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOfferLevelReassignmentHttpTests|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureLifetimeTests|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerRouteFixtureNativeResourceTests|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalLevelReadHttpTests|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalMutationParityHttpTests|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalPaginationParityHttpTests|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerLifecycleAcceptanceTests.Delete_OriginalEmptyDatabaseIntMaxReturns404AfterFreshLiveDecision|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerOriginalOpenPositionParityHttpTests|FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerLevelCollectionWireParityHttpTests'


def verify_focused_filter(expression):
    if expression!=FOCUSED_FILTER:
        raise ValueError('Exact reviewed40 filter required; broad lifecycle class forbidden')
    return expression


def verify_workflow_filter(path):
    expressions=re.findall(r"--filter '([^']+)'",Path(path).read_text(encoding='utf-8'))
    if len(expressions)!=1:raise ValueError('Exactly one reviewed focused filter required')
    return verify_focused_filter(expressions[0])


def verify_focused(root):
    import xml.etree.ElementTree as ET
    files=list(Path(root).rglob('*.trx'))
    if len(files)!=1:raise ValueError('Exactly one actual focused TRX required')
    run=ET.parse(files[0]).getroot()
    q=lambda name:'{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'+name
    if run.tag!=q('TestRun'):raise ValueError('Exact TRX namespace/root required')
    groups=[run.findall(q(name)) for name in ('TestDefinitions','Results','ResultSummary')]
    if any(len(nodes)!=1 for nodes in groups):raise ValueError('One definition/result/summary graph required')
    definitions,results,summary=[nodes[0] for nodes in groups]
    guid=re.compile(r'[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}')
    defs={};execution_ids=set();names=set()
    for node in definitions:
        ident=node.get('id','');name=node.get('name','')
        methods=node.findall(q('TestMethod'));executions=node.findall(q('Execution'))
        if (node.tag!=q('UnitTest') or not guid.fullmatch(ident) or ident.lower() in defs or
            name not in FOCUSED_ROSTER or name in names or len(methods)!=1 or len(executions)!=1):
            raise ValueError('Exact authored40 definitions and unique identities required')
        method=methods[0];execution=executions[0].get('id','')
        fullname=name.split('(',1)[0]
        cls,meth=fullname.rsplit('.',1)
        # Actual VSTest codeBase/storage may be absolute Linux/Windows paths.
        if (method.get('className')!=cls or method.get('name')!=meth or
            Path(method.get('codeBase','').replace('\\','/')).name!=FOCUSED_ASSEMBLY or
            Path(node.get('storage','').replace('\\','/')).name.lower()!=FOCUSED_ASSEMBLY.lower() or
            not guid.fullmatch(execution) or execution.lower() in execution_ids):
            raise ValueError('Exact Career assembly/class/method/execution wire required')
        defs[ident.lower()]=(name,execution.lower());execution_ids.add(execution.lower());names.add(name)
    if len(defs)!=40 or names!=FOCUSED_ROSTER:raise ValueError('Exact authored40 roster required')
    seen=set();seen_executions=set()
    for node in results:
        ident=node.get('testId','').lower();execution=node.get('executionId','').lower()
        if (node.tag!=q('UnitTestResult') or ident not in defs or ident in seen or
            execution in seen_executions or defs[ident]!=(node.get('testName'),execution) or
            node.get('testType','').lower()!=UNIT_TEST_TYPE or node.get('outcome')!='Passed' or
            node.findall('./'+q('Output')+'/'+q('ErrorInfo'))):
            raise ValueError('Actual focused result/definition identity or outcome mismatch')
        seen.add(ident);seen_executions.add(execution)
    if seen!=set(defs):raise ValueError('Incomplete actual focused result graph')
    counters=summary.findall(q('Counters'))
    if len(counters)!=1 or summary.get('outcome')!='Completed':raise ValueError('Exact successful TRX summary required')
    required={'total':40,'executed':40,'passed':40,'failed':0,'error':0,'notExecuted':0}
    optional={'completed','passedButRunAborted','disconnected','timeout','warning','aborted','inconclusive','notRunnable','pending','inProgress'}
    counts=counters[0].attrib
    if (not set(required)<=set(counts) or set(counts)-set(required)-optional or
        any(not re.fullmatch(r'\d+',v) for v in counts.values()) or
        any(counts[k]!=str(v) for k,v in required.items()) or
        any(int(v)!=0 for k,v in counts.items() if k not in required)):
        raise ValueError('Actual focused40 counters or infrastructure outcomes mismatch')
    if any(node.get('outcome') in {'Error','Warning','Failed','Aborted','Timeout'} for node in run.iter(q('RunInfo'))):
        raise ValueError('Run-level infrastructure failure refused')
    return {'actualFocusedPassed':40,'failed':0,'skipped':0,'trxSha256':digest(files[0].read_bytes()),
            'exactAuthoredRosterAndDefinitionJoin':True,'physicalRemovalEvidenceStillRequiresReview':True}


def main():
    if len(sys.argv)==3 and sys.argv[1]=='focused':
        print(json.dumps(verify_focused(sys.argv[2]),sort_keys=True))
        return
    if len(sys.argv)==3 and sys.argv[1]=='audit':
        print(json.dumps(audit_no_vulnerabilities(Path(sys.argv[2]).read_bytes()),sort_keys=True))
        return
    if len(sys.argv)!=1:raise ValueError('Closed candidate/audit invocation required')
    binding=require_qualified_scanners()
    verify_workflow_filter(ROOT/'.github/workflows/_build-and-test.yml')
    packet=ROOT/'scripts/career-ci-candidate'
    raw=(packet/'policy.json').read_bytes()
    if digest(raw)!=POLICY_SHA256:raise ValueError('reviewed policy pin mismatch')
    result=hydrate(ROOT,packet/'source.zip',json.loads(raw))
    result['qualifiedSharedScanners']=binding
    output=os.environ.get('GITHUB_OUTPUT')
    if not output:raise ValueError('Actual GitHub job output file required')
    with open(output,'a',encoding='utf-8') as stream:
        stream.write('qualified-shared-ref='+binding['revision']+'\n')
    print(json.dumps(result,sort_keys=True))


if __name__=='__main__':main()
