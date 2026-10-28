"""Source-analysis request trust boundaries; no compiler or game execution here."""
from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from types import SimpleNamespace
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
from vanilla_data import source_analysis as analysis


def observation():
    """Invented compiler output with the actual envelope, never original evidence."""
    identity = 'Example.Demo.Tick(int)'
    return {
        'schemaVersion': 1, 'kind': 'source-semantic-inventory', 'accepted': True,
        'analysisOnly': True, 'sourceClosureComplete': True, 'entries': [identity],
        'methods': [{'id': identity, 'path': 'Demo.cs', 'line': 1, 'depth': 0,
                     'type': 'Example.Demo', 'name': 'Tick', 'parameterTypes': ['int'], 'qualified': True,
                     'diagnostics': [], 'calls': [], 'accesses': [], 'uncertainties': []}],
        'errors': [], 'frontiers': [], 'compilationErrorCount': 0,
        'sourceSha256': {'Demo.cs': 'a' * 64}, 'referenceSha256': {'Reference.dll': 'b' * 64},
        'compiler': 'Invented Roslyn identity for validator tests',
        'limits': ['Analysis only; this fixture is not a port or behavior qualification.'],
    }


class SourceAnalysisRequestTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.folder = Path(temporary.name)
        self.source = self.folder / 'source'
        self.source.mkdir()
        (self.source / 'Demo.cs').write_text(
            'namespace Example; public class Demo { public void Tick(int value, string name) {} '
            'public void Tick(string name) {} public void Tick() {} }', encoding='utf-8')
        (self.source / 'Helpers.cs').write_text('namespace Example; public class Helpers {}', encoding='utf-8')
        self.assembly = self.folder / 'Reference.dll'
        self.assembly.write_bytes(b'never loaded or executed')
        self.request = {
            'schemaVersion': 1,
            'sourceRoot': str(self.source),
            'assembly': str(self.assembly),
            'sources': ['Demo.cs', 'Helpers.cs'],
            'entries': [{'path': 'Demo.cs', 'type': 'Example.Demo', 'method': 'Tick',
                         'parameterTypes': ['int', 'System.String']}],
        }

    def test_explicit_allowlist_and_overload_identity_preserve_distinct_requests(self):
        request = copy.deepcopy(self.request)
        request['entries'].append({'path': 'Demo.cs', 'type': 'Example.Demo', 'method': 'Tick',
                                   'parameterTypes': ['System.String']})
        normalized = analysis.validate_request(request)
        self.assertIsInstance(normalized, dict)
        self.assertEqual(2, len(normalized['entries']))
        self.assertNotEqual(normalized['entries'][0]['parameterTypes'], normalized['entries'][1]['parameterTypes'])
        self.assertEqual(1, normalized['maxDepth'])
        self.assertEqual(32, normalized['maxMethods'])

    def test_parameterless_entry_and_explicit_budget_endpoints_are_supported(self):
        for depth, methods in ((0, 1), (4, 128)):
            request = copy.deepcopy(self.request)
            request.update(maxDepth=depth, maxMethods=methods)
            request['entries'][0]['parameterTypes'] = []
            with self.subTest(depth=depth, methods=methods):
                normalized = analysis.validate_request(request)
                self.assertEqual((depth, methods), (normalized['maxDepth'], normalized['maxMethods']))

    def test_missing_required_fields_are_not_filled_with_analysis_guesses(self):
        for name in ('schemaVersion', 'sourceRoot', 'assembly', 'sources', 'entries'):
            request = copy.deepcopy(self.request)
            del request[name]
            with self.subTest(name=name), self.assertRaises(ValueError):
                analysis.validate_request(request)

    def test_unknown_keys_at_both_levels_are_rejected(self):
        for level in ('request', 'entry'):
            request = copy.deepcopy(self.request)
            target = request if level == 'request' else request['entries'][0]
            target['assumeCompatible'] = True
            with self.subTest(level=level), self.assertRaises(ValueError):
                analysis.validate_request(request)

    def test_bad_envelope_and_boolean_schema_version_are_rejected(self):
        for value in (None, [], 'source', True):
            with self.subTest(value=value), self.assertRaises(ValueError):
                analysis.validate_request(value)
        for value in (True, 1.0, '1', 0, 2):
            request = copy.deepcopy(self.request)
            request['schemaVersion'] = value
            with self.subTest(version=value), self.assertRaises(ValueError):
                analysis.validate_request(request)

    def test_budgets_cannot_be_bools_strings_fractions_or_outside_bounds(self):
        cases = {'maxDepth': (-1, 5, True, False, '1', 1.5, None),
                 'maxMethods': (0, 129, True, False, '32', 2.5, None)}
        for name, values in cases.items():
            for value in values:
                request = copy.deepcopy(self.request)
                request[name] = value
                with self.subTest(name=name, value=value), self.assertRaises(ValueError):
                    analysis.validate_request(request)

    def test_empty_or_nonstring_input_locations_are_rejected(self):
        for name in ('sourceRoot', 'assembly'):
            for value in ('', '   ', None, True, 7, ['a']):
                request = copy.deepcopy(self.request)
                request[name] = value
                with self.subTest(name=name, value=value), self.assertRaises(ValueError):
                    analysis.validate_request(request)

    def test_escaped_absolute_drive_unc_and_noncanonical_source_paths_are_rejected(self):
        paths = ('../Demo.cs', 'a/../../Demo.cs', '/Demo.cs', 'C:/Demo.cs', 'C:Demo.cs',
                 '//server/share/Demo.cs', r'\\server\share\Demo.cs', r'a\Demo.cs',
                 './Demo.cs', 'a/./Demo.cs', 'a//Demo.cs', 'a/../Demo.cs', 'Demo.txt', '', 'Demo.cs\0')
        for value in paths:
            request = copy.deepcopy(self.request)
            request['sources'] = [value]
            request['entries'][0]['path'] = value
            with self.subTest(path=value), self.assertRaises(ValueError):
                analysis.validate_request(request)

    def test_duplicate_windows_paths_do_not_create_multiple_source_identities(self):
        for names in (['Demo.cs', 'Demo.cs'], ['Demo.cs', 'demo.cs'], ['A/Demo.cs', 'a/demo.cs']):
            request = copy.deepcopy(self.request)
            request['sources'] = names
            request['entries'][0]['path'] = names[0]
            with self.subTest(names=names), self.assertRaises(ValueError):
                analysis.validate_request(request)

    def test_entry_file_must_belong_to_the_explicit_source_allowlist(self):
        request = copy.deepcopy(self.request)
        request['entries'][0]['path'] = 'NotRequested.cs'
        with self.assertRaises(ValueError):
            analysis.validate_request(request)

    def test_empty_and_malformed_source_or_entry_lists_are_rejected(self):
        cases = [('sources', []), ('sources', 'Demo.cs'), ('sources', [17]),
                 ('entries', []), ('entries', {}), ('entries', [None]), ('entries', ['Tick'])]
        for name, value in cases:
            request = copy.deepcopy(self.request)
            request[name] = value
            with self.subTest(name=name, value=value), self.assertRaises(ValueError):
                analysis.validate_request(request)

    def test_source_allowlist_has_a_real_256_file_bound(self):
        request = copy.deepcopy(self.request)
        request['sources'] = ['Demo.cs'] + [f'Other{i:03}.cs' for i in range(255)]
        # This test concerns the admitted manifest size, not filesystem probing.
        for name in request['sources'][1:]:
            (self.source / name).write_text('// explicit source input', encoding='utf-8')
        self.assertEqual(256, len(analysis.validate_request(request)['sources']))
        request['sources'].append('OneTooMany.cs')
        with self.assertRaises(ValueError):
            analysis.validate_request(request)

    def test_entry_identity_and_parameter_projection_are_complete(self):
        for name in ('path', 'type', 'method', 'parameterTypes'):
            request = copy.deepcopy(self.request)
            del request['entries'][0][name]
            with self.subTest(name=name), self.assertRaises(ValueError):
                analysis.validate_request(request)
        for name in ('type', 'method'):
            for value in ('', '  ', None, True, 17):
                request = copy.deepcopy(self.request)
                request['entries'][0][name] = value
                with self.subTest(name=name, value=value), self.assertRaises(ValueError):
                    analysis.validate_request(request)
        for value in ('int', None, [True], [17], [''], ['int; System.String']):
            request = copy.deepcopy(self.request)
            request['entries'][0]['parameterTypes'] = value
            with self.subTest(parameters=value), self.assertRaises(ValueError):
                analysis.validate_request(request)

    def test_malformed_request_never_starts_any_subprocess(self):
        cases = []
        for field, value in (('maxDepth', True), ('maxMethods', 129), ('sources', ['../Outside.cs'])):
            request = copy.deepcopy(self.request)
            request[field] = value
            cases.append(json.dumps(request))
        valid_json = json.dumps(self.request)
        cases.extend(('not JSON', '{"schemaVersion":1,' + valid_json[1:]))
        for index, raw in enumerate(cases):
            request_path = self.folder / f'request-{index}.json'
            request_path.write_text(raw, encoding='utf-8')
            output = self.folder / f'evidence-{index}'
            with self.subTest(index=index), patch('subprocess.Popen', side_effect=AssertionError('Compiler must not start')) as start:
                with self.assertRaises(ValueError):
                    analysis.analyze(request_path, output)
                start.assert_not_called()


