import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile
import career_ci_candidate as candidate

HERE=Path(__file__).resolve().parent
PACKET=HERE/'career-ci-candidate'


class CandidateControls(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup)
        self.root=Path(self.tmp.name)
        for row in json.loads((PACKET/'policy.json').read_bytes())['files']:
            (self.root/row['path']).parent.mkdir(parents=True,exist_ok=True)
        self.policy=json.loads((PACKET/'policy.json').read_bytes())
        # The actual V2 original fixture bytes are supplied for the source-only
        # self-test. Native CI's fresh checkout supplies these independently.
        self.preimages=[]
        for row in self.policy['files']:
            if row['preimageSha256'] is None:continue
            target=self.root/row['path']
            raw=('unchanged-test-preimage:'+row['path']).encode()
            target.write_bytes(raw);row['preimageSha256']=candidate.digest(raw)
            self.preimages.append(target)
        self.fixture=self.preimages[0]
        self.archive=PACKET/'source.zip'

    def refused_without_write(self,archive=None,policy=None):
        before={p.relative_to(self.root).as_posix():p.read_bytes() for p in self.root.rglob('*') if p.is_file()}
        with self.assertRaises(ValueError):candidate.hydrate(self.root,archive or self.archive,policy or self.policy)
        self.assertEqual(before,{p.relative_to(self.root).as_posix():p.read_bytes() for p in self.root.rglob('*') if p.is_file()})

    def test_exact_eighteen_file_projection_and_readback(self):
        result=candidate.hydrate(self.root,self.archive,self.policy)
        self.assertEqual(result['nativeResults'],0)
        for row in self.policy['files']:
            self.assertEqual(candidate.digest((self.root/row['path']).read_bytes()),row['sha256'])

    def test_archive_corruption_refused_before_any_write(self):
        path=self.root/'bad.zip';path.write_bytes(self.archive.read_bytes()+b'x')
        self.refused_without_write(path)

    def test_last_file_wrong_pin_prevents_first_file_write(self):
        self.policy['files'][-1]['sha256']='0'*64
        self.refused_without_write()

    def test_original_fixture_mismatch_preserved(self):
        self.fixture.write_bytes(b'foreign change')
        self.refused_without_write()

    def test_foreign_new_file_preserved(self):
        (self.root/next(r['path'] for r in self.policy['files'] if r['preimageSha256'] is None)).write_bytes(b'foreign source')
        self.refused_without_write()

    def test_parent_traversal_refused(self):
        self.policy['files'][-1]['path']='../outside.cs'
        self.refused_without_write()

    def test_duplicate_zip_entry_refused(self):
        path=self.root/'duplicate.zip'
        with zipfile.ZipFile(self.archive) as source,zipfile.ZipFile(path,'w') as target:
            for i in source.infolist():target.writestr(copy.copy(i),source.read(i))
            import warnings
            with warnings.catch_warnings():
                warnings.simplefilter('ignore')
                target.writestr(copy.copy(source.infolist()[-1]),source.read(source.infolist()[-1]))
        self.policy['archiveSha256']=candidate.digest(path.read_bytes())
        self.policy['archiveBytes']=path.stat().st_size
        self.refused_without_write(path)

    def test_actual_policy_pin_and_v7_file_inventory(self):
        self.assertEqual(candidate.digest((PACKET/'policy.json').read_bytes()),candidate.POLICY_SHA256)
        self.assertEqual(len(self.policy['files']),18)

    def test_existing_pr_job_calls_candidate_without_new_workflow(self):
        text=(HERE.parent/'.github/workflows/pr-validation.yml').read_text()
        self.assertIn('pull_request:',text)
        self.assertIn('uses: ./.github/workflows/_build-and-test.yml',text)
        self.assertIn('source-candidate: true',text)
        self.assertNotIn('workflow_dispatch:',text)

    def test_candidate_commands_have_required_order_and_full_suite_unfiltered(self):
        text=(HERE.parent/'.github/workflows/_build-and-test.yml').read_text()
        markers=['Verify and unpack reviewed candidate source','Check fresh physical memory before SDK setup',
                 'Set up .NET for candidate','Restore candidate','Strict Release build candidate first',
                 'Focused forty original lifecycle and real provider cases','Unfiltered full candidate suite',
                 'Candidate format and package audit','Gate owned production coverage']
        positions=[text.index('name: '+x) for x in markers]
        self.assertEqual(positions,sorted(positions))
        full=text.split('name: Unfiltered full candidate suite')[1].split('name: Candidate format')[0]
        self.assertNotIn('--filter',full)
        self.assertIn('-warnaserror',text)
        self.assertIn("--collect 'XPlat Code Coverage'",full)

    def test_normal_shared_route_preserved_and_security_pin_unchanged(self):
        text=(HERE.parent/'.github/workflows/_build-and-test.yml').read_text()
        self.assertIn('if: ${{ !inputs.source-candidate }}',text)
        self.assertIn('dotnet-validate@e3a6093324a24968876782153286f52db8b29fd8',text)
        self.assertIn('GOTOOLCHAIN: go1.26.8',text)
        self.assertIn('timeout-minutes: 30',text)
        self.assertIn('if: always()',text)

    def test_each_candidate_native_phase_has_fresh_floor_and_finite_timeout(self):
        text=(HERE.parent/'.github/workflows/_build-and-test.yml').read_text()
        for name in ('Restore candidate','Strict Release build candidate first',
                     'Focused forty original lifecycle and real provider cases',
                     'Unfiltered full candidate suite','Candidate format and package audit'):
            step=text.split('name: '+name)[1].split('      - name:')[0]
            self.assertIn('python3 -B scripts/career_required_ci_preflight.py',step)
            self.assertIn('timeout --signal=TERM --kill-after=5s',step)

    def audit(self):
        return {'projects':[{'path':'/checkout/Legacy.Maliev.CareerService.'+name+'.csproj',
                             'frameworks':[{'framework':'net10.0'}]}
                            for name in ('Domain','Application','Api','Data','Tests')]}

    def test_existing_json_audit_contract_accepts_all_five_clean_projects(self):
        result=candidate.audit_no_vulnerabilities(json.dumps(self.audit()))
        self.assertEqual(result['vulnerabilities'],0)
        self.assertEqual(len(result['auditedProjects']),5)

    def test_audit_rejects_actual_direct_and_transitive_vulnerabilities(self):
        for key in ('topLevelPackages','transitivePackages'):
            report=self.audit()
            report['projects'][-1]['frameworks'][0][key]=[{'id':'Affected','vulnerabilities':[{'severity':'High','advisoryurl':'https://example.invalid/advisory'}]}]
            with self.assertRaisesRegex(ValueError,'affected dependency'):
                candidate.audit_no_vulnerabilities(json.dumps(report))

    def test_audit_missing_project_or_framework_evidence_refused(self):
        for report in ({},[],{'projects':[]},{'projects':[{'path':'a.csproj','frameworks':[]}]}):
            with self.assertRaises(ValueError):candidate.audit_no_vulnerabilities(json.dumps(report))

    def test_audit_missing_duplicate_or_foreign_project_inventory_refused(self):
        for change in ('missing','duplicate','foreign'):
            report=self.audit()
            if change=='missing':report['projects'].pop()
            if change=='duplicate':report['projects'][-1]=copy.deepcopy(report['projects'][0])
            if change=='foreign':report['projects'][-1]['path']='Foreign.csproj'
            with self.assertRaisesRegex(ValueError,'five-project'):
                candidate.audit_no_vulnerabilities(json.dumps(report))

    def test_audit_malformed_package_data_refused(self):
        report=self.audit();report['projects'][0]['frameworks'][0]['topLevelPackages']=['invalid']
        with self.assertRaisesRegex(ValueError,'package data'):
            candidate.audit_no_vulnerabilities(json.dumps(report))

    def test_missing_qualified_successor_refuses_before_source_projection(self):
        with patch.object(candidate,'hydrate') as hydrate,patch('sys.argv',['career_ci_candidate.py']):
            with self.assertRaisesRegex(ValueError,'Qualified Go9'):candidate.main()
            hydrate.assert_not_called()
        self.assertIsNone(candidate.QUALIFIED_SCANNER_BINDING)

    def test_old_go8_binding_and_malformed_successor_refused(self):
        for binding in ({'revision':'a'*40,'qualificationSha256':'b'*64,'toolchain':'go1.26.8'},
                        {'revision':0,'qualificationSha256':'b'*64,'toolchain':'go1.26.9'},
                        {'revision':'a'*40,'toolchain':'go1.26.9'}):
            with patch.object(candidate,'QUALIFIED_SCANNER_BINDING',binding):
                with self.assertRaises(ValueError):candidate.require_qualified_scanners()

    def test_candidate_scanners_use_qualified_pin_output_and_go9(self):
        text=(HERE.parent/'.github/workflows/_build-and-test.yml').read_text()
        checkout=text.split('name: Check out exact existing shared scanners')[1].split('      - name:')[0]
        self.assertIn('steps.candidate.outputs.qualified-shared-ref',checkout)
        self.assertNotIn('e3a609',checkout)
        scan=text.split('name: Scan candidate using exact existing scanners')[1].split('      - name:')[0]
        self.assertIn('GOTOOLCHAIN: go1.26.9',scan)

    def test_audit_command_checks_json_after_exit_status_and_preserves_raw(self):
        text=(HERE.parent/'.github/workflows/_build-and-test.yml').read_text()
        step=text.split('name: Candidate format and package audit')[1].split('      - name:')[0]
        self.assertIn('--format json',step)
        self.assertIn('set -euo pipefail',step)
        self.assertIn('scripts/career_ci_candidate.py audit',step)
        self.assertIn('career-candidate-package-audit*.json',text)

    def test_missing_empty_wrong_or_nonstring_framework_identity_refused(self):
        for framework in ({},{'framework':''},{'framework':'net9.0'},{'framework':None},{'framework':10}):
            report=self.audit();report['projects'][0]['frameworks']=[framework]
            with self.assertRaisesRegex(ValueError,'framework identity'):
                candidate.audit_no_vulnerabilities(json.dumps(report))

    def test_malformed_vulnerability_shapes_refused_even_when_falsey(self):
        for value in (None,{},'',0,[None],[{}],[{'severity':'High'}],
                      [{'severity':'','advisoryurl':'https://example.invalid'}],
                      [{'severity':'High','advisoryurl':None}]):
            for key in ('topLevelPackages','transitivePackages'):
                report=self.audit()
                report['projects'][0]['frameworks'][0][key]=[{'id':'Package','vulnerabilities':value}]
                with self.assertRaisesRegex(ValueError,'vulnerability data'):
                    candidate.audit_no_vulnerabilities(json.dumps(report))

    def test_valid_clean_reports_allow_omitted_and_empty_package_arrays(self):
        for form in ('omitted','empty','empty-vulnerabilities'):
            report=self.audit()
            if form=='empty':report['projects'][0]['frameworks'][0].update(topLevelPackages=[],transitivePackages=[])
            if form=='empty-vulnerabilities':
                report['projects'][0]['frameworks'][0]['topLevelPackages']=[{'id':'Package','vulnerabilities':[]}]
            self.assertEqual(candidate.audit_no_vulnerabilities(json.dumps(report))['vulnerabilities'],0)

    def trx(self,failed=False,missing=False,wrong_class=False):
        import uuid
        import xml.etree.ElementTree as ET
        root=self.root/'focused';root.mkdir(exist_ok=True)
        q=lambda name:'{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'+name
        run=ET.Element(q('TestRun'));results=ET.SubElement(run,q('Results'))
        definitions=ET.SubElement(run,q('TestDefinitions'))
        for count,name in enumerate(sorted(candidate.FOCUSED_ROSTER),1):
            ident=str(uuid.UUID(int=count));execution=str(uuid.UUID(int=100+count))
            fullname=name.split('(',1)[0];cls,method=fullname.rsplit('.',1)
            node=ET.SubElement(definitions,q('UnitTest'),id=ident,name=name,storage='/work/'+candidate.FOCUSED_ASSEMBLY)
            ET.SubElement(node,q('Execution'),id=execution)
            ET.SubElement(node,q('TestMethod'),className=cls,name=method,codeBase='/work/'+candidate.FOCUSED_ASSEMBLY)
            if missing and count==40:continue
            ET.SubElement(results,q('UnitTestResult'),testId=ident,executionId=execution,
                testName='ForeignTests.Case' if wrong_class and count==40 else name,
                outcome='Failed' if failed and count==40 else 'Passed',testType=candidate.UNIT_TEST_TYPE)
        summary=ET.SubElement(run,q('ResultSummary'),outcome='Completed')
        ET.SubElement(summary,q('Counters'),total='40',executed='40',passed='40',failed='0',error='0',notExecuted='0')
        ET.ElementTree(run).write(root/'focused.trx',encoding='utf-8',xml_declaration=True)
        return root

    def test_focused_parser_contract_positive_fixture_is_not_runtime_proof(self):
        report=candidate.verify_focused(self.trx())
        self.assertEqual(report['actualFocusedPassed'],40)
        self.assertTrue(report['physicalRemovalEvidenceStillRequiresReview'])

    def test_focused_parser_rejects_failed_missing_or_foreign_case_despite_green_counters(self):
        for kwargs in ({'failed':True},{'missing':True},{'wrong_class':True}):
            with self.assertRaises(ValueError):candidate.verify_focused(self.trx(**kwargs))

    def test_focused_parser_requires_actual_receipt_and_counters(self):
        with self.assertRaises(ValueError):candidate.verify_focused(self.root)
        path=self.trx()/'focused.trx';raw=path.read_text();path.write_text(raw.replace('total="40"','total="39"'))
        with self.assertRaises(ValueError):candidate.verify_focused(path.parent)

    def test_full_coverage_gate_uses_unfiltered_full_directory(self):
        text=(HERE.parent/'.github/workflows/_build-and-test.yml').read_text()
        self.assertIn("inputs.source-candidate && 'runner-results/full' || 'runner-results'",text)
        self.assertIn('scripts/career_ci_candidate.py focused runner-results/focused',text)


    def test_focused_rejects_definition_identity_assembly_and_theory_mutations(self):
        import xml.etree.ElementTree as ET
        q=lambda name:'{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'+name
        mutations=(
            lambda run:run.remove(run.find(q('TestDefinitions'))),
            lambda run:run.find(q('TestDefinitions')).append(run.find(q('TestDefinitions'))[0]),
            lambda run:run.find(q('TestDefinitions'))[0].find(q('TestMethod')).set('className','Foreign.Namespace.Class'),
            lambda run:run.find(q('TestDefinitions'))[0].find(q('TestMethod')).set('codeBase','Foreign.Tests.dll'),
            lambda run:run.find(q('TestDefinitions'))[0].set('storage','Foreign.Tests.dll'),
            lambda run:run.find(q('TestDefinitions'))[0].find(q('TestMethod')).set('name','InventedMethod'),
            lambda run:run.find(q('TestDefinitions'))[0].find(q('Execution')).set('id',run.find(q('TestDefinitions'))[1].find(q('Execution')).get('id')),
            lambda run:run.find(q('TestDefinitions'))[0].set('id','not-a-guid'),
            lambda run:run.find(q('Results'))[0].set('executionId',run.find(q('Results'))[1].get('executionId')),
            lambda run:run.find(q('Results'))[0].set('testId',run.find(q('Results'))[1].get('testId')),
            lambda run:run.find(q('Results'))[0].set('testType','foreign'),
            lambda run:run.find(q('ResultSummary')).set('outcome','Failed'),
            lambda run:run.find(q('ResultSummary')).find(q('Counters')).set('aborted','1'),
            lambda run:ET.SubElement(run,q('RunInfo'),outcome='Error'),
            lambda run:ET.SubElement(ET.SubElement(run.find(q('Results'))[0],q('Output')),q('ErrorInfo')),
        )
        for index,mutate in enumerate(mutations):
            with self.subTest(mutation=index):
                path=self.trx()/'focused.trx';doc=ET.parse(path);mutate(doc.getroot());doc.write(path)
                with self.assertRaises(ValueError):candidate.verify_focused(path.parent)

    def test_focused_rejects_invented_theory_data_even_with_joined_definitions(self):
        import xml.etree.ElementTree as ET
        q=lambda name:'{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'+name
        path=self.trx()/'focused.trx';doc=ET.parse(path);run=doc.getroot()
        node=next(n for n in run.find(q('TestDefinitions')) if 'distractors: 60' in n.get('name',''))
        invented=node.get('name').replace('distractors: 60','distractors: 61');ident=node.get('id');node.set('name',invented)
        next(n for n in run.find(q('Results')) if n.get('testId')==ident).set('testName',invented)
        doc.write(path)
        with self.assertRaises(ValueError):candidate.verify_focused(path.parent)

    def test_workflow_uses_only_existing_career_scaffold_contract(self):
        text=(HERE.parent/'.github/workflows/_build-and-test.yml').read_text()
        self.assertEqual(text.count('./tooling/Test-CareerScaffoldContract.ps1'),1)
        self.assertNotIn('Test-EmployeeScaffoldContract.ps1',text)
        self.assertTrue((HERE.parent/'tooling/Test-CareerScaffoldContract.ps1').is_file())


    def test_previous_six_file_policy_cannot_hydrate_seven_file_binding(self):
        policy=copy.deepcopy(self.policy)
        policy['files']=[r for r in policy['files'] if not r['path'].endswith('CareerOriginalOfferReadHttpTests.cs')]
        self.refused_without_write(policy=policy)

    def test_old_seventeen_case_results_cannot_prove_nineteen(self):
        import xml.etree.ElementTree as ET
        q=lambda name:'{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'+name
        path=self.trx()/'focused.trx';doc=ET.parse(path);run=doc.getroot()
        for group in ('TestDefinitions','Results'):
            nodes=run.find(q(group))
            for node in list(nodes):
                if 'CareerOriginalOfferReadHttpTests.' in node.get('name',node.get('testName','')):nodes.remove(node)
        counters=run.find(q('ResultSummary')).find(q('Counters'))
        for name in ('total','executed','passed'):counters.set(name,'17')
        doc.write(path)
        with self.assertRaises(ValueError):candidate.verify_focused(path.parent)


    def test_both_actual_preimages_required_and_second_mismatch_prevents_all_writes(self):
        self.assertEqual(len(self.preimages),7)
        self.preimages[1].write_bytes(b'foreign second original')
        self.refused_without_write()

    def test_previous_seven_file_policy_refused_before_any_write(self):
        policy=copy.deepcopy(self.policy)
        added={'CareerLifecycleAcceptanceTests.cs','CareerOriginalLevelReadHttpTests.cs','CareerOriginalMutationParityHttpTests.cs','CareerOriginalPaginationParityHttpTests.cs','CareerOriginalOpenPositionParityHttpTests.cs','CareerModels.cs','CareerApplicationService.cs','CareerSelectedDeletionIsolationHttpTests.cs','CareerSharedLevelHttpTests.cs','CareerLevelCollectionWireParityHttpTests.cs','CareerSourcePaginationCardinalityHttpTests.cs'}
        policy['files']=[r for r in policy['files'] if Path(r['path']).name not in added]
        self.assertEqual(len(policy['files']),7)
        self.refused_without_write(policy=policy)

    def test_previous_nineteen_receipt_refused_even_when_its_graph_and_counters_join(self):
        import xml.etree.ElementTree as ET
        q=lambda name:'{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'+name
        path=self.trx()/'focused.trx';doc=ET.parse(path);run=doc.getroot()
        added=('CareerLifecycleAcceptanceTests.','CareerOriginalLevelReadHttpTests.','CareerOriginalMutationParityHttpTests.','CareerOriginalPaginationParityHttpTests.','CareerOriginalOpenPositionParityHttpTests.','CareerLevelCollectionWireParityHttpTests.')
        for group in ('TestDefinitions','Results'):
            nodes=run.find(q(group))
            for node in list(nodes):
                if any(cls in node.get('name',node.get('testName','')) for cls in added):nodes.remove(node)
            self.assertEqual(len(nodes),19)
        counts=run.find(q('ResultSummary')).find(q('Counters'))
        for name in ('total','executed','passed'):counts.set(name,'19')
        doc.write(path)
        with self.assertRaises(ValueError):candidate.verify_focused(path.parent)

    def test_reviewed_filter_and_workflow_are_exact_and_reject_overbroad_lifecycle(self):
        workflow=HERE.parent/'.github/workflows/_build-and-test.yml'
        self.assertEqual(candidate.verify_workflow_filter(workflow),candidate.FOCUSED_FILTER)
        prefix='FullyQualifiedName~Legacy.Maliev.CareerService.Tests.Controllers.CareerLifecycleAcceptanceTests'
        specific=prefix+'.Delete_OriginalEmptyDatabaseIntMaxReturns404AfterFreshLiveDecision'
        for expression in (candidate.FOCUSED_FILTER.replace(specific,prefix),candidate.FOCUSED_FILTER+'|'+prefix,
                           candidate.FOCUSED_FILTER.replace('CareerOriginalMutationParityHttpTests','ForeignTests')):
            with self.assertRaises(ValueError):candidate.verify_focused_filter(expression)
        mutated=self.root/'workflow.yml'
        mutated.write_text(workflow.read_text().replace(specific,prefix))
        with self.assertRaises(ValueError):candidate.verify_workflow_filter(mutated)

    def test_joined_foreign_methods_and_new_theory_arguments_do_not_prove_reviewed_roster(self):
        import xml.etree.ElementTree as ET
        q=lambda name:'{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'+name
        for find,replace in (('LevelExist_ShouldReturnRequestedLevelId','InventedMethod'),
                            ('firstYear: 2000','firstYear: 2001'),('kind: "offer"','kind: "foreign"')):
            path=self.trx()/'focused.trx';doc=ET.parse(path);run=doc.getroot()
            node=next(n for n in run.find(q('TestDefinitions')) if find in n.get('name',''))
            old=node.get('name');new=old.replace(find,replace);node.set('name',new)
            if '(' not in old:node.find(q('TestMethod')).set('name',replace)
            ident=node.get('id')
            next(n for n in run.find(q('Results')) if n.get('testId')==ident).set('testName',new)
            doc.write(path)
            with self.assertRaises(ValueError):candidate.verify_focused(path.parent)


    def test_previous_eleven_file_policy_cannot_hydrate_twelve_binding(self):
        policy=copy.deepcopy(self.policy)
        policy['files']=[row for row in policy['files'] if Path(row['path']).name not in {'CareerOriginalOpenPositionParityHttpTests.cs','CareerModels.cs','CareerApplicationService.cs','CareerSelectedDeletionIsolationHttpTests.cs','CareerSharedLevelHttpTests.cs','CareerLevelCollectionWireParityHttpTests.cs','CareerSourcePaginationCardinalityHttpTests.cs'}]
        self.assertEqual(len(policy['files']),11)
        self.refused_without_write(policy=policy)

    def test_previous_joined_thirty_five_receipt_cannot_prove_thirty_seven(self):
        import xml.etree.ElementTree as ET
        q=lambda name:'{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'+name
        path=self.trx()/'focused.trx';doc=ET.parse(path);run=doc.getroot()
        for group in ('TestDefinitions','Results'):
            nodes=run.find(q(group))
            for node in list(nodes):
                if any(cls in node.get('name',node.get('testName','')) for cls in ('CareerOriginalOpenPositionParityHttpTests.','CareerLevelCollectionWireParityHttpTests.')):nodes.remove(node)
            self.assertEqual(len(nodes),35)
        for name in ('total','executed','passed'):run.find(q('ResultSummary')).find(q('Counters')).set(name,'35')
        doc.write(path)
        with self.assertRaises(ValueError):candidate.verify_focused(path.parent)


    def snapshot(self):
        return {p.relative_to(self.root).as_posix():p.read_bytes() for p in self.root.rglob('*') if p.is_file()}

    def test_archive_read_is_bounded_and_reader_uses_the_same_verified_bytes(self):
        from unittest.mock import MagicMock
        raw=self.archive.read_bytes();actual=Path.open;stream=MagicMock()
        stream.__enter__.return_value.read.return_value=raw
        def open_file(path,*args,**kwargs):
            return stream if path==self.archive else actual(path,*args,**kwargs)
        with patch.object(Path,'open',autospec=True,side_effect=open_file):
            candidate.hydrate(self.root,self.archive,self.policy)
        stream.__enter__.return_value.read.assert_called_once_with(131073)
        self.assertEqual(stream.__enter__.call_count,1)

    def repack(self, name, mutate):
        path=self.root/name
        with zipfile.ZipFile(self.archive) as source,zipfile.ZipFile(path,'w',compression=zipfile.ZIP_DEFLATED) as target:
            for index,info in enumerate(source.infolist()):
                data=source.read(info);info,data=mutate(index,info,data)
                target.writestr(info,data)
        policy=copy.deepcopy(self.policy)
        policy['archiveBytes']=path.stat().st_size;policy['archiveSha256']=candidate.digest(path.read_bytes())
        return path,policy

    def test_all_eighteen_deflated_entries_keep_existing_encoded_and_decoded_caps(self):
        self.assertLessEqual(self.archive.stat().st_size,131072)
        with zipfile.ZipFile(self.archive) as z:
            self.assertEqual(len(z.infolist()),18)
            self.assertEqual({i.filename for i in z.infolist()},candidate.ALLOWED_PATHS)
            self.assertEqual(sum(i.file_size for i in z.infolist()),148729)
            for info in z.infolist():
                self.assertEqual(info.compress_type,zipfile.ZIP_DEFLATED)
                self.assertLessEqual(info.file_size,32768)
                row=next(row for row in self.policy['files'] if row['path']==info.filename)
                self.assertEqual(candidate.digest(z.read(info)),row['sha256'])

    def test_decoded_oversized_entry_refused_before_any_source_write(self):
        def mutate(index,info,data):
            return info,b'x'*32769 if index==0 else data
        path,policy=self.repack('decoded-oversize.zip',mutate)
        policy['files'][0]['bytes']=32769;policy['files'][0]['sha256']=candidate.digest(b'x'*32769)
        self.refused_without_write(path,policy)

    def test_encoded_archive_limit_is_not_relaxed_for_compression(self):
        path=self.root/'encoded-oversize.zip';path.write_bytes(self.archive.read_bytes()+b'x'*131073)
        policy=copy.deepcopy(self.policy);policy['archiveBytes']=path.stat().st_size;policy['archiveSha256']=candidate.digest(path.read_bytes())
        self.refused_without_write(path,policy)

    def test_unreviewed_stored_method_is_refused_even_with_recomputed_archive_pin(self):
        def mutate(index,info,data):
            if index==0:info.compress_type=zipfile.ZIP_STORED
            return info,data
        path,policy=self.repack('stored-entry.zip',mutate)
        self.refused_without_write(path,policy)

    def test_zip_symlink_entry_is_refused_before_any_source_write(self):
        import stat
        def mutate(index,info,data):
            if index==0:info.external_attr=(stat.S_IFLNK|0o777)<<16
            return info,data
        path,policy=self.repack('symlink-entry.zip',mutate)
        self.refused_without_write(path,policy)

    def test_duplicate_policy_row_cannot_collapse_into_eighteen_paths(self):
        policy=copy.deepcopy(self.policy);policy['files'].append(copy.deepcopy(policy['files'][0]))
        self.refused_without_write(policy=policy)

    def test_unreviewed_application_path_cannot_use_new_production_scope(self):
        policy=copy.deepcopy(self.policy);policy['files'][0]['path']='Legacy.Maliev.CareerService.Application/Models/Unexpected.cs'
        self.refused_without_write(policy=policy)

    def test_each_ancestor_and_target_link_is_refused_before_any_write(self):
        paths=['Legacy.Maliev.CareerService.Application',
               'Legacy.Maliev.CareerService.Application/Models',self.policy['files'][0]['path']]
        actual=candidate._is_link
        for name in paths:
            with self.subTest(path=name),patch.object(candidate,'_is_link',side_effect=lambda p: p==self.root/name or actual(p)):
                self.refused_without_write()

    def test_windows_junction_metadata_is_included_in_link_refusal(self):
        path=self.root/'Legacy.Maliev.CareerService.Application'
        with patch.object(type(path),'is_junction',create=True,return_value=True):
            self.assertTrue(candidate._is_link(path))
            self.refused_without_write()

    def test_late_replacement_failure_restores_all_existing_and_absent_preimages(self):
        before=self.snapshot();actual=candidate._replace_bytes;calls=0
        def replace(target,data):
            nonlocal calls
            calls+=1
            if calls==8:raise OSError('controlled late replacement failure')
            return actual(target,data)
        with patch.object(candidate,'_replace_bytes',side_effect=replace):
            with self.assertRaisesRegex(ValueError,'rollback verified'):
                candidate.hydrate(self.root,self.archive,self.policy)
        self.assertEqual(before,self.snapshot())

    def test_error_after_atomic_replacement_is_also_rolled_back(self):
        before=self.snapshot();actual=candidate._replace_bytes;calls=0
        def replace(target,data):
            nonlocal calls
            calls+=1;actual(target,data)
            if calls==8:raise OSError('controlled post-replacement failure')
        with patch.object(candidate,'_replace_bytes',side_effect=replace):
            with self.assertRaisesRegex(ValueError,'rollback verified'):
                candidate.hydrate(self.root,self.archive,self.policy)
        self.assertEqual(before,self.snapshot())

    def test_readback_failure_rolls_back_all_eighteen_sources(self):
        before=self.snapshot();actual=candidate._replace_bytes;read=Path.read_bytes;calls=0;injected=False
        target=self.root/self.policy['files'][0]['path']
        def replace(path,data):
            nonlocal calls
            actual(path,data);calls+=1
        def observe(path):
            nonlocal injected
            if path==target and calls==18 and not injected:
                injected=True;return b'controlled readback mismatch'
            return read(path)
        with patch.object(candidate,'_replace_bytes',side_effect=replace),patch.object(Path,'read_bytes',autospec=True,side_effect=observe):
            with self.assertRaisesRegex(ValueError,'rollback verified'):
                candidate.hydrate(self.root,self.archive,self.policy)
        self.assertTrue(injected);self.assertEqual(before,self.snapshot())

    def test_atomic_temporary_is_removed_after_os_replace_failure(self):
        before=self.snapshot()
        with patch.object(candidate.os,'replace',side_effect=OSError('controlled atomic failure')):
            with self.assertRaisesRegex(ValueError,'rollback verified'):
                candidate.hydrate(self.root,self.archive,self.policy)
        self.assertEqual(before,self.snapshot())
        self.assertFalse(list(self.root.rglob('.career-candidate-*')))

    def test_rollback_failure_is_explicit_and_never_claims_verified(self):
        actual=candidate._replace_bytes;calls=0;first=self.root/self.policy['files'][0]['path']
        original=first.read_bytes()
        def replace(target,data):
            nonlocal calls
            calls+=1
            if calls==8 or (target==first and data==original):raise OSError('controlled rollback failure')
            return actual(target,data)
        with patch.object(candidate,'_replace_bytes',side_effect=replace):
            with self.assertRaisesRegex(ValueError,'rollback incomplete: .*CareerModels.cs'):
                candidate.hydrate(self.root,self.archive,self.policy)
        self.assertNotEqual(first.read_bytes(),original)

    def test_rollback_preserves_foreign_change_in_an_already_written_target(self):
        actual=candidate._replace_bytes;calls=0;first=self.root/self.policy['files'][0]['path']
        def replace(target,data):
            nonlocal calls
            calls+=1
            if calls==8:
                first.write_bytes(b'foreign concurrent content');raise OSError('controlled failure')
            return actual(target,data)
        with patch.object(candidate,'_replace_bytes',side_effect=replace):
            with self.assertRaisesRegex(ValueError,'rollback incomplete'):
                candidate.hydrate(self.root,self.archive,self.policy)
        self.assertEqual(first.read_bytes(),b'foreign concurrent content')

    def test_late_unwritten_foreign_change_is_preserved_and_prior_writes_restored(self):
        before=self.snapshot();actual=candidate._replace_bytes;calls=0;last=self.root/self.policy['files'][-1]['path']
        def replace(target,data):
            nonlocal calls
            calls+=1;actual(target,data)
            if calls==1:last.write_bytes(b'foreign future preimage')
        with patch.object(candidate,'_replace_bytes',side_effect=replace):
            with self.assertRaisesRegex(ValueError,'rollback incomplete'):
                candidate.hydrate(self.root,self.archive,self.policy)
        expected=before.copy();expected[last.relative_to(self.root).as_posix()]=b'foreign future preimage'
        self.assertEqual(expected,self.snapshot())

    def test_corrupt_compressed_stream_refused_before_any_source_write(self):
        import struct,zlib
        raw=bytearray(self.archive.read_bytes())
        with zipfile.ZipFile(self.archive) as z:info=z.infolist()[-1]
        nameLength,extraLength=struct.unpack_from('<HH',raw,info.header_offset+26)
        start=info.header_offset+30+nameLength+extraLength
        raw[start+info.compress_size//2]^=1
        path=self.root/'corrupt-stream.zip';path.write_bytes(raw)
        policy=copy.deepcopy(self.policy);policy['archiveBytes']=len(raw);policy['archiveSha256']=candidate.digest(raw)
        before=self.snapshot()
        with self.assertRaises((ValueError,zipfile.BadZipFile,zlib.error)):
            candidate.hydrate(self.root,path,policy)
        self.assertEqual(before,self.snapshot())

    def test_previous_twelve_file_policy_cannot_hydrate_complete_eighteen_binding(self):
        added={'CareerModels.cs','CareerApplicationService.cs','CareerSelectedDeletionIsolationHttpTests.cs','CareerSharedLevelHttpTests.cs','CareerLevelCollectionWireParityHttpTests.cs','CareerSourcePaginationCardinalityHttpTests.cs'}
        policy=copy.deepcopy(self.policy);policy['files']=[r for r in policy['files'] if Path(r['path']).name not in added]
        self.assertEqual(len(policy['files']),12);self.refused_without_write(policy=policy)

    def test_joined_previous_thirty_seven_receipt_cannot_prove_forty(self):
        import xml.etree.ElementTree as ET
        q=lambda name:'{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'+name
        path=self.trx()/'focused.trx';doc=ET.parse(path);run=doc.getroot()
        for group in ('TestDefinitions','Results'):
            nodes=run.find(q(group))
            for node in list(nodes):
                if 'CareerLevelCollectionWireParityHttpTests.' in node.get('name',node.get('testName','')):nodes.remove(node)
            self.assertEqual(len(nodes),37)
        for name in ('total','executed','passed'):run.find(q('ResultSummary')).find(q('Counters')).set(name,'37')
        doc.write(path)
        with self.assertRaises(ValueError):candidate.verify_focused(path.parent)

    def test_joined_unreviewed_linked_level_theory_argument_is_refused(self):
        import xml.etree.ElementTree as ET
        q=lambda name:'{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'+name
        path=self.trx()/'focused.trx';doc=ET.parse(path);run=doc.getroot()
        node=next(n for n in run.find(q('TestDefinitions')) if 'linked: False' in n.get('name',''))
        name=node.get('name').replace('linked: False','linked: null');node.set('name',name)
        next(n for n in run.find(q('Results')) if n.get('testId')==node.get('id')).set('testName',name)
        doc.write(path)
        with self.assertRaises(ValueError):candidate.verify_focused(path.parent)


    def test_previous_seventeen_policy_refused_without_any_source_write(self):
        policy=copy.deepcopy(self.policy)
        policy['files']=[r for r in policy['files'] if not r['path'].endswith('CareerSourcePaginationCardinalityHttpTests.cs')]
        self.assertEqual(len(policy['files']),17)
        self.refused_without_write(policy=policy)

if __name__=='__main__':unittest.main()