class SourceAnalysisObservationTests(unittest.TestCase):
    def test_complete_span_inventory_can_be_qualified(self):
        analysis.validate_observation(observation())

    def test_resolved_metadata_frontier_is_explicitly_incomplete_without_faking_rejection(self):
        data = observation()
        data['sourceClosureComplete'] = False
        data['frontiers'] = ['metadata-or-nonmethod-body: System.Math.Abs(int)']
        data['methods'][0]['calls'] = [{'offset': 42, 'line': 1, 'symbol': 'System.Math.Abs(int)',
                                      'dispatch': 'direct', 'origin': 'metadata-or-nonmethod-body',
                                      'rng': False, 'refArguments': False}]
        analysis.validate_observation(data)
        data['sourceClosureComplete'] = True
        with self.assertRaises(ValueError):
            analysis.validate_observation(data)

    def test_unrelated_global_compilation_errors_remain_visible_in_narrow_inventory(self):
        data = observation()
        data['compilationErrorCount'] = 2
        analysis.validate_observation(data)

    def test_absent_entry_is_valid_rejected_evidence_instead_of_an_output_schema_crash(self):
        data = observation()
        data.update(accepted=False, sourceClosureComplete=False, entries=[], methods=[],
                    errors=['entry-not-unique: Example.Demo.Missing'])
        analysis.validate_observation(data)

    def test_boolean_or_noninteger_schema_version_is_refused(self):
        for value in (True, 1.0, '1'):
            data = observation(); data['schemaVersion'] = value
            with self.subTest(value=value), self.assertRaises(ValueError):
                analysis.validate_observation(data)

    def test_missing_or_incomplete_input_pins_are_refused(self):
        for field in ('sourceSha256', 'referenceSha256'):
            for value in (None, {}, {'Demo.cs': 'not-a-sha256'}, {'Demo.cs': True}):
                data = observation(); data[field] = value
                with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                    analysis.validate_observation(data)

    def test_duplicate_entries_or_method_ids_cannot_fake_complete_qualification(self):
        for field in ('entries', 'methods'):
            data = observation(); data[field].append(copy.deepcopy(data[field][0]))
            with self.subTest(field=field), self.assertRaises(ValueError):
                analysis.validate_observation(data)

    def test_entry_ids_cannot_be_missing_or_outside_visited_methods_on_green(self):
        for value in (None, '', 17, 'Example.Other.Tick(int)'):
            data = observation(); data['entries'] = [value]
            with self.subTest(entry=value), self.assertRaises(ValueError):
                analysis.validate_observation(data)
        for value in (None, '', 17):
            data = observation(); data['methods'][0]['id'] = value; data['entries'] = [value]
            with self.subTest(method=value), self.assertRaises(ValueError):
                analysis.validate_observation(data)

    def test_every_visited_method_must_qualify_before_green_is_allowed(self):
        data = observation()
        other = copy.deepcopy(data['methods'][0]); other.update(id='Example.Demo.Helper()', name='Helper', parameterTypes=[], qualified=False)
        data['methods'].append(other)
        with self.assertRaises(ValueError):
            analysis.validate_observation(data)

    def test_diagnostics_dynamic_and_unresolved_calls_cannot_be_marked_qualified(self):
        for mode in ('diagnostic', 'dynamic', 'unresolved'):
            data = observation()
            if mode == 'diagnostic':
                data['methods'][0]['diagnostics'] = [{'id': 'CS0103', 'line': 1, 'severity': 'error'}]
            else:
                data['methods'][0]['calls'] = [{'offset': 42, 'line': 1, 'symbol': None,
                    'dispatch': 'dynamic' if mode == 'dynamic' else 'direct', 'origin': 'unresolved',
                    'rng': False, 'refArguments': False}]
            with self.subTest(mode=mode), self.assertRaises(ValueError):
                analysis.validate_observation(data)

    def test_real_budget_cutoff_errors_cannot_be_marked_green(self):
        for error in ('depth-limit: Example.Demo.Helper()', 'method-limit: Example.Demo.Helper()', 'operation-limit'):
            data = observation(); data['errors'] = [error]
            with self.subTest(error=error), self.assertRaises(ValueError):
                analysis.validate_observation(data)

    def test_omitted_uncertainty_frontier_cannot_claim_closed_source_inventory(self):
        data = observation()
        data['methods'][0]['uncertainties'] = ['ref-out-alias-effects-not-proven']
        with self.assertRaises(ValueError):
            analysis.validate_observation(data)

    def test_malformed_nested_method_or_call_has_a_controlled_validation_failure(self):
        for mode in ('method', 'call'):
            data = observation()
            if mode == 'method': data['methods'] = [None]
            else: data['methods'][0]['calls'] = [None]
            with self.subTest(mode=mode), self.assertRaises(ValueError):
                analysis.validate_observation(data)


class SourceAnalysisOrchestrationTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.folder = Path(temporary.name)
        source = self.folder / 'source'; source.mkdir(); self.source = source
        (source / 'Demo.cs').write_text('namespace Example; class Demo { static void Tick(int x) {} }', encoding='utf-8')
        reference = self.folder / 'reference'; reference.mkdir()
        self.assembly = reference / 'Reference.dll'; self.assembly.write_bytes(b'never executed')
        self.request = self.folder / 'request.json'
        self.request.write_text(json.dumps(dict(schemaVersion=1, sourceRoot=str(source), assembly=str(self.assembly),
            sources=['Demo.cs'], entries=[dict(path='Demo.cs', type='Example.Demo', method='Tick', parameterTypes=['int'])])), encoding='utf-8')
        self.reference_sha = hashlib.sha256(self.assembly.read_bytes()).hexdigest()
        self.before = dict(sources={'Demo.cs': 'a' * 64}, references={'Reference.dll': self.reference_sha},
                           tools={'tools/vanilla_data/source_analysis.py': 'c' * 64})
        self.pins = {'SourceProbe.dll': 'd' * 64, 'Microsoft.CodeAnalysis.dll': 'e' * 64,
                     'Microsoft.CodeAnalysis.CSharp.dll': 'f' * 64}

    def invoke(self, mode=None):
        self.input_reads = 0
        self.dll_reads = 0
        if mode == 'method-budget':
            request = json.loads(self.request.read_text(encoding='utf-8'))
            request['maxMethods'] = 1
            self.request.write_text(json.dumps(request), encoding='utf-8')
        def inputs(_):
            self.input_reads += 1
            data = copy.deepcopy(self.before)
            if mode == 'source-drift' and self.input_reads > 1: data['sources']['Demo.cs'] = '0' * 64
            return data
        def process(args, **kwargs):
            if args[1] == 'build': return SimpleNamespace(returncode=1 if mode == 'build-fails' else 0)
            if mode == 'probe-fails': return SimpleNamespace(returncode=9)
            if mode == 'timeout': raise analysis.subprocess.TimeoutExpired(args, 90)
            data = observation(); data['referenceSha256'] = dict(self.before['references'])
            if mode == 'backend-rejects':
                data.update(accepted=False, sourceClosureComplete=False, methods=[], entries=[],
                            errors=['entry-not-unique: Example.Demo.Tick'])
            elif mode == 'wrong-source': data['sourceSha256']['Demo.cs'] = '0' * 64
            elif mode == 'foreign-entry':
                data['entries'] = ['Example.Demo.Other(int)']; data['methods'][0].update(id=data['entries'][0], name='Other')
            elif mode == 'missing-one-entry': data['entries'] = []
            elif mode == 'outside-allowlist': data['methods'][0]['path'] = 'Other.cs'
            elif mode == 'depth-overrun': data['methods'][0]['depth'] = 2
            elif mode == 'method-budget':
                helper = copy.deepcopy(data['methods'][0]); helper.update(id='Example.Demo.Helper()', name='Helper', parameterTypes=[], depth=1)
                data['methods'].append(helper)
            elif mode == 'bad-shape': data['methods'] = [None]
            output = Path(args[-1])
            self.observed_bytes = json.dumps(data).encode('utf-8')
            output.write_bytes(self.observed_bytes)
            return SimpleNamespace(returncode=0)
        def dlls(_):
            self.dll_reads += 1
            result = dict(self.pins)
            if mode == 'dll-drift' and self.dll_reads > 1: result['SourceProbe.dll'] = '0' * 64
            return result
        validate = analysis.validate_observation
        def validate_and_check_rewrite(data):
            result = validate(data)
            if mode == 'observation-change':
                (self.folder / 'evidence/observation.json').write_bytes(self.observed_bytes + b'\n')
            return result
        with patch.object(analysis.facts, 'reference', return_value={'assemblySha256': self.reference_sha, 'dependenciesSha256': {}}), \
             patch.object(analysis, '_inputs', side_effect=inputs), \
             patch.object(analysis, 'dll_pins', side_effect=dlls), \
             patch.object(analysis, 'validate_observation', side_effect=validate_and_check_rewrite), \
             patch.object(analysis.subprocess, 'run', side_effect=process):
            return analysis.analyze(self.request, self.folder / 'evidence')

    def test_fresh_qualified_inventory_retains_consumed_observation_and_exact_hash(self):
        result = self.invoke()
        self.assertTrue(result['accepted'], result)
        self.assertTrue(result['analysisOnly'])
        self.assertEqual(hashlib.sha256(self.observed_bytes).hexdigest(), result['observationSha256'])
        self.assertTrue((self.folder / 'evidence/result.json').is_file())

    def test_rejected_entry_selection_retains_negative_evidence(self):
        result = self.invoke('backend-rejects')
        self.assertFalse(result['accepted'])
        self.assertTrue((self.folder / 'evidence/observation.json').is_file())
        self.assertTrue((self.folder / 'evidence/result.json').is_file())
        self.assertIn('entry-not-unique: Example.Demo.Tick', result['analysisErrors'])

    def test_equal_fake_green_cannot_analyze_a_different_entry(self):
        result = self.invoke('foreign-entry')
        self.assertFalse(result['accepted'])
        self.assertTrue(result['validationErrors'])

    def test_incomplete_requested_entry_set_cannot_be_green(self):
        result = self.invoke('missing-one-entry')
        self.assertFalse(result['accepted'])
        self.assertTrue(result['validationErrors'])

    def test_source_provenance_and_midrun_drift_reject_qualified_output(self):
        for mode in ('wrong-source', 'source-drift'):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as temporary:
                self.folder = Path(temporary)
                # Paths in the valid request remain the original extant inputs.
                self.request = self.folder / 'request.json'
                original = dict(schemaVersion=1, sourceRoot=str(self.source), assembly=str(self.assembly),
                    sources=['Demo.cs'], entries=[dict(path='Demo.cs', type='Example.Demo', method='Tick', parameterTypes=['int'])])
                self.request.write_text(json.dumps(original), encoding='utf-8')
                self.assertFalse(self.invoke(mode)['accepted'])

    def test_build_failure_does_not_run_analyzer_or_fabricate_an_observation(self):
        result = self.invoke('build-fails')
        self.assertFalse(result['accepted'])
        self.assertEqual(1, len(result['commands']))
        self.assertFalse((self.folder / 'evidence/observation.json').exists())

    def test_existing_green_output_cannot_exceed_admitted_sources_or_traversal_budget(self):
        for mode in ('outside-allowlist', 'depth-overrun', 'method-budget'):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as temporary:
                self.folder = Path(temporary)
                self.request = self.folder / 'request.json'
                self.request.write_text(json.dumps(dict(schemaVersion=1, sourceRoot=str(self.source), assembly=str(self.assembly),
                    sources=['Demo.cs'], entries=[dict(path='Demo.cs', type='Example.Demo', method='Tick', parameterTypes=['int'])])), encoding='utf-8')
                result = self.invoke(mode)
                self.assertFalse(result['accepted'])
                self.assertTrue(result['validationErrors'])

    def test_rewritten_output_is_rejected_and_sha_still_identifies_validated_bytes(self):
        result = self.invoke('observation-change')
        self.assertFalse(result['accepted'])
        self.assertEqual(hashlib.sha256(self.observed_bytes).hexdigest(), result['observationSha256'])
        self.assertNotEqual(result['observationSha256'], hashlib.sha256((self.folder / 'evidence/observation.json').read_bytes()).hexdigest())

    def test_changed_compiler_dll_closure_cannot_admit_an_otherwise_qualified_result(self):
        result = self.invoke('dll-drift')
        self.assertFalse(result['accepted'])
        self.assertTrue(result['validationErrors'])

    def test_failed_timeout_or_malformed_backend_retains_controlled_rejection(self):
        for mode in ('probe-fails', 'timeout', 'bad-shape'):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as temporary:
                self.folder = Path(temporary)
                result = self.invoke(mode)
                self.assertFalse(result['accepted'])
                self.assertTrue(result['validationErrors'])
                self.assertTrue((self.folder / 'evidence/result.json').is_file())


if __name__ == '__main__':
    unittest.main()
